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
    private readonly WaService _send; private readonly ConversationRepo _convs; private readonly MvChat.Web.Contacts.ContactsRepo _contacts;
    private readonly MvChat.Web.Reports.ReportRepo _report;
    public DettaglioModel(CampaignRepo repo, CampaignSender sender, WaRepo wa, CatalogRepo catalog, Repos repos, WaService send, ConversationRepo convs, MvChat.Web.Contacts.ContactsRepo contacts, MvChat.Web.Reports.ReportRepo report)
    { _repo = repo; _sender = sender; _wa = wa; _catalog = catalog; _repos = repos; _send = send; _convs = convs; _contacts = contacts; _report = report; }

    /// <summary>Motivi dei rifiuti di questa campagna (solo quelli presenti).</summary>
    public List<MvChat.Web.Reports.ReasonCount> Reasons { get; private set; } = new();

    public List<TestNumber> TestNumbers { get; private set; } = new();
    /// <summary>Template approvati del numero della campagna (per scegliere il promemoria).</summary>
    public List<WaTemplate> Templates { get; private set; } = new();

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
        TestNumbers = await _repo.TestNumbersAsync(c.GymId);
        Templates = (await _wa.TemplatesAsync(c.GymId)).Where(x => x.IsApproved && x.WaNumberId == c.WaNumberId).ToList();
        Reasons = (await _report.RefusalReasonsAsync(User.Scope(), DateTime.UtcNow.AddYears(-20), DateTime.UtcNow.AddDays(1), null, c.GymId, c.Id)).Where(r => r.Count > 0).ToList();
        var t = await _wa.TemplateAsync(c.TemplateId);
        var first = (await _repo.RecipientsAsync(id, null, 1)).FirstOrDefault();
        if (t is not null)
            Preview = TemplateText.Fill(t.Body, new Dictionary<string, string?>
            {
                ["nome"] = first?.FirstName ?? "Giulia", ["cognome"] = first?.LastName, ["abbonamento"] = first?.Membership,
                ["scadenza"] = first?.ExpiresOn?.ToString("dd/MM/yyyy"), ["palestra"] = c.GymName, ["offerta"] = c.OfferTitle,
                ["servizio"] = first?.Service, ["note"] = first?.Notes
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
        if (number is null) return "Il numero WhatsApp dell'attività non è più collegato.";
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

    [BindProperty] public string? ExtraInstructions { get; set; }
    [BindProperty] public bool AskRefusalReason { get; set; }

    /// <summary>Istruzioni in più per l'assistente: si cambiano anche a campagna avviata e valgono dalle risposte successive.</summary>
    public async Task<IActionResult> OnPostExtraAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (C.Status is "completata" or "annullata") return Back();
        var text = string.IsNullOrWhiteSpace(ExtraInstructions) ? null : ExtraInstructions.Trim()[..Math.Min(ExtraInstructions.Trim().Length, 1500)];
        await _repo.SetExtraInstructionsAsync(id, text, AskRefusalReason);
        await Audit("campaign.instructions", text is null ? "tolte" : "aggiornate");
        TempData["Ok"] = text is null ? "Istruzioni in più tolte." : "Istruzioni in più salvate: l'assistente le usa dalla prossima risposta.";
        return Back();
    }

    [BindProperty] public int? FollowUpTemplateId { get; set; }
    [BindProperty] public int FollowUpDays { get; set; } = 3;

    /// <summary>Promemoria a chi non risponde: si attiva, cambia o toglie finché la campagna non è annullata.</summary>
    public async Task<IActionResult> OnPostFollowUpAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (C.Status == "annullata") return Back();
        var t = FollowUpTemplateId is int fid ? Templates.FirstOrDefault(x => x.Id == fid) : null;
        if (FollowUpTemplateId is not null && (t is null || t.Id == C.TemplateId || FollowUpDays is < 1 or > 7))
        { TempData["Err"] = "Scegli un template approvato diverso dal primo messaggio e un numero di giorni tra 1 e 7."; return Back(); }
        if (t is not null && t.Variables.Contains("offerta") && C.OfferId is null) { TempData["Err"] = "Il promemoria cita l'offerta, ma la campagna non ha un'offerta."; return Back(); }
        await _repo.SetFollowUpAsync(id, t?.Id, Math.Clamp(FollowUpDays, 1, 7));
        await Audit("campaign.followup", t is null ? "tolto" : $"{t.Name} dopo {FollowUpDays} giorni");
        TempData["Ok"] = t is null ? "Promemoria tolto." : $"Promemoria attivo: «{t.Name}» a chi non risponde dopo {FollowUpDays} {(FollowUpDays == 1 ? "giorno" : "giorni")}.";
        return Back();
    }

    /// <summary>Rimette in coda i destinatari a cui l'invio non è riuscito (es. Meta non raggiungibile o numero da correggere).</summary>
    public async Task<IActionResult> OnPostRequeueAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (C.Status is not ("in_corso" or "in_pausa" or "completata")) return Back();
        var n = await _repo.RequeueErrorsAsync(id);
        await Audit("campaign.requeue", $"{n} destinatari");
        TempData["Ok"] = n == 0 ? "Nessun destinatario da riprovare: quelli in errore avevano già ricevuto il messaggio o l'invio era incerto."
            : $"{n} destinatari rimessi in coda." + (C.Status == "in_pausa" ? " Riprendi la campagna per inviare." : "");
        return Back();
    }

    /// <summary>
    /// Prova facoltativa: il primo messaggio della campagna arriva ai numeri di prova dell'attività, con dati di esempio.
    /// Si apre una conversazione di prova per ognuno, così si può anche rispondere e vedere l'assistente all'opera.
    /// </summary>
    public async Task<IActionResult> OnPostTestAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (C.Status is "completata" or "annullata") return Back();
        if (TestNumbers.Count == 0) { TempData["Err"] = "Nessun numero di prova: aggiungili prima."; return Back(); }
        var number = await _wa.NumberAsync(C.WaNumberId);
        var t = await _wa.TemplateAsync(C.TemplateId);
        if (number is null) { TempData["Err"] = "Il numero WhatsApp dell'attività non è più collegato."; return Back(); }
        if (t is null || !t.IsApproved) { TempData["Err"] = "Il template del primo messaggio non è approvato da Meta."; return Back(); }
        int ok = 0; var problems = new List<string>();
        foreach (var n in TestNumbers)
        {
            if (await _contacts.IsOptedOutAsync(C.OrganizationId, n.Phone)) { problems.Add($"{n.Name}: è nella lista STOP"); continue; }
            var parts = n.Name.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var values = new Dictionary<string, string?>
            {
                ["nome"] = parts[0], ["cognome"] = parts.Length > 1 ? parts[1] : "Prova", ["abbonamento"] = "Annuale",
                ["scadenza"] = DateTime.UtcNow.ToRome().Date.AddDays(30).ToString("dd/MM/yyyy"), ["palestra"] = C.GymName, ["sede"] = C.GymName, ["offerta"] = C.OfferTitle,
                ["servizio"] = TemplateText.Placeholders["servizio"], ["note"] = TemplateText.Placeholders["note"]
            };
            var r = await _send.SendTemplateAsync(number, t, n.Phone, values, User.Scope().UserId);
            if (!r.Ok) { problems.Add($"{n.Name}: {r.Error}"); continue; }
            var convId = await _convs.CreateAsync(new Conversation
            {
                OrganizationId = C.OrganizationId, GymId = C.GymId, WaNumberId = number.Id, ContactPhone = n.Phone, ContactName = parts[0],
                Membership = "Annuale", ExpiresOn = DateTime.UtcNow.ToRome().Date.AddDays(30), GoalModelId = C.GoalModelId, OfferId = C.OfferId, IsTest = true,
                TestOfCampaignId = C.Id
            });
            await _wa.SetMessageConversationAsync(r.MessageId, convId);
            ok++;
        }
        await Audit("campaign.test", $"prova a {ok} numeri");
        if (ok > 0) TempData["Ok"] = $"Prova inviata a {ok} {(ok == 1 ? "numero" : "numeri")}: rispondi dal telefono per vedere l'assistente. Le conversazioni di prova sono in Conversazioni.";
        if (problems.Count > 0) TempData["Err"] = "Non inviata a " + string.Join("; ", problems);
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
