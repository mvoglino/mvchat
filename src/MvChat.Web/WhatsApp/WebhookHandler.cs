using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using MvChat.Web.Ai;
using MvChat.Web.Contacts;
using MvChat.Web.Infrastructure;

namespace MvChat.Web.WhatsApp;

/// <summary>
/// Riceve da Meta i messaggi dei clienti e gli aggiornamenti di stato (consegnato, letto, errore, template approvato).
/// Ogni chiamata viene prima controllata con la firma di Meta: senza firma valida non si tocca nulla.
/// </summary>
public sealed class WebhookHandler
{
    private readonly AppConfigStore _config;
    private readonly WaRepo _repo;
    private readonly WaService _wa;
    private readonly ContactsRepo _contacts;
    private readonly ConversationRepo _convs;
    private readonly AiQueue _queue;
    private readonly MediaStore _media;
    private readonly AiClient _ai;
    private readonly ILogger<WebhookHandler> _log;

    public WebhookHandler(AppConfigStore config, WaRepo repo, WaService wa, ContactsRepo contacts, ConversationRepo convs, AiQueue queue, MediaStore media, AiClient ai, ILogger<WebhookHandler> log)
    { _config = config; _repo = repo; _wa = wa; _contacts = contacts; _convs = convs; _queue = queue; _media = media; _ai = ai; _log = log; }

    /// <summary>Conferma iniziale dell'indirizzo: Meta manda una parola d'ordine e si aspetta indietro il "challenge".</summary>
    public string? Verify(string? mode, string? token, string? challenge)
    {
        var expected = _config.Current.Meta.WebhookVerifyToken;
        return mode == "subscribe" && !string.IsNullOrEmpty(expected) && FixedEquals(token ?? "", expected) ? challenge : null;
    }

    public bool SignatureOk(byte[] body, string? header)
    {
        var secret = _config.Current.Meta.AppSecret;
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(header) || !header.StartsWith("sha256=")) return false;
        var expected = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body)).ToLowerInvariant();
        return FixedEquals(header.ToLowerInvariant(), expected);
    }

    private static bool FixedEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    /// <summary>Registra e lavora un evento. Usato sia dal webhook vero sia dal simulatore.</summary>
    public async Task ProcessAsync(string payload)
    {
        var eventId = await _repo.LogEventAsync(payload);
        try
        {
            await DispatchAsync(payload);
            await _repo.MarkEventAsync(eventId, null);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Evento WhatsApp {Id} non elaborato", eventId);
            await _repo.MarkEventAsync(eventId, ex.Message.Length > 480 ? ex.Message[..480] : ex.Message);
        }
    }

    private async Task DispatchAsync(string payload)
    {
        var root = JsonNode.Parse(payload);
        foreach (var entry in root?["entry"]?.AsArray() ?? new())
        foreach (var change in entry?["changes"]?.AsArray() ?? new())
        {
            var field = change?["field"]?.GetValue<string>();
            var value = change?["value"];
            if (value is null) continue;
            if (field == "message_template_status_update") await TemplateStatusAsync(value);
            else if (field is "phone_number_quality_update" or "phone_number_name_update" or "account_update") await NumberUpdateAsync(value);
            else await MessagesAsync(value);
        }
    }

    /// <summary>Riprova gli avvisi rimasti non elaborati (dall'operazione pianificata). I messaggi già registrati non si ripetono.</summary>
    public async Task<int> RetryFailedAsync()
    {
        var n = 0;
        foreach (var (id, payload) in await _repo.RetryEventsAsync(20))
        {
            try
            {
                await DispatchAsync(payload);
                await _repo.MarkEventAsync(id, null);
                n++;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Evento WhatsApp {Id}: nuovo tentativo non riuscito", id);
                await _repo.MarkEventAsync(id, ex.Message);
            }
        }
        return n;
    }

    /// <summary>Lo stesso pacchetto che manderebbe Meta per un messaggio di testo: lo usa il simulatore dei numeri finti.</summary>
    public static string SimulatedInbound(string phoneNumberId, string phone, string text) => new JsonObject
    {
        ["object"] = "whatsapp_business_account",
        ["entry"] = new JsonArray(new JsonObject
        {
            ["id"] = "sim",
            ["changes"] = new JsonArray(new JsonObject
            {
                ["field"] = "messages",
                ["value"] = new JsonObject
                {
                    ["messaging_product"] = "whatsapp",
                    ["metadata"] = new JsonObject { ["phone_number_id"] = phoneNumberId },
                    ["messages"] = new JsonArray(new JsonObject
                    {
                        ["from"] = phone.TrimStart('+'), ["id"] = "sim-in-" + Guid.NewGuid().ToString("N"),
                        ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ["type"] = "text",
                        ["text"] = new JsonObject { ["body"] = text.Trim() }
                    })
                }
            })
        })
    }.ToJsonString();

    private async Task MessagesAsync(JsonNode value)
    {
        var phoneNumberId = value["metadata"]?["phone_number_id"]?.GetValue<string>();
        if (phoneNumberId is null) return;
        var number = await _repo.NumberByPhoneNumberIdAsync(phoneNumberId);
        if (number is null) { _log.LogWarning("Messaggio per un numero sconosciuto: {Id}", phoneNumberId); return; }
        // Un numero Meta riceve i messaggi solo se Meta ha confermato almeno una volta numero e chiave:
        // nessuno può "prenotare" il numero di un altro. Se poi la chiave scade, i messaggi (e gli STOP) continuano ad arrivare.
        if (!number.IsSimulated && number.VerifiedAt is null) { _log.LogWarning("Messaggio per un numero mai verificato: {Id}", phoneNumberId); return; }

        Exception? failure = null;
        foreach (var st in value["statuses"]?.AsArray() ?? new())
        {
            try { await StatusAsync(number, st); }
            catch (Exception ex) { failure ??= ex; }
        }
        // Ogni messaggio per conto suo: se uno dà errore, gli altri vengono lavorati lo stesso e l'avviso si riprova più tardi.
        foreach (var m in value["messages"]?.AsArray() ?? new())
        {
            try { await InboundAsync(number, m); }
            catch (Exception ex) { failure ??= ex; }
        }
        if (failure is not null) throw failure;
    }

    /// <summary>Stato di un messaggio inviato. Se Meta non è riuscita a consegnarlo, lo si riporta su destinatario e conversazione.</summary>
    private async Task StatusAsync(WaNumber number, JsonNode? st)
    {
        var id = st?["id"]?.GetValue<string>();
        var status = st?["status"]?.GetValue<string>();
        if (id is null || status is null) return;
        var err = st!["errors"]?[0];
        var code = err?["code"]?.ToString();
        var error = err is null ? null : $"{err["title"]?.GetValue<string>() ?? err["message"]?.GetValue<string>()} (codice {code})";
        await _repo.UpdateStatusAsync(id, status, error);
        if (status != "failed") return;

        var sent = await _repo.SentMessageAsync(id);
        if (sent is null) return;
        // 131050: il cliente ha bloccato i messaggi promozionali di questa attività su WhatsApp → va in lista STOP.
        if (code == "131050")
            await _contacts.AddOptOutAsync(sent.OrganizationId, sent.GymId, sent.Phone, "Ha disattivato i messaggi promozionali su WhatsApp (Meta 131050)", "meta", null);
        if (sent.ConversationId is long cid && sent.Kind == "template")
        {
            // Il primo messaggio della campagna non è arrivato: il destinatario risulta in errore e la conversazione si chiude.
            await _convs.TemplateFailedAsync(cid, WaRepo.Clip("messaggio non consegnato: " + (FriendlyError(code) ?? error), 300)!);
        }
    }

    /// <summary>Spiegazione in italiano semplice degli errori di consegna più frequenti.</summary>
    public static string? FriendlyError(string? code) => code switch
    {
        "131026" => "il numero non usa WhatsApp o non può ricevere il messaggio",
        "131049" => "Meta ha limitato i messaggi promozionali verso questo cliente (troppi ricevuti di recente): riprovare più avanti",
        "131050" => "il cliente ha bloccato i messaggi promozionali: aggiunto alla lista STOP",
        "131047" => "sono passate più di 24 ore dall'ultimo messaggio del cliente",
        "131042" => "problema di pagamento dell'account WhatsApp su Meta",
        "130472" => "il cliente fa parte di un test di Meta e non riceve messaggi promozionali",
        _ => null
    };

    private static readonly Dictionary<string, string> MediaLabels = new()
    {
        ["image"] = "[immagine]", ["audio"] = "[messaggio vocale]", ["voice"] = "[messaggio vocale]", ["video"] = "[video]", ["document"] = "[documento]",
        ["sticker"] = "[adesivo]", ["location"] = "[posizione]", ["contacts"] = "[contatto]", ["order"] = "[ordine]"
    };

    private async Task InboundAsync(WaNumber number, JsonNode? m)
    {
        var id = m?["id"]?.GetValue<string>();
        var from = m?["from"]?.GetValue<string>();
        if (id is null || from is null) return;
        if (await _repo.MessageExistsAsync(id)) return; // Meta a volte manda due volte lo stesso messaggio
        var phone = "+" + from.TrimStart('+');
        var type = m!["type"]?.GetValue<string>() ?? "text";
        if (type is "system" or "unsupported" or "ephemeral") return; // avvisi tecnici di WhatsApp, non scritti dal cliente
        var isText = type is "text" or "button" or "interactive";
        var text = type switch
        {
            "text" => m["text"]?["body"]?.GetValue<string>(),
            "button" => m["button"]?["text"]?.GetValue<string>(),
            "interactive" => m["interactive"]?["button_reply"]?["title"]?.GetValue<string>() ?? m["interactive"]?["list_reply"]?["title"]?.GetValue<string>(),
            "reaction" => m["reaction"]?["emoji"]?.GetValue<string>() is { Length: > 0 } e ? $"[reazione {e}]" : "[reazione tolta]",
            _ => (MediaLabels.GetValueOrDefault(type, $"[{type}]")) + (m[type]?["caption"]?.GetValue<string>() is { Length: > 0 } cap ? " " + cap : "")
        };
        var conv = await _convs.FindForInboundAsync(number.Id, phone);

        // Chi scrive STOP esce subito, per tutto il gruppo: prima si aggiorna la lista STOP, poi tutto il resto.
        var stop = isText && WaService.IsStop(text);
        if (stop)
        {
            await _contacts.AddOptOutAsync(number.OrganizationId, number.GymId, phone, WaRepo.Clip($"Ha scritto: {text}", 200), "whatsapp", null);
            if (conv is not null) await _convs.SetStateAsync(conv.Id, "chiusa", Outcomes.OptOut, WaRepo.Clip($"Ha scritto: {text}", 480));
        }

        var msgId = await _repo.InsertMessageAsync(number, phone, "in", isText ? "text" : type, text, null, id, "received", null, null, conv?.Id);
        try
        {
            // Vocali e foto: si ricorda il riferimento di Meta per scaricare il file (in reception o per la trascrizione).
            var mediaId = MediaStore.Kinds.Contains(type) ? m[type]?["id"]?.GetValue<string>() : null;
            if (mediaId is not null) await _media.SetMediaAsync(msgId, mediaId, m[type]?["mime_type"]?.GetValue<string>());

            if (stop)
            {
                await _wa.SendTextAsync(number, phone, WaService.StopConfirmation(number.GymName), null, conv?.Id);
                return;
            }
            if (type == "reaction") return; // una reazione (👍) non chiede una risposta

            if (conv is null)
            {
                // Messaggio fuori da campagne: lo vede la reception, come conversazione da gestire.
                var name = await _contacts.NameForPhoneAsync(number.GymId, phone);
                var newId = await _convs.CreateSpontaneousAsync(number, phone, name ?? phone);
                await _repo.SetMessageConversationAsync(msgId, newId);
                return;
            }
            if (conv.Outcome == Outcomes.OptOut) return; // ha chiesto STOP: il messaggio resta registrato, l'assistente non risponde

            if (conv.Status == "ai")
            {
                if (MediaStore.IsVoice(type) && mediaId is not null && !number.IsSimulated && _ai.TranscribeEnabled)
                {
                    // Vocale: l'assistente lo fa trascrivere e risponde come a un messaggio scritto.
                    await _convs.InboundAsync(conv.Id, needsReply: true, reopenAs: null);
                    _queue.Enqueue(conv.Id);
                }
                else if (!isText)
                {
                    // L'assistente legge solo testo: foto, vocali e documenti li guarda una persona.
                    await _convs.InboundAsync(conv.Id, needsReply: false, reopenAs: null);
                    await _wa.SendTextAsync(number, phone, Ai.Guardrails.HoldingMessage, null, conv.Id);
                    await _convs.SetStateAsync(conv.Id, "operatore", Outcomes.Operatore, $"il cliente ha mandato {text}: serve una persona");
                }
                else if (string.IsNullOrWhiteSpace(text)) await _convs.InboundAsync(conv.Id, needsReply: false, reopenAs: null);
                else
                {
                    await _convs.InboundAsync(conv.Id, needsReply: true, reopenAs: null);
                    _queue.Enqueue(conv.Id); // risponde l'assistente AI
                }
            }
            else if (conv.Status == "chiusa" && conv.Outcome == Outcomes.Rifiuto && conv.ReasonAsk == 1
                     && conv.LastMessageAt > DateTime.UtcNow.AddHours(-24)
                     && (isText && !IsCourtesy(text) || MediaStore.IsVoice(type) && mediaId is not null && !number.IsSimulated && _ai.TranscribeEnabled))
            {
                // Risponde alla domanda sul motivo del rifiuto: lo legge l'assistente, che aggiorna il motivo e ringrazia.
                await _convs.ReopenForReasonAsync(conv.Id);
                _queue.Enqueue(conv.Id);
            }
            else
            {
                // Se la conversazione era chiusa e il cliente riscrive, la riprende una persona (non per un semplice «grazie»).
                var reopen = conv.Status == "chiusa" && !(isText && IsCourtesy(text)) ? "operatore" : null;
                await _convs.InboundAsync(conv.Id, needsReply: false, reopenAs: reopen);
            }
        }
        catch
        {
            // Il messaggio si toglie, così quando l'avviso viene riprovato si rifà tutto il lavoro.
            await _repo.DeleteMessageAsync(msgId);
            throw;
        }
    }

    private static readonly System.Text.RegularExpressions.Regex Courtesy = new(
        @"^(ok+|okay|va bene|perfetto|grazie( mille| ancora| a te| anche a te)?|buona (giornata|serata)|a presto|ricevuto|d'accordo)[\s!.,]*$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>«Grazie», «ok», un'emoji: un saluto finale che non chiede di riaprire la conversazione.</summary>
    public static bool IsCourtesy(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        var t = text.Trim();
        if (!t.Any(char.IsLetterOrDigit)) return true; // solo emoji o punteggiatura
        if (t.Length > 40) return false;
        // Toglie le emoji in fondo («Grazie 🙏»).
        t = new string(t.Where(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch) || ".,!'".Contains(ch)).ToArray()).Trim();
        return Courtesy.IsMatch(t);
    }

    /// <summary>Meta avvisa quando cambia la qualità o il limite di invio di un numero: si aggiornano subito i dati.</summary>
    private async Task NumberUpdateAsync(JsonNode value)
    {
        var display = value["display_phone_number"]?.ToString();
        if (string.IsNullOrEmpty(display)) return;
        var digits = new string(display.Where(char.IsDigit).ToArray());
        foreach (var n in await _repo.MetaNumbersAsync())
            if (new string(n.DisplayPhone.Where(char.IsDigit).ToArray()).EndsWith(digits.Length > 9 ? digits[^9..] : digits))
                await _wa.CheckNumberAsync(n);
    }

    private async Task TemplateStatusAsync(JsonNode value)
    {
        var id = value["message_template_id"]?.ToString();
        var ev = value["event"]?.GetValue<string>();
        if (id is null || ev is null) return;
        var reason = value["reason"]?.GetValue<string>();
        await _repo.SetTemplateStatusByMetaAsync(id, WaService.MapTemplateStatus(ev), reason is null or "NONE" ? null : reason);
    }
}
