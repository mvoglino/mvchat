using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Ai;
using MvChat.Web.Campaigns;
using MvChat.Web.Data;
using MvChat.Web.Infrastructure;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Conversazioni;

/// <summary>Tutti i dialoghi, divisi tra quelli gestiti solo dall'assistente AI e quelli proseguiti con una persona dell'attività.</summary>
public class ArchivioModel : PageModel
{
    private readonly ArchiveRepo _archive; private readonly Repos _repos; private readonly CampaignRepo _campaigns;
    public ArchivioModel(ArchiveRepo archive, Repos repos, CampaignRepo campaigns) { _archive = archive; _repos = repos; _campaigns = campaigns; }

    public const int PageSize = 500;
    public List<ArchiveRow> Items { get; private set; } = new();
    public ArchiveStats Stats { get; private set; } = new(0, 0, 0, 0, 0, 0);
    public List<Gym> Gyms { get; private set; } = new();
    public List<Campaign> Campaigns { get; private set; } = new();
    public ArchiveFilter F { get; private set; } = new();

    private async Task LoadAsync(int? gym, string? tipo, string? esito, DateTime? dal, DateTime? al, int? campagna, string? q, bool prove)
    {
        var me = User.Scope();
        Gyms = await _repos.GymsAsync(me);
        Campaigns = await _campaigns.ListAsync(me, null);
        F = new ArchiveFilter
        {
            GymId = Gyms.Any(g => g.Id == gym) ? gym : null,
            Kind = tipo is "ai" or "operatore" ? tipo : "",
            Outcome = esito is not null && Outcomes.All.Contains(esito) ? esito : "",
            From = dal, To = al,
            CampaignId = Campaigns.Any(c => c.Id == campagna) ? campagna : null,
            Search = q, IncludeTests = prove
        };
        Stats = await _archive.StatsAsync(me, F);
    }

    public async Task OnGetAsync(int? gym, string? tipo, string? esito, DateTime? dal, DateTime? al, int? campagna, string? q, bool prove = false)
    {
        await LoadAsync(gym, tipo, esito, dal, al, campagna, q, prove);
        Items = await _archive.ListAsync(User.Scope(), F, PageSize);
    }

    /// <summary>Lo stesso elenco in un file che si apre con Excel (separatore ; e accenti corretti).</summary>
    public async Task<IActionResult> OnGetCsvAsync(int? gym, string? tipo, string? esito, DateTime? dal, DateTime? al, int? campagna, string? q, bool prove = false)
    {
        await LoadAsync(gym, tipo, esito, dal, al, campagna, q, prove);
        var rows = await _archive.ListAsync(User.Scope(), F, 50000);
        var sb = new StringBuilder();
        static string C(string? v) => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";
        sb.AppendLine("Data;Attività;Cliente;Cellulare;Obiettivo;Campagna;Gestione;Operatori;Messaggi cliente;Risposte assistente;Risposte operatori;Esito;Stato");
        foreach (var r in rows)
            sb.AppendLine(string.Join(";", C(r.CreatedAt.ToRome().ToString("dd/MM/yyyy HH:mm")), C(r.GymName), C(r.ContactName), C(r.ContactPhone), C(r.GoalName),
                C(r.CampaignName ?? (r.IsTest ? "prova" : "")), C(r.HumanInvolved ? "Con operatore" : "Solo assistente AI"), C(r.Operators),
                r.CustomerMsgs, r.AiMsgs, r.HumanMsgs, C(Outcomes.Label(r.Outcome)), C(r.Status)));
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        await _repos.AuditAsync(User.Scope(), "archive.export", $"{rows.Count} dialoghi", HttpContext.Connection.RemoteIpAddress?.ToString());
        return File(bytes, "text/csv; charset=utf-8", $"dialoghi-{DateTime.UtcNow.ToRome():yyyyMMdd-HHmm}.csv");
    }

    public static int Pct(int part, int total) => total == 0 ? 0 : (int)Math.Round(100.0 * part / total);

    public string Url(string? tipo) =>
        $"?tipo={tipo}&gym={F.GymId}&esito={F.Outcome}&dal={F.From:yyyy-MM-dd}&al={F.To:yyyy-MM-dd}&campagna={F.CampaignId}&q={Uri.EscapeDataString(F.Search ?? "")}&prove={(F.IncludeTests ? "true" : "false")}";
}
