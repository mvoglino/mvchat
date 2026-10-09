using MvChat.Web.Ai;
using MvChat.Web.Catalog;
using MvChat.Web.Contacts;
using MvChat.Web.Infrastructure;
using MvChat.Web.Security;
using MvChat.Web.WhatsApp;

namespace MvChat.Web.Campaigns;

public sealed record RunSummary(int Sent, int Skipped, int Errors, List<string> Notes);

/// <summary>
/// Lavora la coda delle campagne: a piccoli gruppi, rispettando orari dell'attività,
/// limite giornaliero della campagna e limite di Meta del numero. Ogni destinatario viene
/// "prenotato" prima dell'invio, così due giri contemporanei non scrivono mai due volte alla stessa persona.
/// </summary>
public sealed class CampaignSender
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public const int BatchSize = 40;

    private readonly CampaignRepo _repo; private readonly CatalogRepo _catalog; private readonly WaRepo _wa; private readonly WaService _send;
    private readonly ConversationRepo _convs; private readonly ContactsRepo _contacts; private readonly AppConfigStore _config; private readonly ILogger<CampaignSender> _log;
    public CampaignSender(CampaignRepo repo, CatalogRepo catalog, WaRepo wa, WaService send, ConversationRepo convs, ContactsRepo contacts, AppConfigStore config, ILogger<CampaignSender> log)
    { _repo = repo; _catalog = catalog; _wa = wa; _send = send; _convs = convs; _contacts = contacts; _config = config; _log = log; }

    private static readonly Scope AllScope = new() { Role = Roles.SuperAdmin };

    // Errori di Meta che riguardano il numero o l'account: inutile continuare, la campagna va in pausa.
    private static readonly string[] AccountErrors = { "codice 190)", "codice 10)", "codice 200)", "codice 131031", "codice 132001", "codice 132015", "codice 132016", "codice 368" };
    // Problemi del template (parametri, testo, regole Meta): ogni destinatario fallirebbe allo stesso modo, quindi pausa.
    private static readonly string[] TemplateErrors = { "codice 132000", "codice 132005", "codice 132007", "codice 132012", "codice 132068", "codice 132069" };
    // Troppi messaggi in poco tempo: si riprova al giro successivo.
    private static readonly string[] SlowDownErrors = { "codice 130429", "codice 131048", "codice 131056", "codice 80007", "codice 4)" };
    // Meta momentaneamente non disponibile: il destinatario torna in coda (massimo 3 tentativi).
    private static readonly string[] TransientErrors = { "Meta non raggiungibile", "Meta ha risposto 5", "codice 1)", "codice 2)", "codice 131000", "codice 131016", "codice 133004" };
    /// <summary>Dopo tanti errori di fila nello stesso giro qualcosa non va: meglio fermarsi e far controllare.</summary>
    public const int MaxErrorsInARow = 5;

    /// <param name="waitForTurn">Da una pagina (bottone "invia adesso") si aspetta che finisca il giro automatico in corso, invece di rinunciare.</param>
    public async Task<RunSummary> RunAsync(TimeSpan budget, int? onlyCampaignId = null, bool waitForTurn = false)
    {
        var summary = new RunSummary(0, 0, 0, new());
        if (!await Gate.WaitAsync(waitForTurn ? TimeSpan.FromSeconds(30) : TimeSpan.Zero)) return summary with { Notes = new() { "un altro giro di invio è già in corso" } };
        try
        {
            var deadline = DateTime.UtcNow + budget;
            await _repo.FailStaleClaimsAsync();
            await _repo.PromoteScheduledAsync();
            var sent = 0; var skipped = 0; var errors = 0;
            foreach (var id in await _repo.RunnableAsync())
            {
                if (onlyCampaignId is int only && only != id) continue;
                if (DateTime.UtcNow >= deadline) break;
                try
                {
                    var (s, k, e, note) = await RunCampaignAsync(id, deadline);
                    sent += s; skipped += k; errors += e;
                    if (note is not null) summary.Notes.Add($"#{id}: {note}");
                }
                catch (Exception ex) { _log.LogError(ex, "Campagna {Id}: errore nel giro di invio", id); }
            }
            // Promemoria a chi non ha risposto (se resta tempo nel giro).
            var followUps = 0;
            if (onlyCampaignId is null && DateTime.UtcNow < deadline)
            {
                try { followUps = await FollowUpsAsync(deadline); }
                catch (Exception ex) { _log.LogError(ex, "Promemoria delle campagne: errore nel giro di invio"); }
                if (followUps > 0) summary.Notes.Add($"promemoria inviati: {followUps}");
            }
            return summary with { Sent = sent, Skipped = skipped, Errors = errors };
        }
        finally { Gate.Release(); }
    }

    /// <summary>I dati del destinatario per i segnaposto del template (gli stessi per il primo messaggio e per il promemoria).</summary>
    private static Dictionary<string, string?> Values(Campaign c, CampaignRecipient r, Catalog.Offer? offer) => new()
    {
        ["nome"] = r.FirstName, ["cognome"] = r.LastName, ["abbonamento"] = r.Membership,
        ["scadenza"] = r.ExpiresOn?.ToString("dd/MM/yyyy"), ["palestra"] = c.GymName, ["sede"] = c.GymName, ["offerta"] = offer?.Title,
        ["servizio"] = r.Service, ["note"] = r.Notes
    };

    /// <summary>
    /// Promemoria automatico: a chi non ha risposto al primo messaggio dopo i giorni scelti, un secondo template (una volta sola).
    /// Rispetta lista STOP, orari di invio dell'attività e limite di Meta del numero; se il cliente ha risposto nel frattempo non parte.
    /// </summary>
    private async Task<int> FollowUpsAsync(DateTime deadline)
    {
        var sent = 0;
        var campaigns = new Dictionary<int, (Campaign C, WaNumber? N, WaTemplate? T, Catalog.Offer? O, bool Open, int Room)>();
        foreach (var (r, campaignId) in await _repo.FollowUpsDueAsync(BatchSize))
        {
            if (DateTime.UtcNow >= deadline) break;
            if (!campaigns.TryGetValue(campaignId, out var k))
            {
                var c = await _repo.GetAsync(campaignId);
                if (c is null) continue;
                var n = await _wa.NumberAsync(c.WaNumberId);
                var t = c.FollowUpTemplateId is int ft ? await _wa.TemplateAsync(ft) : null;
                var o = c.OfferId is int oid ? (await _catalog.OffersAsync(AllScope, offerId: oid)).FirstOrDefault() : null;
                var open = n is not null && SendWindows.IsOpen(await _repo.WindowsAsync(c.GymId), DateTime.UtcNow);
                var room = n is null ? 0 : SendWindows.MetaDailyLimit(n.MessagingLimit, n.IsSimulated) is int lim ? lim - await _repo.FirstContactsLast24hAsync(n.Id) : int.MaxValue;
                campaigns[campaignId] = k = (c, n, t, o, open, room);
            }
            if (k.N is null || !k.Open || k.Room <= 0) continue; // si riprova al giro dopo (finché resta nei 3 giorni utili)
            if (k.T is null || !k.T.IsApproved || k.T.WaNumberId != k.N.Id)
            {
                if (await _repo.ClaimFollowUpAsync(r.Id)) await _repo.MarkFollowUpAsync(r.Id, "saltato", "template del promemoria non approvato o non più disponibile");
                continue;
            }
            // Il cliente potrebbe aver scritto proprio adesso: si ricontrolla prima di prenotare.
            var conv = r.ConversationId is long cid ? await _convs.GetAsync(cid) : null;
            if (conv is null || conv.LastInboundAt is not null || conv.Status != "ai") continue;
            if (!await _repo.ClaimFollowUpAsync(r.Id)) continue;
            if (await _contacts.IsOptedOutAsync(k.C.OrganizationId, r.Phone)) { await _repo.MarkFollowUpAsync(r.Id, "saltato", "nella lista STOP"); continue; }
            var values = Values(k.C, r, k.O);
            var missing = k.T.Variables.FirstOrDefault(v => !TemplateText.TryValue(values, v, out var x) || string.IsNullOrWhiteSpace(x));
            if (missing is not null) { await _repo.MarkFollowUpAsync(r.Id, "saltato", $"manca il dato «{missing}» richiesto dal promemoria"); continue; }
            var res = await _send.SendTemplateAsync(k.N, k.T, r.Phone, values, null, conv.Id);
            if (!res.Ok) { await _repo.MarkFollowUpAsync(r.Id, "errore", res.Error); continue; }
            if (res.MessageId > 0) await _wa.SetMessageConversationAsync(res.MessageId, conv.Id);
            await _repo.MarkFollowUpAsync(r.Id, "inviato", null);
            campaigns[campaignId] = k with { Room = k.Room - 1 };
            sent++;
        }
        return sent;
    }

    private async Task Pause(int id, string reason)
    {
        await _repo.SetStatusAsync(id, "in_pausa", reason, new[] { "in_corso" });
        await _repo.NoteRunAsync(id, "messa in pausa: " + reason);
    }

    private async Task<(int, int, int, string?)> RunCampaignAsync(int id, DateTime deadline)
    {
        var c = await _repo.GetAsync(id);
        if (c is null || c.Status != "in_corso") return (0, 0, 0, null);
        var number = await _wa.NumberAsync(c.WaNumberId);
        var template = await _wa.TemplateAsync(c.TemplateId);
        var model = (await _catalog.ModelsAsync(AllScope, c.GoalModelId)).FirstOrDefault();
        var offer = c.OfferId is int oid ? (await _catalog.OffersAsync(AllScope, offerId: oid)).FirstOrDefault() : null;

        // Controlli che fermano tutta la campagna: meglio una pausa spiegata che messaggi sbagliati.
        string? stop =
            number is null ? "il numero WhatsApp dell'attività non è più collegato"
            : template is null || !template.IsApproved ? "il template del primo messaggio non è (più) approvato da Meta"
            : model is null ? "il modello di obiettivo non esiste più"
            : c.OfferId is not null && (offer is null || offer.Status != "Attiva") ? $"l'offerta collegata non è attiva ({offer?.Status ?? "eliminata"})"
            : !number.IsSimulated && string.Equals(number.QualityRating, "RED", StringComparison.OrdinalIgnoreCase) ? "Meta segnala qualità bassa (rossa) del numero: invii fermati per proteggerlo"
            : null;
        if (stop is not null) { await Pause(id, stop); return (0, 0, 0, "pausa: " + stop); }

        var now = DateTime.UtcNow;
        var windows = await _repo.WindowsAsync(c.GymId);
        if (!SendWindows.IsOpen(windows, now))
        {
            var next = SendWindows.NextOpenRome(windows, now);
            var note = "fuori dall'orario di invio dell'attività" + (next is DateTime n ? $": si riparte {n:dd/MM} alle {n:HH:mm}" : "");
            await _repo.NoteRunAsync(id, note);
            return (0, 0, 0, note);
        }

        // Quanti messaggi si possono mandare adesso.
        var room = BatchSize;
        string? limitNote = null;
        if (SendWindows.MetaDailyLimit(number!.MessagingLimit, number.IsSimulated) is int metaLimit)
        {
            var free = metaLimit - await _repo.FirstContactsLast24hAsync(number.Id);
            if (free < room) { room = Math.Max(0, free); limitNote = $"raggiunto il limite di Meta del numero ({metaLimit} persone nuove in 24 ore): si riparte quando si libera"; }
        }
        if (c.DailyLimit is int daily)
        {
            var rome = now.ToRome();
            var midnightUtc = now - (rome - rome.Date);
            var free = daily - await _repo.SentSinceAsync(id, midnightUtc);
            if (free < room) { room = Math.Max(0, free); limitNote = $"raggiunto il limite di {daily} invii al giorno della campagna: si riparte domani"; }
        }
        if (room == 0) { await _repo.NoteRunAsync(id, limitNote); return (0, 0, 0, limitNote); }

        var batch = await _repo.ClaimAsync(id, room);
        int sent = 0, skipped = 0, errors = 0, inARow = 0, done = 0;
        var pending = batch.Select(b => b.Id).ToHashSet();
        string? runNote = null;
        try
        {
            foreach (var r in batch)
            {
                if (DateTime.UtcNow >= deadline) { runNote = "giro interrotto per tempo: continua al prossimo"; break; }
                // Se nel frattempo qualcuno ha messo in pausa o annullato la campagna, ci si ferma subito.
                if (++done % 5 == 0 && await _repo.StatusAsync(id) != "in_corso") { runNote = "giro fermato: la campagna non è più in invio"; break; }
                pending.Remove(r.Id);
                if (await _contacts.IsOptedOutAsync(c.OrganizationId, r.Phone)) { await _repo.MarkAsync(r.Id, "saltato", "nella lista STOP", null); skipped++; continue; }
                if (await _repo.HasOpenConversationAsync(number.Id, r.Phone)) { await _repo.MarkAsync(r.Id, "saltato", "ha già una conversazione aperta con l'attività", null); skipped++; continue; }
                // Limite di campagne per cliente: chi ne ha già ricevute abbastanza negli ultimi 30 giorni non viene disturbato.
                if (_config.Current.Meta.MaxCampaignsPerCustomer is int maxC and > 0
                    && await _repo.RecentCampaignsAsync(c.OrganizationId, r.Phone, c.Id) >= maxC)
                { await _repo.MarkAsync(r.Id, "saltato", $"ha già ricevuto {maxC} {(maxC == 1 ? "campagna" : "campagne")} negli ultimi 30 giorni", null); skipped++; continue; }

                var values = Values(c, r, offer);
                // Un dato mancante non si sostituisce con un trattino: il cliente riceverebbe un messaggio strano.
                var missing = template!.Variables.FirstOrDefault(v => !TemplateText.TryValue(values, v, out var x) || string.IsNullOrWhiteSpace(x));
                if (missing is not null) { await _repo.MarkAsync(r.Id, "saltato", $"manca il dato «{missing}» richiesto dal primo messaggio", null); skipped++; continue; }

                var res = await _send.SendTemplateAsync(number, template!, r.Phone, values, null);
                if (!res.Ok)
                {
                    var err = res.Error ?? "errore sconosciuto";
                    if (AccountErrors.Any(err.Contains) || res.MessageId == 0)
                    {
                        pending.Add(r.Id); // non è colpa del destinatario: tornerà in coda
                        await Pause(id, "Meta ha rifiutato l'invio: " + err);
                        return (sent, skipped, errors, "pausa: " + err);
                    }
                    if (TemplateErrors.Any(err.Contains))
                    {
                        pending.Add(r.Id);
                        await Pause(id, "il template del primo messaggio ha un problema per Meta: " + err);
                        return (sent, skipped, errors, "pausa: " + err);
                    }
                    if (SlowDownErrors.Any(err.Contains)) { pending.Add(r.Id); runNote = "Meta chiede di rallentare: si continua al prossimo giro"; break; }
                    if (TransientErrors.Any(err.Contains))
                    {
                        await _repo.RetryLaterAsync(r.Id, err);
                        runNote = "Meta momentaneamente non disponibile: si riprova al prossimo giro";
                        break;
                    }
                    await _repo.MarkAsync(r.Id, "errore", err, null);
                    errors++;
                    if (++inARow >= MaxErrorsInARow)
                    {
                        await Pause(id, $"{MaxErrorsInARow} errori di fila nell'invio (l'ultimo: {err}): controlla template e numero prima di riprendere");
                        return (sent, skipped, errors, "pausa: troppi errori di fila");
                    }
                    continue;
                }
                inARow = 0;
                var conv = new Conversation
                {
                    OrganizationId = c.OrganizationId, GymId = c.GymId, WaNumberId = number.Id, ContactPhone = r.Phone, ContactName = r.FirstName,
                    Membership = r.Membership, ExpiresOn = r.ExpiresOn, GoalModelId = c.GoalModelId, OfferId = c.OfferId, CampaignId = c.Id,
                    Service = r.Service, Notes = r.Notes
                };
                var convId = await _convs.CreateAsync(conv);
                await _wa.SetMessageConversationAsync(res.MessageId, convId);
                await _repo.MarkAsync(r.Id, "inviato", null, convId);
                sent++;
            }
        }
        finally { await _repo.ReleaseAsync(pending); }

        if (await _repo.RemainingAsync(id) == 0)
        {
            await _repo.SetStatusAsync(id, "completata", null, new[] { "in_corso" });
            runNote = "tutti i destinatari sono stati lavorati";
        }
        var summary = $"inviati {sent}" + (skipped > 0 ? $", saltati {skipped}" : "") + (errors > 0 ? $", errori {errors}" : "");
        var final = runNote ?? limitNote;
        await _repo.NoteRunAsync(id, final is null ? summary : $"{summary} · {final}");
        return (sent, skipped, errors, final);
    }
}

/// <summary>Finché l'applicazione è accesa, fa un giro di invio ogni 30 secondi. Se l'hosting la spegne, riparte con /jobs/tick.</summary>
public sealed class CampaignWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes; private readonly AppConfigStore _config; private readonly ILogger<CampaignWorker> _log;
    public CampaignWorker(IServiceScopeFactory scopes, AppConfigStore config, ILogger<CampaignWorker> log) { _scopes = scopes; _config = config; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_config.Current.Installed)
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<CampaignSender>().RunAsync(TimeSpan.FromSeconds(25));
                }
                catch (Exception ex) { _log.LogError(ex, "Giro di invio delle campagne non riuscito"); }
            }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
