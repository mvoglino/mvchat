using System.Globalization;
using System.Text.Json;
using MvChat.Web.Infrastructure;
using MvChat.Web.Security;

namespace MvChat.Web.Billing;

/// <summary>Una riga del rendiconto: il canone di un'attività o il suo consumo AI.</summary>
public sealed record StatementLine(int GymId, string Activity, string Kind, string Description, decimal Amount, int AiCalls = 0, decimal AiUsd = 0);

/// <summary>Il rendiconto di un mese per chi riceve la fattura: il gruppo, oppure l'attività singola.</summary>
public sealed class Statement
{
    public string Month { get; set; } = "";
    public int OrganizationId { get; set; }
    public bool IsGroup { get; set; }
    public string RecipientName { get; set; } = "";
    public string? LegalName { get; set; }
    public string? VatNumber { get; set; }
    public string? Address { get; set; }
    public string? Email { get; set; }
    public List<StatementLine> Lines { get; set; } = new();
    public decimal Subtotal { get; set; }
    public decimal VatPct { get; set; }
    public decimal VatAmount { get; set; }
    public decimal Total { get; set; }
    public decimal UsdToEur { get; set; }
    public decimal AiMarkupPct { get; set; }
    public bool Closed { get; set; }
    public DateTime? ClosedAt { get; set; }
    public decimal Fees => Lines.Where(l => l.Kind == "canone").Sum(l => l.Amount);
    public decimal Ai => Lines.Where(l => l.Kind == "ai").Sum(l => l.Amount);
    public int Activities => Lines.Select(l => l.GymId).Distinct().Count();
    /// <summary>Dati per la fattura mancanti: MVitalia deve completarli prima di fatturare.</summary>
    public bool MissingData => string.IsNullOrWhiteSpace(LegalName) || string.IsNullOrWhiteSpace(VatNumber);
}

/// <summary>
/// Calcola i rendiconti mensili: canone per ogni attività abbonata nel mese + consumo AI rifatturato
/// (costo del fornitore in dollari × cambio × ricarico). Il rendiconto è intestato al gruppo se l'attività
/// ne fa parte, altrimenti all'attività. I mesi chiusi si leggono dal database e non cambiano più.
/// </summary>
public sealed class BillingService
{
    private static readonly CultureInfo It = CultureInfo.GetCultureInfo("it-IT");
    private readonly Db _db; private readonly AppConfigStore _config;
    public BillingService(Db db, AppConfigStore config) { _db = db; _config = config; }

    public static bool TryMonth(string? s, out DateTime first) =>
        DateTime.TryParseExact((s ?? "") + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out first);

    public static string MonthName(string month) => TryMonth(month, out var d) ? d.ToString("MMMM yyyy", It) : month;

    /// <summary>Quali destinatari può vedere chi chiede: MVitalia tutti, il gruppo il suo, l'attività singola la sua.</summary>
    private static object ScopeArgs(Scope s) => new { All = s.IsSuperAdmin ? 1 : 0, Org = s.OrganizationId ?? -1, IsOrg = s.IsOrgAdmin ? 1 : 0, IsMgr = s.IsManager ? 1 : 0 };
    private const string ScopeWhere = "(@All=1 OR (o.Id=@Org AND ((@IsOrg=1 AND o.IsGroup=1) OR (@IsMgr=1 AND o.IsGroup=0))))";

    public async Task<List<Statement>> MonthAsync(Scope s, string month)
    {
        if (!TryMonth(month, out var first)) return new();
        var closed = (await ClosedAsync(s, month)).ToDictionary(x => x.OrganizationId);
        var live = await ComputeAsync(s, first);
        foreach (var st in live.Where(x => !closed.ContainsKey(x.OrganizationId))) closed[st.OrganizationId] = st;
        return closed.Values.OrderBy(x => x.RecipientName).ToList();
    }

    private sealed record GymRow(int Id, string Name, int OrgId, string OrgName, bool IsGroup, decimal? Fee, DateTime? From, DateTime? To,
        string? OLegal, string? OVat, string? OAddr, string? OCity, string? OEmail, string? GLegal, string? GVat, string? GAddr, string? GCity, string? GEmail,
        decimal AiUsd, int AiCalls);

    private async Task<List<Statement>> ComputeAsync(Scope s, DateTime first)
    {
        var b = _config.Current.Billing;
        var last = first.AddMonths(1).AddDays(-1);
        var fromUtc = first.FromRome(); var toUtc = first.AddMonths(1).FromRome();
        var rows = await _db.QueryAsync(
            $@"SELECT g.Id, g.Name, o.Id AS OrgId, o.Name AS OrgName, o.IsGroup, g.MonthlyFeeEur, g.FeeStartsOn, g.FeeEndsOn,
                      o.LegalName AS OLegal, o.VatNumber AS OVat, o.Address AS OAddr, o.City AS OCity, COALESCE(o.BillingEmail, o.ContactEmail) AS OEmail,
                      g.LegalName AS GLegal, g.VatNumber AS GVat, g.Address AS GAddr, g.City AS GCity, g.ContactEmail AS GEmail,
                      (SELECT COALESCE(SUM(a.CostUsd),0) FROM AiUsage a WHERE a.GymId=g.Id AND a.CreatedAt>=@fromUtc AND a.CreatedAt<@toUtc) AS AiUsd,
                      (SELECT COUNT(*) FROM AiUsage a WHERE a.GymId=g.Id AND a.Ok=1 AND a.CreatedAt>=@fromUtc AND a.CreatedAt<@toUtc) AS AiCalls
               FROM Gyms g JOIN Organizations o ON o.Id=g.OrganizationId WHERE {ScopeWhere} ORDER BY o.Name, g.Name",
            new { All = s.IsSuperAdmin ? 1 : 0, Org = s.OrganizationId ?? -1, IsOrg = s.IsOrgAdmin ? 1 : 0, IsMgr = s.IsManager ? 1 : 0, fromUtc, toUtc },
            r => new GymRow(r.Int("Id"), r.Str("Name")!, r.Int("OrgId"), r.Str("OrgName")!, r.Bool("IsGroup"),
                r.IsDBNull(r.GetOrdinal("MonthlyFeeEur")) ? null : r.GetDecimal(r.GetOrdinal("MonthlyFeeEur")), r.Date("FeeStartsOn"), r.Date("FeeEndsOn"),
                r.Str("OLegal"), r.Str("OVat"), r.Str("OAddr"), r.Str("OCity"), r.Str("OEmail"), r.Str("GLegal"), r.Str("GVat"), r.Str("GAddr"), r.Str("GCity"), r.Str("GEmail"),
                Convert.ToDecimal(r.GetValue(r.GetOrdinal("AiUsd"))), Convert.ToInt32(r.GetValue(r.GetOrdinal("AiCalls")))));

        var month = first.ToString("yyyy-MM");
        var list = new List<Statement>();
        foreach (var grp in rows.GroupBy(r => r.OrgId))
        {
            var f = grp.First();
            static string? Addr(string? a, string? c) => string.Join(", ", new[] { a, c }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } x ? x : null;
            var st = new Statement
            {
                Month = month, OrganizationId = f.OrgId, IsGroup = f.IsGroup, RecipientName = f.OrgName,
                // Gruppo: dati del gruppo. Attività singola: i suoi dati, altrimenti quelli del suo contenitore.
                LegalName = f.IsGroup ? f.OLegal : f.GLegal ?? f.OLegal,
                VatNumber = f.IsGroup ? f.OVat : f.GVat ?? f.OVat,
                Address = f.IsGroup ? Addr(f.OAddr, f.OCity) : Addr(f.GAddr, f.GCity) ?? Addr(f.OAddr, f.OCity),
                Email = f.IsGroup ? f.OEmail : f.GEmail ?? f.OEmail,
                UsdToEur = b.UsdToEur, AiMarkupPct = b.AiMarkupPct, VatPct = b.VatPct
            };
            foreach (var g in grp)
            {
                var subscribed = g.From is DateTime from && from.Date <= last && (g.To is null || g.To.Value.Date >= first);
                var fee = g.Fee ?? b.DefaultMonthlyFeeEur;
                if (subscribed && fee > 0)
                    st.Lines.Add(new(g.Id, g.Name, "canone", $"Abbonamento mvchat · {MonthName(month)}", Math.Round(fee, 2)));
                if (g.AiUsd > 0)
                {
                    var eur = Math.Round(g.AiUsd * b.UsdToEur * (1 + b.AiMarkupPct / 100m), 2);
                    if (eur > 0)
                        st.Lines.Add(new(g.Id, g.Name, "ai", $"Assistente AI · {g.AiCalls} risposte", eur, g.AiCalls, Math.Round(g.AiUsd, 4)));
                }
            }
            if (st.Lines.Count == 0) continue;
            Totals(st);
            list.Add(st);
        }
        return list;
    }

    private static void Totals(Statement st)
    {
        st.Subtotal = st.Lines.Sum(l => l.Amount);
        st.VatAmount = Math.Round(st.Subtotal * st.VatPct / 100m, 2);
        st.Total = st.Subtotal + st.VatAmount;
    }

    public Task<List<Statement>> ClosedAsync(Scope s, string month) => _db.QueryAsync(
        $@"SELECT b.*, o.IsGroup FROM BillingStatements b JOIN Organizations o ON o.Id=b.OrganizationId WHERE b.Month=@month AND {ScopeWhere}",
        new { month, All = s.IsSuperAdmin ? 1 : 0, Org = s.OrganizationId ?? -1, IsOrg = s.IsOrgAdmin ? 1 : 0, IsMgr = s.IsManager ? 1 : 0 },
        r => new Statement
        {
            Month = r.Str("Month")!, OrganizationId = r.Int("OrganizationId"), IsGroup = r.Bool("IsGroup"), RecipientName = r.Str("RecipientName")!,
            LegalName = r.Str("LegalName"), VatNumber = r.Str("VatNumber"), Address = r.Str("Address"), Email = r.Str("Email"),
            Lines = JsonSerializer.Deserialize<List<StatementLine>>(r.Str("LinesJson")!) ?? new(),
            Subtotal = r.GetDecimal(r.GetOrdinal("Subtotal")), VatPct = r.GetDecimal(r.GetOrdinal("VatPct")), VatAmount = r.GetDecimal(r.GetOrdinal("VatAmount")),
            Total = r.GetDecimal(r.GetOrdinal("Total")), UsdToEur = r.GetDecimal(r.GetOrdinal("UsdToEur")), AiMarkupPct = r.GetDecimal(r.GetOrdinal("AiMarkupPct")),
            Closed = true, ClosedAt = r.Date("ClosedAt")
        });

    /// <summary>Chiude il mese: i rendiconti aperti vengono salvati così come sono e non cambiano più.</summary>
    public async Task<int> CloseAsync(Scope s, string month, int userId)
    {
        var open = (await MonthAsync(s, month)).Where(x => !x.Closed).ToList();
        foreach (var st in open)
            await _db.ExecuteAsync(
                @"INSERT INTO BillingStatements (Month, OrganizationId, RecipientName, LegalName, VatNumber, Address, Email, LinesJson, Subtotal, VatPct, VatAmount, Total, UsdToEur, AiMarkupPct, ClosedBy)
                  VALUES (@Month, @OrganizationId, @RecipientName, @LegalName, @VatNumber, @Address, @Email, @LinesJson, @Subtotal, @VatPct, @VatAmount, @Total, @UsdToEur, @AiMarkupPct, @userId)",
                new { st.Month, st.OrganizationId, st.RecipientName, st.LegalName, st.VatNumber, st.Address, st.Email, LinesJson = JsonSerializer.Serialize(st.Lines),
                      st.Subtotal, st.VatPct, st.VatAmount, st.Total, st.UsdToEur, st.AiMarkupPct, userId });
        return open.Count;
    }

    /// <summary>Riapre un rendiconto chiuso (per correggere): si ricalcola con le regole di adesso.</summary>
    public Task<int> ReopenAsync(string month, int orgId) =>
        _db.ExecuteAsync("DELETE FROM BillingStatements WHERE Month=@month AND OrganizationId=@orgId", new { month, orgId });
}
