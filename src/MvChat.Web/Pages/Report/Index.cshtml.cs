using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Data;
using MvChat.Web.Infrastructure;
using MvChat.Web.Reports;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Report;

/// <summary>
/// Risultati per livello: MVitalia vede gruppi e attività singole, l'amministratore di gruppo le sue attività,
/// l'amministratore di attività le sue campagne. Da ogni riga si scende di un livello.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ReportRepo _report; private readonly Repos _repos; private readonly AppConfigStore _config;
    public IndexModel(ReportRepo report, Repos repos, AppConfigStore config) { _report = report; _repos = repos; _config = config; }

    public Scope Me { get; private set; } = new();
    public DateTime From { get; private set; }
    public DateTime To { get; private set; }
    public int? Group { get; private set; }
    public int? Activity { get; private set; }
    public string Level { get; private set; } = "";        // clienti · attivita · campagne
    public string Title { get; private set; } = "";
    public List<(string Label, string? Url)> Crumbs { get; } = new();
    public List<ReportRow> Rows { get; private set; } = new();
    public ReportRow Total { get; private set; } = new();
    public List<DayPoint> Days { get; private set; } = new();
    public MetaSettings Meta => _config.Current.Meta;

    private async Task<bool> LoadAsync(DateTime? dal, DateTime? al, int? gruppo, int? attivita)
    {
        Me = User.Scope();
        var today = DateTime.UtcNow.ToRome().Date;
        To = (al ?? today).Date; From = (dal ?? To.AddDays(-29)).Date;
        if (From > To) (From, To) = (To, From);
        if ((To - From).TotalDays > 366) From = To.AddDays(-366);
        var fromUtc = From.FromRome(); var toUtc = To.AddDays(1).FromRome();

        var gyms = await _repos.GymsAsync(Me);
        // Il livello si decide dal ruolo e da cosa è stato aperto; quello che arriva dall'indirizzo vale solo se è nel perimetro.
        if (Me.IsManager) attivita = Me.GymId;
        if (Me.IsOrgAdmin) gruppo = Me.OrganizationId;
        var gym = attivita is int a ? gyms.FirstOrDefault(g => g.Id == a) : null;
        if (attivita is not null && gym is null) return false;
        if (gruppo is int gid && !gyms.Any(g => g.OrganizationId == gid)) return false;
        Activity = gym?.Id; Group = gym is not null ? (gym.InGroup ? gym.OrganizationId : null) : gruppo;

        Crumbs.Add(("Report", Me.IsSuperAdmin ? Url(null, null) : null));
        if (gym is null && Group is int g0)
        {
            Level = "attivita";
            var list = await _report.ActivitiesAsync(Me, fromUtc, toUtc, g0);
            Title = list.FirstOrDefault().OrgName ?? "Gruppo";
            Rows = list.Select(x => { x.Row.Link = Url(g0, x.Row.Id); return x.Row; }).ToList();
            if (Me.IsSuperAdmin) Crumbs.Add((Title, null));
        }
        else if (gym is not null)
        {
            Level = "campagne"; Title = gym.Name;
            Rows = await _report.CampaignsAsync(Me, fromUtc, toUtc, gym.Id);
            foreach (var r in Rows) r.Link = $"/Campagne/{r.Id}";
            if (gym.InGroup && !Me.IsManager) Crumbs.Add((gym.OrganizationName, Url(gym.OrganizationId, null)));
            Crumbs.Add((gym.Name, null));
            // I totali dell'attività comprendono anche le conversazioni fuori campagna (prove escluse).
            var act = (await _report.ActivitiesAsync(Me, fromUtc, toUtc, gym.OrganizationId)).First(x => x.Row.Id == gym.Id).Row;
            Total = act;
        }
        else
        {
            // MVitalia: i gruppi (somma delle loro attività) e le attività singole.
            Level = "clienti"; Title = "Tutti i clienti del servizio";
            var list = await _report.ActivitiesAsync(Me, fromUtc, toUtc, null);
            foreach (var grp in list.GroupBy(x => (x.OrgId, x.OrgName, x.InGroup)))
            {
                if (grp.Key.InGroup)
                {
                    var row = new ReportRow { Kind = "gruppo", Id = grp.Key.OrgId, Name = grp.Key.OrgName, Detail = $"gruppo · {grp.Count()} attività", Link = Url(grp.Key.OrgId, null) };
                    foreach (var x in grp) row.Add(x.Row);
                    Rows.Add(row);
                }
                else
                    foreach (var x in grp) { x.Row.Detail = "attività singola"; x.Row.Link = Url(null, x.Row.Id); Rows.Add(x.Row); }
            }
        }
        if (Level != "campagne") { Total = new ReportRow(); foreach (var r in Rows) Total.Add(r); }
        Days = await _report.DailyAsync(Me, fromUtc, toUtc, gym is null ? Group : null, gym?.Id);
        return true;
    }

    public async Task<IActionResult> OnGetAsync(DateTime? dal, DateTime? al, int? gruppo, int? attivita) =>
        await LoadAsync(dal, al, gruppo, attivita) ? Page() : NotFound();

    /// <summary>La tabella del report in un file che si apre con Excel.</summary>
    public async Task<IActionResult> OnGetCsvAsync(DateTime? dal, DateTime? al, int? gruppo, int? attivita)
    {
        if (!await LoadAsync(dal, al, gruppo, attivita)) return NotFound();
        var it = CultureInfo.GetCultureInfo("it-IT");
        static string C(string? v) => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";
        var sb = new StringBuilder();
        sb.AppendLine("Nome;Tipo;Primi messaggi;Consegnati;Letti;Conversazioni;Con risposta;Obiettivo raggiunto;Rifiuti;Non contattare più;Solo assistente AI;Con operatore;Costo Meta stimato (€);Costo AI ($)");
        foreach (var r in Rows.Append(new ReportRow { Name = "Totale", Kind = "" }.Also(t => t.Add(Total))))
            sb.AppendLine(string.Join(";", C(r.Name), C(r.Detail ?? r.Kind), r.Sent, r.Delivered, r.Read, r.Conversations, r.Replied, r.Reached, r.Refused, r.OptOut,
                r.AiOnly, r.WithOperator, r.MetaCostEur(Meta).ToString("0.00", it), r.AiCostUsd.ToString("0.0000", it)));
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"report-{From:yyyyMMdd}-{To:yyyyMMdd}.csv");
    }

    public string Url(int? gruppo, int? attivita, string? handler = null) =>
        $"/Report?dal={From:yyyy-MM-dd}&al={To:yyyy-MM-dd}" + (gruppo is int g ? $"&gruppo={g}" : "") + (attivita is int a ? $"&attivita={a}" : "") + (handler is null ? "" : $"&handler={handler}");
}

internal static class Fluent
{
    public static T Also<T>(this T x, Action<T> a) { a(x); return x; }
}
