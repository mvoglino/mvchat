using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Ai;
using MvChat.Web.Campaigns;
using MvChat.Web.Catalog;
using MvChat.Web.Data;
using MvChat.Web.Infrastructure;
using MvChat.Web.Security;
using MvChat.Web.WhatsApp;

namespace MvChat.Web.Pages.Campagne;

public class DettaglioModel : PageModel
{
    private readonly CampaignRepo _repo; private readonly CampaignSender _sender; private readonly WaRepo _wa; private readonly CatalogRepo _catalog; private readonly Repos _repos;
    public DettaglioModel(CampaignRepo repo, CampaignSender sender, WaRepo wa, CatalogRepo catalog, Repos repos)
    { _repo = repo; _sender = sender; _wa = wa; _catalog = catalog; _repos = repos; }

    public Campaign C { get; private set; } = null!;
    public List<CampaignRecipient> Recipients { get; private set; } = new();
    public string Preview { get; private set; } = "";
    public string Windows { get; private set; } = "";
    public bool WindowOpen { get; private set; }
    public DateTime? NextOpen { get; private set; }
    public int? MetaLimit { get; private set; }
    public string Filter { get; private set; } = "";

    private async Task<bool> LoadAsync(int id, string? filter = null)
    {
        var c = await _repo.GetAsync(User.Scope(), id); // perimetro nella query
        if (c is null) return false;
        C = c;
        Filter = filter is "in_attesa" or "inviato" or "saltato" or "errore" ? filter : "";
        Recipients = await _repo.RecipientsAsync(id, Filter == "" ? null : Filter, 500);
        var w = await _repo.WindowsAsync(c.GymId);
        Windows = SendWindows.Describe(w);
        WindowOpen = SendWindows.IsOpen(w, DateTime.UtcNow);
        NextOpen = WindowOpen ? null : SendWindows.NextOpenRome(w, DateTime.UtcNow);
        var number = await _wa.NumberAsync(c.WaNumberId);
        MetaLimit = number is null ? null : SendWindows.MetaDailyLimit(number.MessagingLimit, number.IsSimulated);
        var t = await _wa.TemplateAsync(c.TemplateId);
        var first = (await _repo.RecipientsAsync(id, null, 1)).FirstOrDefault();
        if (t is not null)
            Preview = TemplateText.Fill(t.Body, new Dictionary<string, string?>
            {
                ["nome"] = first?.FirstName ?? "Giulia", ["cognome"] = first?.LastName, ["abbonamento"] = first?.Membership,
                ["scadenza"] = first?.ExpiresOn?.ToString("dd/MM/yyyy"), ["palestra"] = c.GymName, ["offerta"] = c.OfferTitle
            });
        return true;
    }

    public async Task<IActionResult> OnGetAsync(int id, string? stato) => await LoadAsync(id, stato) ? Page() : NotFound();

    private IActionResult Back() => Redirect($"/Campagne/{C.Id}");
    private Task Audit(string action, string detail) =>
        _repos.AuditAsync(User.Scope(), action, $"#{C.Id} {C.Name}{(detail == "" ? "" : " · " + detail)}", HttpContext.Connection.RemoteIpAddress?.ToString(), C.OrganizationId, C.GymId);

    /// <summary>Gli stessi controlli dell'invio, fatti prima: così chi avvia sa subito se qualcosa non va.</summary>
    private async Task<string?> ProblemAsync()
    {
        if (C.Waiting == 0) return "Non ci sono destinatari da contattare.";
        var number = await _wa.NumberAsync(C.WaNumberId);
        if (number is null) return "Il numero WhatsApp della palestra non è più collegato.";
        var t = await _wa.TemplateAsync(C.TemplateId);
        if (t is null || !t.IsApproved) return "Il template del primo messaggio non è approvato da Meta.";
        if (C.OfferId is int oid)
        {
            var o = (await _catalog.OffersAsync(new Scope { Role = Roles.SuperAdmin }, offerId: oid)).FirstOrDefault();
            if (o is null || o.Status != "Attiva") return $"L'offerta collegata non è attiva ({o?.Status ?? "eliminata"}).";
        }
        return null;
    }

    private async Task<string> RunNowAsync()
    {
        var r = await _sender.RunAsync(TimeSpan.FromSeconds(20), C.Id, waitForTurn: true);
        var fresh = await _repo.GetAsync(C.Id);
        return r.Sent + r.Skipped + r.Errors > 0
            ? $"Inviati {r.Sent}" + (r.Skipped > 0 ? $", saltati {r.Skipped}" : "") + (r.Errors > 0 ? $", errori {r.Errors}" : "") + "."
            : fresh?.LastRunNote is { } n ? "Nessun invio in questo momento: " + n + "." : "Nessun invio in questo momento.";
    }

    public async Task<IActionResult> OnPostStartAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!C.CanStart) return Back();
        if (await ProblemAsync() is { } p) { TempData["Err"] = p; return Back(); }
        var scheduled = C.StartAt is DateTime s && s > DateTime.UtcNow;
        await _repo.SetStatusAsync(id, scheduled ? "programmata" : "in_corso", null, new[] { "bozza" });
        await Audit("campaign.started", scheduled ? "programmata" : "avviata");
        TempData["Ok"] = scheduled ? $"Campagna programmata: parte il {C.StartAt!.Value.ToRome():dd/MM} alle {C.StartAt!.Value.ToRome():HH:mm}." : "Campagna avviata. " + await RunNowAsync();
        return Back();
    }

    public async Task<IActionResult> OnPostPauseAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (C.CanPause) { await _repo.SetStatusAsync(id, "in_pausa", "messa in pausa da " + User.Scope().Name, new[] { "in_corso", "programmata" }); await Audit("campaign.paused", ""); TempData["Ok"] = "Campagna in pausa: nessun nuovo primo messaggio partirà. Le conversazioni già aperte continuano."; }
        return Back();
    }

    public async Task<IActionResult> OnPostResumeAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!C.CanResume) return Back();
        if (await ProblemAsync() is { } p) { TempData["Err"] = p; return Back(); }
        var scheduled = C.StartAt is DateTime s && s > DateTime.UtcNow;
        await _repo.SetStatusAsync(id, scheduled ? "programmata" : "in_corso", null, new[] { "in_pausa" });
        await Audit("campaign.resumed", "");
        TempData["Ok"] = "Campagna ripresa. " + (scheduled ? "" : await RunNowAsync());
        return Back();
    }

    public async Task<IActionResult> OnPostCancelAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!C.CanCancel) return Back();
        await _repo.SetStatusAsync(id, "annullata", "annullata da " + User.Scope().Name, new[] { "bozza", "programmata", "in_corso", "in_pausa" });
        await _repo.SkipWaitingAsync(id, "campagna annullata");
        await Audit("campaign.cancelled", "");
        TempData["Ok"] = "Campagna annullata: i destinatari non ancora contattati non riceveranno il messaggio.";
        return Back();
    }

    /// <summary>Non serve aspettare il giro automatico: invia subito un gruppo di messaggi.</summary>
    public async Task<IActionResult> OnPostRunAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (C.Status != "in_corso") return Back();
        TempData["Ok"] = await RunNowAsync();
        return Back();
    }
}
