using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Data;
using MvChat.Web.Infrastructure;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Impostazioni;

/// <summary>
/// Consumi AI mese per mese, divisi tra Anthropic e OpenAI: in totale, per gruppo e per attività. Solo MVitalia.
/// Il costo è quello registrato a ogni chiamata (token o minuti di audio × prezzi in Impostazioni AI).
/// </summary>
public class ConsumiModel : PageModel
{
    private readonly Db _db; private readonly AppConfigStore _config; private readonly Repos _repos;
    public ConsumiModel(Db db, AppConfigStore config, Repos repos) { _db = db; _config = config; _repos = repos; }

    public sealed record Cell(decimal Anthropic, decimal OpenAi)
    {
        public decimal Total => Anthropic + OpenAi;
        public static Cell operator +(Cell a, Cell b) => new(a.Anthropic + b.Anthropic, a.OpenAi + b.OpenAi);
        public static readonly Cell Zero = new(0, 0);
    }
    public sealed record UsageRow(int Month, int? OrgId, string OrgName, bool IsGroup, int? GymId, string GymName, Cell Cost);
    public sealed record Choice(string Value, string Label, bool IsGroup);

    public int Year { get; private set; }
    public int Month { get; private set; }
    public string Filter { get; private set; } = "";
    public string FilterLabel { get; private set; } = "Tutto MVitalia";
    public decimal UsdToEur { get; private set; }
    public decimal MarkupPct { get; private set; }
    public List<int> Months { get; private set; } = new();
    public Dictionary<int, Cell> ByMonth { get; private set; } = new();
    public List<UsageRow> Rows { get; private set; } = new();
    public List<Choice> Choices { get; private set; } = new();
    public List<int> Years { get; private set; } = new();

    public async Task OnGetAsync(int? anno, int? mese, string? vista) => await LoadAsync(anno, mese, vista);

    public async Task<IActionResult> OnGetExcelAsync(int? anno, int? mese, string? vista)
    {
        await LoadAsync(anno, mese, vista);
        var wb = new Xlsx();
        var note = $"{FilterLabel} · costo calcolato da mvchat con i prezzi di Impostazioni AI · cambio 1 $ = {UsdToEur:0.####} € · da rifatturare = + {MarkupPct:0.##}%";
        var m = wb.Sheet("Mese per mese").Title($"Consumi AI {Year}").Note(note).Blank()
            .Header("Mese", "Anthropic $", "OpenAI $", "Totale $", "Totale €", "Da rifatturare €");
        var tot = Cell.Zero;
        foreach (var mm in Months)
        {
            var c = ByMonth.GetValueOrDefault(mm, Cell.Zero); tot += c;
            m.Row(MonthName(mm), c.Anthropic, c.OpenAi, c.Total, new XEur(Eur(c.Total)), new XEur(Rebill(c.Total)));
        }
        m.Total("Totale", tot.Anthropic, tot.OpenAi, tot.Total, new XEur(Eur(tot.Total)), new XEur(Rebill(tot.Total)));
        var a = wb.Sheet("Per attività").Title($"Consumi AI {Year} per gruppo e attività").Note(note).Blank()
            .Header("Mese", "Gruppo", "Attività", "Anthropic $", "OpenAI $", "Totale $", "Totale €");
        foreach (var r in Rows.OrderBy(r => r.Month).ThenBy(r => r.OrgName).ThenBy(r => r.GymName))
            a.Row(MonthName(r.Month), r.OrgName, r.GymName, r.Cost.Anthropic, r.Cost.OpenAi, r.Cost.Total, new XEur(Eur(r.Cost.Total)));
        await _repos.AuditAsync(User.Scope(), "report.export", $"consumi AI {Year} {FilterLabel}", HttpContext.Connection.RemoteIpAddress?.ToString());
        return File(wb.ToBytes(), Xlsx.ContentType, $"consumi-ai-{Year}.xlsx");
    }

    private async Task LoadAsync(int? anno, int? mese, string? vista)
    {
        var now = DateTime.UtcNow.ToRome();
        Year = anno is >= 2024 and <= 2100 ? anno.Value : now.Year;
        var lastMonth = Year == now.Year ? now.Month : Year < now.Year ? 12 : 0;
        Months = Enumerable.Range(1, lastMonth).ToList();
        Month = mese is >= 1 and <= 12 && mese <= Math.Max(lastMonth, 1) ? mese.Value : Math.Max(lastMonth, 1);
        UsdToEur = _config.Current.Billing.UsdToEur; MarkupPct = _config.Current.Billing.AiMarkupPct;

        var first = await _db.ScalarAsync<DateTime?>("SELECT MIN(CreatedAt) FROM AiUsage", null);
        var firstYear = first?.ToRome().Year ?? now.Year;
        Years = Enumerable.Range(Math.Min(firstYear, now.Year), now.Year - Math.Min(firstYear, now.Year) + 1).Reverse().ToList();

        Choices = await _db.QueryAsync(
            @"SELECT CONCAT('o', o.Id), o.Name, o.IsGroup FROM Organizations o WHERE o.IsGroup=1
              UNION ALL SELECT CONCAT('a', g.Id), CONCAT(g.Name, CASE WHEN o.IsGroup=1 THEN CONCAT(' · ', o.Name) ELSE '' END), 0
                FROM Gyms g JOIN Organizations o ON o.Id=g.OrganizationId
              ORDER BY 3 DESC, 2", null,
            r => new Choice(r.GetString(0), r.GetString(1), Convert.ToInt64(r.GetValue(2)) != 0));
        Filter = Choices.Any(c => c.Value == vista) ? vista! : "";
        if (Filter != "") FilterLabel = (Filter[0] == 'o' ? "Gruppo " : "Attività ") + Choices.First(c => c.Value == Filter).Label;
        int? orgId = Filter.StartsWith('o') ? int.Parse(Filter[1..]) : null;
        int? gymId = Filter.StartsWith('a') ? int.Parse(Filter[1..]) : null;

        foreach (var mm in Months)
        {
            // Mese italiano → intervallo in UTC (le date sono salvate in UTC).
            var from = new DateTime(Year, mm, 1).FromRome(); var to = new DateTime(Year, mm, 1).AddMonths(1).FromRome();
            var rows = await _db.QueryAsync(
                @"SELECT a.OrganizationId, o.Name AS OrgName, COALESCE(o.IsGroup,0) AS IsGroup, a.GymId, g.Name AS GymName,
                         SUM(CASE WHEN a.Provider='anthropic' THEN a.CostUsd ELSE 0 END) AS Anth,
                         SUM(CASE WHEN a.Provider='openai' THEN a.CostUsd ELSE 0 END) AS Oai
                  FROM AiUsage a LEFT JOIN Gyms g ON g.Id=a.GymId
                  LEFT JOIN Organizations o ON o.Id=COALESCE(g.OrganizationId, a.OrganizationId)
                  WHERE a.CreatedAt>=@from AND a.CreatedAt<@to
                    AND (@orgId IS NULL OR COALESCE(g.OrganizationId, a.OrganizationId)=@orgId) AND (@gymId IS NULL OR a.GymId=@gymId)
                  GROUP BY a.OrganizationId, o.Name, o.IsGroup, a.GymId, g.Name",
                new { from, to, orgId, gymId },
                r => new UsageRow(mm, r.IntN("OrganizationId"), r.Str("OrgName") ?? "MVitalia", Convert.ToInt64(r["IsGroup"]) != 0, r.IntN("GymId"),
                    r.Str("GymName") ?? (r.IntN("OrganizationId") is null ? "Prove di MVitalia (collegamento, banco)" : "Senza attività"),
                    new Cell(r.GetDecimal(r.GetOrdinal("Anth")), r.GetDecimal(r.GetOrdinal("Oai")))));
            Rows.AddRange(rows.Where(x => x.Cost.Total != 0));
            ByMonth[mm] = rows.Aggregate(Cell.Zero, (s, x) => s + x.Cost);
        }
    }

    public decimal Eur(decimal usd) => Math.Round(usd * UsdToEur, 2, MidpointRounding.AwayFromZero);
    public decimal Rebill(decimal usd) => Math.Round(usd * UsdToEur * (1 + MarkupPct / 100m), 2, MidpointRounding.AwayFromZero);
    public static string MonthName(int m) => System.Globalization.CultureInfo.GetCultureInfo("it-IT").DateTimeFormat.GetMonthName(m) is var s && s.Length > 0 ? char.ToUpper(s[0]) + s[1..] : m.ToString();
    public string Url(int? anno = null, int? mese = null, string? vista = null, string handler = "") =>
        $"/Impostazioni/Consumi?{(handler != "" ? "handler=" + handler + "&" : "")}anno={anno ?? Year}&mese={mese ?? Month}&vista={Uri.EscapeDataString(vista ?? Filter)}";
}
