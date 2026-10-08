using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MvChat.Web.Infrastructure;

namespace MvChat.Web.WhatsApp;

public sealed record ApiResult(bool Ok, string? Id, string? Error, JsonNode? Body);

/// <summary>
/// Chiamate alla WhatsApp Cloud API di Meta. Nessuna logica di mvchat qui dentro:
/// solo la traduzione da "cosa vogliamo fare" a "richiesta HTTP che Meta capisce".
/// </summary>
public sealed class CloudApi
{
    private readonly HttpClient _http;
    private readonly AppConfigStore _config;
    public CloudApi(HttpClient http, AppConfigStore config) { _http = http; _config = config; }

    private string Url(string path)
    {
        var m = _config.Current.Meta;
        return $"{m.GraphBaseUrl.TrimEnd('/')}/{m.GraphVersion}/{path}";
    }

    /// <summary>I numeri vanno a Meta senza il "+": +393331234567 → 393331234567.</summary>
    public static string To(string phone) => phone.TrimStart('+');

    public Task<ApiResult> SendTemplateAsync(string phoneNumberId, string token, string to, string templateName, string language, IEnumerable<string> bodyParams) =>
        PostAsync($"{phoneNumberId}/messages", token, new JsonObject
        {
            ["messaging_product"] = "whatsapp",
            ["to"] = To(to),
            ["type"] = "template",
            ["template"] = new JsonObject
            {
                ["name"] = templateName,
                ["language"] = new JsonObject { ["code"] = language },
                ["components"] = new JsonArray(new JsonObject
                {
                    ["type"] = "body",
                    ["parameters"] = new JsonArray(bodyParams.Select(p => (JsonNode)new JsonObject { ["type"] = "text", ["text"] = p }).ToArray())
                })
            }
        }, r => r?["messages"]?[0]?["id"]?.GetValue<string>());

    public Task<ApiResult> SendTextAsync(string phoneNumberId, string token, string to, string text) =>
        PostAsync($"{phoneNumberId}/messages", token, new JsonObject
        {
            ["messaging_product"] = "whatsapp",
            ["to"] = To(to),
            ["type"] = "text",
            ["text"] = new JsonObject { ["body"] = text, ["preview_url"] = true }
        }, r => r?["messages"]?[0]?["id"]?.GetValue<string>());

    public Task<ApiResult> CreateTemplateAsync(string wabaId, string token, string name, string language, string category, string metaBody, IEnumerable<string> examples)
    {
        var body = new JsonObject { ["type"] = "BODY", ["text"] = metaBody };
        var ex = examples.ToList();
        // Gli esempi servono solo se il testo ha dei segnaposto: un elenco vuoto Meta lo rifiuta.
        if (ex.Count > 0) body["example"] = new JsonObject { ["body_text"] = new JsonArray(new JsonArray(ex.Select(e => (JsonNode)JsonValue.Create(e)!).ToArray())) };
        return PostAsync($"{wabaId}/message_templates", token, new JsonObject
        {
            ["name"] = name,
            ["language"] = language,
            ["category"] = category,
            ["components"] = new JsonArray(body)
        }, r => r?["id"]?.GetValue<string>());
    }

    public Task<ApiResult> TemplatesAsync(string wabaId, string token) =>
        GetAsync($"{wabaId}/message_templates?fields=name,status,language,rejected_reason,id&limit=200", token);

    public Task<ApiResult> PhoneInfoAsync(string phoneNumberId, string token) =>
        GetAsync($"{phoneNumberId}?fields=display_phone_number,verified_name,quality_rating,messaging_limit_tier", token);

    /// <summary>Dove scaricare un file mandato dal cliente (vocale, foto): Meta risponde con un indirizzo valido pochi minuti.</summary>
    public Task<ApiResult> MediaInfoAsync(string mediaId, string token) => GetAsync(Uri.EscapeDataString(mediaId), token);

    /// <summary>Scarica il file. La chiave si manda solo agli indirizzi di Meta (o al finto server delle prove).</summary>
    public async Task<(byte[]? Bytes, string? Error)> DownloadMediaAsync(string url, string token, long maxBytes)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return (null, "indirizzo del file non valido");
        var graphHost = Uri.TryCreate(_config.Current.Meta.GraphBaseUrl, UriKind.Absolute, out var g) ? g.Host : "";
        var trusted = u.Scheme == "https" && (u.Host.EndsWith(".fbsbx.com") || u.Host.EndsWith(".facebook.com") || u.Host.EndsWith(".whatsapp.net"))
                      || u.Host == graphHost;
        if (!trusted) return (null, "indirizzo del file non di Meta: scaricamento rifiutato");
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, u);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (!res.IsSuccessStatusCode) return (null, $"Meta ha risposto {(int)res.StatusCode}");
            if (res.Content.Headers.ContentLength > maxBytes) return (null, "file troppo grande");
            await using var s = await res.Content.ReadAsStreamAsync();
            using var ms = new MemoryStream();
            var buf = new byte[81920]; int n;
            while ((n = await s.ReadAsync(buf)) > 0)
            {
                ms.Write(buf, 0, n);
                if (ms.Length > maxBytes) return (null, "file troppo grande");
            }
            return (ms.ToArray(), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return (null, "Meta non raggiungibile: " + ex.Message); }
    }

    private async Task<ApiResult> PostAsync(string path, string token, JsonObject body, Func<JsonNode?, string?> id)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Url(path))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await SendAsync(req, id);
    }

    private async Task<ApiResult> GetAsync(string path, string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, Url(path));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await SendAsync(req, _ => null);
    }

    private async Task<ApiResult> SendAsync(HttpRequestMessage req, Func<JsonNode?, string?> id)
    {
        try
        {
            using var res = await _http.SendAsync(req);
            var text = await res.Content.ReadAsStringAsync();
            JsonNode? json = null;
            try { json = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text); } catch (JsonException) { }
            if (res.IsSuccessStatusCode) return new ApiResult(true, id(json), null, json);
            var err = json?["error"];
            var msg = err?["error_user_msg"]?.GetValue<string>() ?? err?["message"]?.GetValue<string>() ?? $"Meta ha risposto {(int)res.StatusCode}";
            var code = err?["code"]?.ToString();
            return new ApiResult(false, null, code is null ? msg : $"{msg} (codice {code})", json);
        }
        catch (HttpRequestException ex)
        {
            return new ApiResult(false, null, "Meta non raggiungibile: " + ex.Message, null);
        }
        catch (TaskCanceledException)
        {
            // Nessuna risposta in tempo: il messaggio potrebbe essere partito lo stesso, quindi non si riprova da soli.
            return new ApiResult(false, null, "Meta non ha risposto in tempo: invio incerto, controlla prima di riprovare", null);
        }
    }
}
