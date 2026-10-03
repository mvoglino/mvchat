using System.Threading.Channels;
using MvChat.Web.Catalog;
using MvChat.Web.Contacts;
using MvChat.Web.Infrastructure;
using MvChat.Web.WhatsApp;

namespace MvChat.Web.Ai;

/// <summary>
/// Il cuore del Passo 5: quando un cliente scrive, l'assistente legge la conversazione,
/// chiede la risposta all'AI, la controlla e la manda su WhatsApp, aggiornando l'esito.
/// </summary>
public sealed class AssistantService
{
    private readonly ConversationRepo _conv;
    private readonly CatalogRepo _catalog;
    private readonly WaRepo _waRepo;
    private readonly WaService _wa;
    private readonly ContactsRepo _contacts;
    private readonly AiClient _ai;
    private readonly ILogger<AssistantService> _log;

    public AssistantService(ConversationRepo conv, CatalogRepo catalog, WaRepo waRepo, WaService wa, ContactsRepo contacts, AiClient ai, ILogger<AssistantService> log)
    { _conv = conv; _catalog = catalog; _waRepo = waRepo; _wa = wa; _contacts = contacts; _ai = ai; _log = log; }

    /// <summary>Le istruzioni complete per una conversazione: le stesse dell'anteprima del Passo 3, più il formato di risposta.</summary>
    public async Task<(string System, GoalModel Model, Offer? Offer, GymProfile Profile)?> BuildSystemAsync(Conversation c, string? firstMessage)
    {
        if (c.GoalModelId is not int goalId) return null; // messaggio spontaneo: nessun obiettivo, risponde una persona
        var model = (await _catalog.ModelsAsync(new MvChat.Web.Security.Scope { Role = MvChat.Web.Security.Roles.SuperAdmin }, goalId)).FirstOrDefault();
        if (model is null) return null;
        var offer = c.OfferId is int oid ? (await _catalog.OffersAsync(new MvChat.Web.Security.Scope { Role = MvChat.Web.Security.Roles.SuperAdmin }, offerId: oid)).FirstOrDefault() : null;
        var profile = await _catalog.ProfileAsync(c.GymId);
        var who = new Recipient(c.ContactName, null, c.Membership, c.ExpiresOn);
        var org = await _catalog.ActivityInfoAsync(c.GymId);
        var system = PromptBuilder.Build(model, c.GymName, profile, offer, who, DateTime.UtcNow.ToRome().Date, org);
        if (!string.IsNullOrWhiteSpace(firstMessage))
            system += $"\n## Primo messaggio già inviato al cliente\n\"{firstMessage}\"\n";
        system += "\n" + Guardrails.OutputFormat;
        return (system, model, offer, profile);
    }

    /// <returns>true se nel frattempo il cliente ha scritto ancora: la conversazione va rimessa in coda.</returns>
    public async Task<bool> ProcessAsync(long conversationId)
    {
        if (!await _conv.ClaimAsync(conversationId)) return false;
        var again = false;
        try { again = await ReplyAsync(conversationId); }
        catch (Exception ex)
        {
            _log.LogError(ex, "Assistente: errore sulla conversazione {Id}", conversationId);
            // Meglio una persona che un cliente senza risposta.
            try { await _conv.SetStateAsync(conversationId, "operatore", Outcomes.Operatore, "errore imprevisto dell'assistente: risponde una persona"); }
            catch (Exception ex2) { _log.LogError(ex2, "Assistente: impossibile passare la conversazione {Id} alla reception", conversationId); }
        }
        finally { await _conv.ReleaseAsync(conversationId); }
        return again;
    }

    private async Task<bool> ReplyAsync(long id)
    {
        var c = await _conv.GetAsync(id);
        if (c is null || c.Status != "ai") return false;
        var number = await _waRepo.NumberAsync(c.WaNumberId);
        if (number is null) { await _conv.SetStateAsync(c.Id, "operatore", Outcomes.Operatore, "il numero WhatsApp dell'attività non è più collegato"); return false; }
        // Su WhatsApp si può rispondere liberamente solo entro 24 ore dall'ultimo messaggio del cliente.
        if (!number.IsSimulated && (c.LastInboundAt is not DateTime li || li < DateTime.UtcNow.AddHours(-24)))
        { await _conv.SetStateAsync(c.Id, "operatore", Outcomes.Operatore, "passate 24 ore dall'ultimo messaggio del cliente: serve un template"); return false; }

        var messages = await _conv.MessagesAsync(id, 40);
        var first = messages.FirstOrDefault(m => m.Direction == "out" && m.Kind == "template")?.Body;
        var built = await BuildSystemAsync(c, first);
        if (built is null) { await HandOffAsync(c, number, "modello di obiettivo non trovato", sendHolding: true); return false; }
        var (system, model, offer, profile) = built.Value;

        if (c.AiReplies >= model.MaxAiMessages) { await HandOffAsync(c, number, $"raggiunto il limite di {model.MaxAiMessages} risposte dell'assistente", sendHolding: true); return false; }
        if (!_ai.Enabled) { await HandOffAsync(c, number, "assistente AI spento o non configurato", sendHolding: true); return false; }

        var turns = messages.Where(m => !(m.Direction == "out" && m.Kind == "template") && m.Kind != "reaction")
            .Select(m => new AiTurn(m.Direction == "in" ? "user" : "assistant", m.Body ?? "")).ToList();
        var r = await _ai.ChatAsync(system, turns);
        await _conv.LogUsageAsync(c.OrganizationId, c.GymId, c.Id, c.IsTest ? "prova" : "chat", r, r.CostUsd(_ai.Settings));
        // Mentre l'AI pensava, una persona ha preso la conversazione o il cliente ha scritto STOP: non si manda nulla.
        if (await _conv.FreshStateAsync(c.Id) is not { Status: "ai" } fresh) return false;
        // Il cliente ha scritto un altro messaggio nel frattempo: si risponde una volta sola, a tutto insieme.
        if (fresh.NeedsReply) return true;

        if (!r.Ok) { await HandOffAsync(c, number, "l'AI non ha risposto: " + r.Error, sendHolding: true); return false; }

        var reply = Guardrails.Parse(r.Text);
        if (reply is null) { await HandOffAsync(c, number, "risposta dell'AI non leggibile", sendHolding: true); return false; }
        if (Guardrails.PriceProblem(reply.Text, offer) is { } priceIssue) { await HandOffAsync(c, number, priceIssue, sendHolding: true); return false; }
        if (Guardrails.PercentProblem(reply.Text, offer) is { } pctIssue) { await HandOffAsync(c, number, pctIssue, sendHolding: true); return false; }

        var text = Guardrails.EnsureDisclosure(reply.Text, c.AiReplies == 0, profile.AssistantName, c.GymName);
        var sent = await _wa.SendTextAsync(number, c.ContactPhone, text, null, c.Id);
        if (!sent.Ok) { await _conv.SetStateAsync(c.Id, "operatore", Outcomes.Operatore, "invio WhatsApp non riuscito: " + sent.Error); return false; }

        var (status, outcome) = reply.Outcome switch
        {
            Outcomes.Operatore => ("operatore", Outcomes.Operatore),
            Outcomes.OptOut => ("chiusa", Outcomes.OptOut),
            Outcomes.Raggiunto => ("chiusa", Outcomes.Raggiunto),
            Outcomes.Rifiuto => ("chiusa", Outcomes.Rifiuto),
            _ => ("ai", Outcomes.InCorso)
        };
        if (outcome == Outcomes.OptOut && !c.IsTest) // le prove non toccano la lista STOP vera
            await _contacts.AddOptOutAsync(c.OrganizationId, c.GymId, c.ContactPhone, "Riconosciuto dall'assistente: " + (reply.Note ?? "non vuole essere contattato"), "assistente", null);
        await _conv.AfterAiReplyAsync(c.Id, status, outcome, reply.Note);
        return false;
    }

    /// <summary>Passa la conversazione a una persona. Se il cliente aspetta una risposta, riceve un messaggio di cortesia.</summary>
    private async Task HandOffAsync(Conversation c, WaNumber number, string reason, bool sendHolding)
    {
        if (sendHolding) await _wa.SendTextAsync(number, c.ContactPhone, Guardrails.HoldingMessage, null, c.Id);
        await _conv.SetStateAsync(c.Id, "operatore", Outcomes.Operatore, reason);
    }
}

/// <summary>Coda in memoria: le risposte partono subito, senza far aspettare Meta.</summary>
public sealed class AiQueue
{
    private readonly Channel<long> _ch = Channel.CreateUnbounded<long>();
    public void Enqueue(long conversationId) => _ch.Writer.TryWrite(conversationId);
    public IAsyncEnumerable<long> ReadAllAsync(CancellationToken ct) => _ch.Reader.ReadAllAsync(ct);
}

/// <summary>
/// Lavora la coda delle risposte. Se l'hosting riavvia l'applicazione e la coda in memoria si perde,
/// l'operazione pianificata (/jobs/tick) riprende le conversazioni rimaste in attesa.
/// </summary>
public sealed class AiWorker : BackgroundService
{
    private readonly AiQueue _queue;
    private readonly IServiceScopeFactory _scopes; private readonly ILogger<AiWorker> _log;
    public AiWorker(AiQueue queue, IServiceScopeFactory scopes, ILogger<AiWorker> log) { _queue = queue; _scopes = scopes; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var id in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopes.CreateScope();
                if (await scope.ServiceProvider.GetRequiredService<AssistantService>().ProcessAsync(id))
                    _queue.Enqueue(id); // il cliente ha aggiunto un messaggio: nuova risposta che li comprende tutti
            }
            catch (Exception ex) { _log.LogError(ex, "Coda dell'assistente: errore sulla conversazione {Id}", id); } // il lavoro continua con le altre
        }
    }
}
