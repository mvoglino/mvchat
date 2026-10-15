using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Billing;
using MvChat.Web.Data;
using MvChat.Web.Infrastructure;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Abbonamento;

/// <summary>
/// «Abbonamento e costi»: quanto costa mvchat all'attività o al gruppo, mese per mese.
/// Abbonamento MVitalia (con l'assistente AI compreso) + spesa Meta stimata per i messaggi WhatsApp.
/// Il costo dell'AI non compare: è compreso nell'abbonamento.
/// </summary>
public class IndexModel : PageModel
{
    private readonly SubscriptionRepo _subs; private readonly AppConfigStore _config; private readonly Repos _repos;
    public IndexModel(SubscriptionRepo subs, AppConfigStore config, Repos repos) { _subs = subs; _config = config; _repos = repos; }

    public sealed record Choice(string Value, string Label);

    public Scope Me { get; private set; } = new();
    public int Year { get; private set; }
    public int Month { get; private set; }
    public string Filter { get; private set; } = "";
    public string Title { get; private set; } = "";
    public List<int> Months { get; private set; } = new();
    public List<int> Years { get; private set; } = new();
    public List<SubGym> Gyms { get; private set; } = new();
    public List<SubMonth> Rows { get; private set; } = new();
    public List<Choice> Choices { get; private set; } = new();
    public decimal MarketingPrice => _config.Current.Meta.MarketingPriceEur;
    public decimal UtilityPrice => _config.Current.Meta.UtilityPriceEur;

    public async Task OnGetAsync(int? anno, int? mese, string? vista) => await LoadAsync(anno, mese, vista);

    public async Task<IActionResult> OnGetExcelAsync(int? anno, int? mese, string? vista)
    {
        await LoadAsync(anno, mese, vista);
        var wb = new Xlsx();
        var note = $"{Title} · abbonamento mvchat (assistente AI compreso) + messaggi WhatsApp stimati dal listino Meta ({MarketingPrice:0.####} € marketing, {UtilityPrice:0.####} € servizio): Meta li fattura direttamente all'attività";
        var m = wb.Sheet("Mese per mese").Title($"Abbonamento e costi {Year}").Note(note).Blank()
            .Header("Mese", "Abbonamento mvchat", "Messaggi Meta", "Spesa Meta stimata", "Totale");
        foreach (var mm in Months)
        {
            var r = Rows.Where(x => x.Month == mm).ToList();
            m.Row(MonthName(mm), new XEur(r.Sum(x => x.Fee)), r.Sum(x => x.Marketing + x.Utility), new XEur(r.Sum(x => x.MetaEur)), new XEur(r.Sum(x => x.Total)));
        }
        m.Total("Totale", new XEur(Rows.Sum(x => x.Fee)), Rows.Sum(x => x.Marketing + x.Utility), new XEur(Rows.Sum(x => x.MetaEur)), new XEur(Rows.Sum(x => x.Total)));
        var a = wb.Sheet("Per attività").Title($"Abbonamento e costi {Year} per attività").Note(note).Blank()
            .Header("Mese", "Gruppo", "Attività", "Abbonamento mvchat", "Messaggi marketing", "Messaggi di servizio", "Spesa Meta stimata", "Totale");
        foreach (var r in Rows.Where(x => x.Total > 0 || x.Marketing + x.Utility > 0))
        {
            var g = Gyms.First(x => x.Id == r.GymId);
            a.Row(MonthName(r.Month), g.InGroup ? g.OrgName : "", g.Name, new XEur(r.Fee), r.Marketing, r.Utility, new XEur(r.MetaEur), new XEur(r.Total));
        }
        await _repos.AuditAsync(Me, "report.export", $"abbonamento e costi {Year} {Title}", HttpContext.Connection.RemoteIpAddress?.ToString());
        return File(wb.ToBytes(), Xlsx.ContentType, $"abbonamento-costi-{Year}.xlsx");
    }

    private async Task LoadAsync(int? anno, int? mese, string? vista)
    {
        Me = User.Scope();
        var now = DateTime.UtcNow.ToRome();
        Year = anno is >= 2024 && anno <= now.Year ? anno.Value : now.Year;
        Years = Enumerable.Range(2026, Math.Max(1, now.Year - 2025)).Reverse().ToList();
        Months = Enumerable.Range(1, Year == now.Year ? now.Month : 12).ToList();
        Month = mese is int mm && Months.Contains(mm) ? mm : Months[^1];

        // Scelta di cosa vedere, sempre dentro il perimetro di chi guarda (il filtro vero è nella query).
        var all = await _subs.GymsAsync(Me);
        if (Me.IsSuperAdmin)
            Choices.AddRange(all.Where(g => g.InGroup).GroupBy(g => (g.OrgId, g.OrgName)).Select(o => new Choice("o" + o.Key.OrgId, "Gruppo " + o.Key.OrgName)));
        if (all.Count > 1)
            Choices.AddRange(all.Select(g => new Choice("a" + g.Id, g.Name + (Me.IsSuperAdmin && g.InGroup ? " · " + g.OrgName : ""))));
        Filter = Choices.Any(c => c.Value == vista) ? vista! : "";
        int? orgId = Filter.StartsWith('o') ? int.Parse(Filter[1..]) : null;
        int? gymId = Filter.StartsWith('a') ? int.Parse(Filter[1..]) : null;
        Gyms = orgId is null && gymId is null ? all : await _subs.GymsAsync(Me, orgId, gymId);
        Title = Filter != "" ? Choices.First(c => c.Value == Filter).Label
            : Me.IsSuperAdmin ? "Tutte le attività"
            : Me.IsOrgAdmin ? "Gruppo " + (all.FirstOrDefault()?.OrgName ?? "")
            : all.Count == 1 ? all[0].Name : "Le mie attività";
        Rows = await _subs.MonthsAsync(Gyms, Year, Months);
    }

    public static string MonthName(int m) => System.Globalization.CultureInfo.GetCultureInfo("it-IT").DateTimeFormat.GetMonthName(m) is var s && s.Length > 0 ? char.ToUpper(s[0]) + s[1..] : m.ToString();
    public string Url(int? anno = null, int? mese = null, string? vista = null, string handler = "") =>
        $"/Abbonamento?{(handler != "" ? "handler=" + handler + "&" : "")}anno={anno ?? Year}&mese={mese ?? Month}&vista={Uri.EscapeDataString(vista ?? Filter)}";
}
