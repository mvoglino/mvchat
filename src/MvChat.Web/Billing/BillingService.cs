using System.Globalization;
using System.Text.Json;
using MvChat.Web.Infrastructure;
using MvChat.Web.Security;

namespace MvChat.Web.Billing;

/// <summary>Una riga del rendiconto: il canone di un'attività o il suo consumo AI.</summary>
public sealed record StatementLine(int GymId, string Activity, string Kind, string Description, decimal Amount, int AiCalls = 0, decimal AiUsd = 0);

/// <summary>
/// Il rendiconto di un mese per chi riceve la fattura: il gruppo, l'attività singola,
/// un'attività del gruppo che paga da sé, oppure un gestore con più attività del gruppo.
/// </summary>
public sealed class Statement
{
    public string Month { get; set; } = "";
    public int OrganizationId { get; set; }
    public bool IsGroup { get; set; }
    /// <summary>Chi riceve il rendiconto: o{gruppo o attività singola} · a{attività del gruppo} · g{gestore}.</summary>
    public string RecipientKey { get; set; } = "";
    /// <summary>gruppo · singola · attivita · gestore</summary>
    public string RecipientKind { get; set; } = "";
    public string KindLabel => RecipientKind switch
    {
        "gruppo" => "gruppo", "singola" => "attività singola", "attivita" => "attività del gruppo", "gestore" => "gestore di più attività", _ => RecipientKind
    };
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
/// (costo del fornitore in dollari × cambio × ricarico). Per un'attività di un gruppo il rendiconto va al gruppo,
/// all'attività stessa o al suo gestore (scelta di MVitalia in Gyms.BillTo); l'attività singola riceve il suo.
/// I mesi chiusi si leggono dal database e non cambiano più.
/// </summary>
public sealed class BillingService
{
    private static readonly CultureInfo It = CultureInfo.GetCultureInfo("it-IT");
    private readonly Db _db; private readonly AppConfigStore _config;
    public BillingService(Db db, AppConfigStore config) { _db = db; _config = config; }

    public static bool TryMonth(string? s, out DateTime first) =>
        DateTime.TryParseExact((s ?? "") + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out first);

    public static string MonthName(string month) => TryMonth(month, out var d) ? d.ToString("MMMM yyyy", It) : month;

    /// <summary>
    /// Chi vede quale rendiconto: MVitalia tutti; l'amministratore di gruppo tutti quelli del suo gruppo;
    /// l'amministratore attività quello dell'attività singola o della sua attività se paga da sé;
    /// il responsabile di area quelli intestati alle sue attività o al loro gestore (solo se tutte le righe sono sue attività).
    /// </summary>
    public static bool Visible(Scope s, Statement st)
    {
        if (s.IsSuperAdmin) return true;
        if (st.OrganizationId != s.OrganizationId) return false;
        if (s.IsOrgAdmin) return true;
        if (s.IsManager) return st.RecipientKind == "singola" || st.RecipientKey == "a" + s.GymId;
        if (s.IsAreaManager)
        {
            var mine = s.AreaGymIds;
            return st.RecipientKind is "attivita" or "gestore" && st.Lines.Count > 0 && st.Lines.All(l => mine.Contains(l.GymId));
        }
        return false;
    }

    public async Task<List<Statement>> MonthAsync(Scope s, string month)
    {
        if (!TryMonth(month, out var first)) return new();
        var closed = (await ClosedAsync(s, month)).ToDictionary(x => x.RecipientKey);
        var live = await ComputeAsync(s, first);
        foreach (var st in live.Where(x => !closed.ContainsKey(x.RecipientKey))) closed[st.RecipientKey] = st;
        return closed.Values.Where(x => Visible(s, x)).OrderBy(x => x.RecipientName).ToList();
    }

    private sealed record GymRow(int Id, bool Active, string Name, int OrgId, string OrgName, bool IsGroup, decimal? Fee, DateTime? From, DateTime? To,
        string? OLegal, string? OVat, string? OAddr, string? OCity, string? OEmail, string? GLegal, string? GVat, string? GAddr, string? GCity, string? GEmail,
        decimal AiUsd, int AiCalls, string BillTo, int? EntityId, string? EName, string? ELegal, string? EVat, string? EAddr, string? ECity, string? EEmail);

    private async Task<List<Statement>> ComputeAsync(Scope s, DateTime first)
    {
        var b = _config.Current.Billing;
        var last = first.AddMonths(1).AddDays(-1);
        var fromUtc = first.FromRome(); var toUtc = first.AddMonths(1).FromRome();
        var rows = await _db.QueryAsync(
            $@"SELECT g.Id, g.IsActive AS GActive, g.Name, o.Id AS OrgId, o.Name AS OrgName, o.IsGroup, g.MonthlyFeeEur, g.FeeStartsOn, g.FeeEndsOn,
                      o.LegalName AS OLegal, o.VatNumber AS OVat, o.Address AS OAddr, o.City AS OCity, COALESCE(o.BillingEmail, o.ContactEmail) AS OEmail,
                      g.LegalName AS GLegal, g.VatNumber AS GVat, g.Address AS GAddr, g.City AS GCity, g.ContactEmail AS GEmail,
                      g.BillTo, e.Id AS EId, e.Name AS EName, e.LegalName AS ELegal, e.VatNumber AS EVat, e.Address AS EAddr, e.City AS ECity, e.Email AS EEmail,
                      (SELECT COALESCE(SUM(a.CostUsd),0) FROM AiUsage a WHERE a.GymId=g.Id AND a.CreatedAt>=@fromUtc AND a.CreatedAt<@toUtc) AS AiUsd,
                      (SELECT COUNT(*) FROM AiUsage a WHERE a.GymId=g.Id AND a.Ok=1 AND a.CreatedAt>=@fromUtc AND a.CreatedAt<@toUtc) AS AiCalls
               FROM Gyms g JOIN Organizations o ON o.Id=g.OrganizationId
               LEFT JOIN BillingEntities e ON e.Id=g.BillingEntityId AND e.OrganizationId=g.OrganizationId AND e.IsActive=1
               WHERE (@All=1 OR g.OrganizationId=@Org) ORDER BY o.Name, g.Name",
            // Si calcola tutto il gruppo di chi guarda (un gestore può avere attività di più aree); cosa mostrare lo decide Visible.
            new { All = s.IsSuperAdmin ? 1 : 0, Org = s.OrganizationId ?? -1, fromUtc, toUtc },
            r => new GymRow(r.Int("Id"), r.Bool("GActive"), r.Str("Name")!, r.Int("OrgId"), r.Str("OrgName")!, r.Bool("IsGroup"),
                r.IsDBNull(r.GetOrdinal("MonthlyFeeEur")) ? null : r.GetDecimal(r.GetOrdinal("MonthlyFeeEur")), r.Date("FeeStartsOn"), r.Date("FeeEndsOn"),
                r.Str("OLegal"), r.Str("OVat"), r.Str("OAddr"), r.Str("OCity"), r.Str("OEmail"), r.Str("GLegal"), r.Str("GVat"), r.Str("GAddr"), r.Str("GCity"), r.Str("GEmail"),
                Convert.ToDecimal(r.GetValue(r.GetOrdinal("AiUsd"))), Convert.ToInt32(r.GetValue(r.GetOrdinal("AiCalls"))),
                r.Str("BillTo") ?? "gruppo", r.IntN("EId"), r.Str("EName"), r.Str("ELegal"), r.Str("EVat"), r.Str("EAddr"), r.Str("ECity"), r.Str("EEmail")));

        var month = first.ToString("yyyy-MM");
        var list = new List<Statement>();
        static string? Addr(string? a, string? c) => string.Join(", ", new[] { a, c }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } x ? x : null;
        // Intestatario di ogni attività. Un gestore disattivato o di un altro gruppo non vale: si torna al gruppo.
        static string KeyOf(GymRow g) => !g.IsGroup ? "o" + g.OrgId
            : g.BillTo == "attivita" ? "a" + g.Id
            : g.BillTo == "gestore" && g.EntityId is int e ? "g" + e
            : "o" + g.OrgId;
        foreach (var grp in rows.GroupBy(KeyOf))
        {
            var f = grp.First();
            var key = grp.Key;
            var st = new Statement
            {
                Month = month, OrganizationId = f.OrgId, IsGroup = f.IsGroup, RecipientKey = key,
                UsdToEur = b.UsdToEur, AiMarkupPct = b.AiMarkupPct, VatPct = b.VatPct
            };
            switch (key[0])
            {
                case 'a': // attività del gruppo che paga da sé: i suoi dati fiscali
                    st.RecipientKind = "attivita"; st.RecipientName = f.Name;
                    st.LegalName = f.GLegal; st.VatNumber = f.GVat; st.Address = Addr(f.GAddr, f.GCity); st.Email = f.GEmail;
                    break;
                case 'g': // gestore con più attività
                    st.RecipientKind = "gestore"; st.RecipientName = f.EName ?? "Gestore";
                    st.LegalName = f.ELegal; st.VatNumber = f.EVat; st.Address = Addr(f.EAddr, f.ECity); st.Email = f.EEmail;
                    break;
                default:
                    // Gruppo: dati del gruppo. Attività singola: i suoi dati, altrimenti quelli del suo contenitore.
                    st.RecipientKind = f.IsGroup ? "gruppo" : "singola"; st.RecipientName = f.OrgName;
                    st.LegalName = f.IsGroup ? f.OLegal : f.GLegal ?? f.OLegal;
                    st.VatNumber = f.IsGroup ? f.OVat : f.GVat ?? f.OVat;
                    st.Address = f.IsGroup ? Addr(f.OAddr, f.OCity) : Addr(f.GAddr, f.GCity) ?? Addr(f.OAddr, f.OCity);
                    st.Email = f.IsGroup ? f.OEmail : f.GEmail ?? f.OEmail;
                    break;
            }
            foreach (var g in grp)
            {
                // Canone: attività attiva e abbonamento valido in almeno un giorno del mese. Il consumo AI si fattura comunque.
                var subscribed = g.Active && g.From is DateTime from && from.Date <= last && (g.To is null || g.To.Value.Date >= first);
                var fee = g.Fee ?? b.DefaultMonthlyFeeEur;
                if (subscribed && fee > 0)
                    st.Lines.Add(new(g.Id, g.Name, "canone", $"Abbonamento mvchat · {MonthName(month)}", Math.Round(fee, 2, MidpointRounding.AwayFromZero)));
                if (g.AiUsd > 0)
                {
                    var eur = Math.Round(g.AiUsd * b.UsdToEur * (1 + b.AiMarkupPct / 100m), 2, MidpointRounding.AwayFromZero);
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
        st.VatAmount = Math.Round(st.Subtotal * st.VatPct / 100m, 2, MidpointRounding.AwayFromZero); // arrotondamento commerciale (0,005 → 0,01)
        st.Total = st.Subtotal + st.VatAmount;
    }

    public async Task<List<Statement>> ClosedAsync(Scope s, string month) => (await _db.QueryAsync(
        "SELECT b.*, o.IsGroup FROM BillingStatements b JOIN Organizations o ON o.Id=b.OrganizationId WHERE b.Month=@month AND (@All=1 OR b.OrganizationId=@Org)",
        new { month, All = s.IsSuperAdmin ? 1 : 0, Org = s.OrganizationId ?? -1 },
        r => new Statement
        {
            Month = r.Str("Month")!, OrganizationId = r.Int("OrganizationId"), IsGroup = r.Bool("IsGroup"), RecipientName = r.Str("RecipientName")!,
            RecipientKey = r.Str("RecipientKey") is { Length: > 0 } k ? k : "o" + r.Int("OrganizationId"),
            RecipientKind = r.Str("RecipientKind") is { Length: > 0 } kd ? kd : (r.Bool("IsGroup") ? "gruppo" : "singola"),
            LegalName = r.Str("LegalName"), VatNumber = r.Str("VatNumber"), Address = r.Str("Address"), Email = r.Str("Email"),
            Lines = JsonSerializer.Deserialize<List<StatementLine>>(r.Str("LinesJson")!) ?? new(),
            Subtotal = r.GetDecimal(r.GetOrdinal("Subtotal")), VatPct = r.GetDecimal(r.GetOrdinal("VatPct")), VatAmount = r.GetDecimal(r.GetOrdinal("VatAmount")),
            Total = r.GetDecimal(r.GetOrdinal("Total")), UsdToEur = r.GetDecimal(r.GetOrdinal("UsdToEur")), AiMarkupPct = r.GetDecimal(r.GetOrdinal("AiMarkupPct")),
            Closed = true, ClosedAt = r.Date("ClosedAt")
        })).Where(x => Visible(s, x)).ToList();

    /// <summary>Chiude il mese: i rendiconti aperti vengono salvati così come sono e non cambiano più.</summary>
    public async Task<int> CloseAsync(Scope s, string month, int userId)
    {
        var open = (await MonthAsync(s, month)).Where(x => !x.Closed).ToList();
        foreach (var st in open)
            await _db.ExecuteAsync(
                @"INSERT IGNORE INTO BillingStatements (Month, OrganizationId, RecipientKey, RecipientKind, RecipientName, LegalName, VatNumber, Address, Email, LinesJson, Subtotal, VatPct, VatAmount, Total, UsdToEur, AiMarkupPct, ClosedBy)
                  VALUES (@Month, @OrganizationId, @RecipientKey, @RecipientKind, @RecipientName, @LegalName, @VatNumber, @Address, @Email, @LinesJson, @Subtotal, @VatPct, @VatAmount, @Total, @UsdToEur, @AiMarkupPct, @userId)",
                new { st.Month, st.OrganizationId, st.RecipientKey, st.RecipientKind, st.RecipientName, st.LegalName, st.VatNumber, st.Address, st.Email, LinesJson = JsonSerializer.Serialize(st.Lines),
                      st.Subtotal, st.VatPct, st.VatAmount, st.Total, st.UsdToEur, st.AiMarkupPct, userId });
        return open.Count;
    }

    /// <summary>Riapre un rendiconto chiuso (per correggere): si ricalcola con le regole di adesso.</summary>
    public Task<int> ReopenAsync(string month, string recipientKey) =>
        _db.ExecuteAsync("DELETE FROM BillingStatements WHERE Month=@month AND RecipientKey=@recipientKey", new { month, recipientKey });

    // ---------- Gestori (società con più attività di un gruppo) ----------
    public Task<List<BillingEntity>> EntitiesAsync(int? orgId = null) => _db.QueryAsync(
        @"SELECT e.*, o.Name AS OrgName, (SELECT COUNT(*) FROM Gyms g WHERE g.BillingEntityId=e.Id AND g.BillTo='gestore') AS Gyms
          FROM BillingEntities e JOIN Organizations o ON o.Id=e.OrganizationId WHERE (@org=-1 OR e.OrganizationId=@org) ORDER BY o.Name, e.Name",
        new { org = orgId ?? -1 },
        r => new BillingEntity
        {
            Id = r.Int("Id"), OrganizationId = r.Int("OrganizationId"), OrgName = r.Str("OrgName")!, Name = r.Str("Name")!, LegalName = r.Str("LegalName"),
            VatNumber = r.Str("VatNumber"), Address = r.Str("Address"), City = r.Str("City"), Email = r.Str("Email"), IsActive = r.Bool("IsActive"),
            Gyms = Convert.ToInt32(r.GetValue(r.GetOrdinal("Gyms")))
        });

    public async Task<int> SaveEntityAsync(BillingEntity e)
    {
        if (e.Id > 0)
        {
            await _db.ExecuteAsync(
                @"UPDATE BillingEntities SET Name=@Name, LegalName=@LegalName, VatNumber=@VatNumber, Address=@Address, City=@City, Email=@Email, IsActive=@IsActive
                  WHERE Id=@Id", new { e.Id, e.Name, e.LegalName, e.VatNumber, e.Address, e.City, e.Email, e.IsActive });
            return e.Id;
        }
        return await _db.ScalarAsync<int>(
            @"INSERT INTO BillingEntities (OrganizationId, Name, LegalName, VatNumber, Address, City, Email, IsActive)
              VALUES (@OrganizationId, @Name, @LegalName, @VatNumber, @Address, @City, @Email, @IsActive); SELECT LAST_INSERT_ID();",
            new { e.OrganizationId, e.Name, e.LegalName, e.VatNumber, e.Address, e.City, e.Email, e.IsActive });
    }

    public async Task<(string BillTo, int? EntityId)> BillToAsync(int gymId) =>
        (await _db.QueryAsync("SELECT BillTo, BillingEntityId FROM Gyms WHERE Id=@gymId", new { gymId }, r => (r.Str("BillTo") ?? "gruppo", r.IntN("BillingEntityId"))))
        .FirstOrDefault(("gruppo", null));

    public Task SetBillToAsync(int gymId, string billTo, int? entityId) => _db.ExecuteAsync(
        "UPDATE Gyms SET BillTo=@billTo, BillingEntityId=@entityId WHERE Id=@gymId", new { gymId, billTo, entityId });
}

/// <summary>Un gestore: società che detiene più attività di un gruppo e riceve un solo rendiconto per tutte.</summary>
public sealed class BillingEntity
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public string OrgName { get; set; } = "";
    public string Name { get; set; } = "";
    public string? LegalName { get; set; }
    public string? VatNumber { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; } = true;
    public int Gyms { get; set; }
}
