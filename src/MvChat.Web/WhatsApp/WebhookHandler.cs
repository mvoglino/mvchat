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
    private readonly ILogger<WebhookHandler> _log;

    public WebhookHandler(AppConfigStore config, WaRepo repo, WaService wa, ContactsRepo contacts, ConversationRepo convs, AiQueue queue, ILogger<WebhookHandler> log)
    { _config = config; _repo = repo; _wa = wa; _contacts = contacts; _convs = convs; _queue = queue; _log = log; }

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
            var root = JsonNode.Parse(payload);
            foreach (var entry in root?["entry"]?.AsArray() ?? new())
            foreach (var change in entry?["changes"]?.AsArray() ?? new())
            {
                var field = change?["field"]?.GetValue<string>();
                var value = change?["value"];
                if (value is null) continue;
                if (field == "message_template_status_update") await TemplateStatusAsync(value);
                else await MessagesAsync(value);
            }
            await _repo.MarkEventAsync(eventId, null);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Evento WhatsApp {Id} non elaborato", eventId);
            await _repo.MarkEventAsync(eventId, ex.Message.Length > 480 ? ex.Message[..480] : ex.Message);
        }
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

        foreach (var st in value["statuses"]?.AsArray() ?? new())
        {
            var id = st?["id"]?.GetValue<string>();
            var status = st?["status"]?.GetValue<string>();
            if (id is null || status is null) continue;
            var err = st!["errors"]?[0];
            var error = err is null ? null : $"{err["title"]?.GetValue<string>() ?? err["message"]?.GetValue<string>()} (codice {err["code"]})";
            await _repo.UpdateStatusAsync(id, status, error);
        }

        foreach (var m in value["messages"]?.AsArray() ?? new())
        {
            var id = m?["id"]?.GetValue<string>();
            var from = m?["from"]?.GetValue<string>();
            if (id is null || from is null) continue;
            if (await _repo.MessageExistsAsync(id)) continue; // Meta a volte manda due volte lo stesso messaggio
            var phone = "+" + from.TrimStart('+');
            var type = m!["type"]?.GetValue<string>() ?? "text";
            var text = type switch
            {
                "text" => m["text"]?["body"]?.GetValue<string>(),
                "button" => m["button"]?["text"]?.GetValue<string>(),
                "interactive" => m["interactive"]?["button_reply"]?["title"]?.GetValue<string>() ?? m["interactive"]?["list_reply"]?["title"]?.GetValue<string>(),
                _ => $"[{type}]"
            };
            var conv = await _convs.FindForInboundAsync(number.Id, phone);
            var msgId = await _repo.InsertMessageAsync(number, phone, "in", type == "text" ? "text" : type, text, null, id, "received", null, null, conv?.Id);

            // Chi scrive STOP esce subito, per tutta la struttura, e riceve una conferma.
            if (WaService.IsStop(text))
            {
                await _contacts.AddOptOutAsync(number.OrganizationId, number.GymId, phone, $"Ha scritto: {text}", "whatsapp", null);
                await _wa.SendTextAsync(number, phone, $"Fatto: non riceverai più messaggi promozionali da {number.GymName}. Per qualsiasi cosa puoi sempre contattare la reception.", null, conv?.Id);
                if (conv is not null) await _convs.SetStateAsync(conv.Id, "chiusa", Outcomes.OptOut, $"Ha scritto: {text}");
                continue;
            }
            if (conv is null || conv.Outcome == Outcomes.OptOut) continue; // messaggio fuori da campagne: lo vede chi consulta il registro

            if (conv.Status == "ai")
            {
                await _convs.InboundAsync(conv.Id, needsReply: true, reopenAs: null);
                _queue.Enqueue(conv.Id); // risponde l'assistente AI
            }
            else
            {
                // Se la conversazione era chiusa e il cliente riscrive, la riprende una persona.
                await _convs.InboundAsync(conv.Id, needsReply: false, reopenAs: conv.Status == "chiusa" ? "operatore" : null);
            }
        }
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
