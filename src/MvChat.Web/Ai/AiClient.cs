using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using MvChat.Web.Infrastructure;

namespace MvChat.Web.Ai;

public sealed record AiTurn(string Role, string Text); // Role: "user" (cliente) o "assistant" (assistente)

public sealed record AiResult(bool Ok, string? Text, string? Error, string Provider, string Model,
    int InputTokens, int OutputTokens, int CacheReadTokens, int CacheWriteTokens)
{
    public decimal CostUsd(AiProviderSettings p) =>
        ((InputTokens * p.InputPrice) + (OutputTokens * p.OutputPrice) + (CacheReadTokens * p.CacheReadPrice)
         + (CacheWriteTokens * p.InputPrice * 1.25m)) / 1_000_000m;
}

public sealed record Transcription(bool Ok, string? Text, decimal Seconds, string Model, string? Error)
{
    public decimal CostUsd(AiSettings a) => Math.Round(Seconds / 60m * a.TranscribePricePerMinute, 6);
    public AiResult AsUsage() => new(Ok, Text, Error, "openai", Model, 0, 0, 0, 0);
}

/// <summary>
/// Un solo punto di contatto con il fornitore AI. Anthropic e OpenAI hanno formati diversi:
/// qui si traducono, così il resto di mvchat non sa (e non deve sapere) chi sta rispondendo.
/// </summary>
public sealed class AiClient
{
    private readonly HttpClient _http;
    private readonly AppConfigStore _config;
    private readonly IDataProtector _protector;

    public AiClient(HttpClient http, AppConfigStore config, IDataProtectionProvider dp)
    {
        _http = http; _config = config;
        _protector = dp.CreateProtector("mvchat.ai.key.v1");
    }

    public string Protect(string key) => _protector.Protect(key.Trim());
    private string? Key(AiProviderSettings p)
    {
        if (string.IsNullOrEmpty(p.KeyEnc)) return null;
        try { return _protector.Unprotect(p.KeyEnc); } catch { return null; }
    }

    public string Provider => _config.Current.Ai.Provider;
    public bool Enabled => Provider is "anthropic" or "openai" && Key(_config.Current.Ai.Current) is not null;
    public AiProviderSettings Settings => _config.Current.Ai.Current;
    public AiSettings AiConfig => _config.Current.Ai;

    public async Task<AiResult> ChatAsync(string system, IReadOnlyList<AiTurn> turns, int maxTokens = 1000)
    {
        var provider = Provider;
        var s = Settings;
        var key = Key(s);
        if (provider is not ("anthropic" or "openai") || key is null)
            return new AiResult(false, null, "Assistente AI non configurato (Impostazioni → AI).", provider, s.Model, 0, 0, 0, 0);
        // I fornitori vogliono la conversazione che inizia con il cliente.
        var list = turns.SkipWhile(t => t.Role != "user").ToList();
        if (list.Count == 0) list.Add(new AiTurn("user", "(il cliente non ha ancora scritto)"));
        return provider == "anthropic" ? await AnthropicAsync(s, key, system, list, maxTokens) : await OpenAiAsync(s, key, system, list, maxTokens);
    }

    private async Task<AiResult> AnthropicAsync(AiProviderSettings s, string key, string system, List<AiTurn> turns, int maxTokens)
    {
        var body = new JsonObject
        {
            ["model"] = s.Model,
            ["max_tokens"] = maxTokens,
            // Le istruzioni sono uguali a ogni risposta della stessa conversazione: la cache le fa costare circa un decimo.
            ["system"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = system, ["cache_control"] = new JsonObject { ["type"] = "ephemeral" } }),
            ["messages"] = new JsonArray(Merge(turns).Select(t => (JsonNode)new JsonObject { ["role"] = t.Role, ["content"] = t.Text }).ToArray())
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, s.BaseUrl.TrimEnd('/') + "/v1/messages")
        { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        req.Headers.Add("x-api-key", key);
        req.Headers.Add("anthropic-version", "2023-06-01");
        var (ok, json, error) = await SendAsync(req);
        if (!ok) return new AiResult(false, null, error, "anthropic", s.Model, 0, 0, 0, 0);
        var text = string.Concat((json?["content"]?.AsArray() ?? new()).Where(c => c?["type"]?.GetValue<string>() == "text").Select(c => c!["text"]!.GetValue<string>()));
        var u = json?["usage"];
        return new AiResult(true, text, null, "anthropic", s.Model, Int(u?["input_tokens"]), Int(u?["output_tokens"]),
            Int(u?["cache_read_input_tokens"]), Int(u?["cache_creation_input_tokens"]));
    }

    private async Task<AiResult> OpenAiAsync(AiProviderSettings s, string key, string system, List<AiTurn> turns, int maxTokens)
    {
        var messages = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = system });
        foreach (var t in Merge(turns)) messages.Add(new JsonObject { ["role"] = t.Role, ["content"] = t.Text });
        var body = new JsonObject
        {
            ["model"] = s.Model,
            ["messages"] = messages,
            ["max_completion_tokens"] = maxTokens * 4, // i modelli che "ragionano" usano parte dei token per pensare
            ["response_format"] = new JsonObject { ["type"] = "json_object" }
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, s.BaseUrl.TrimEnd('/') + "/v1/chat/completions")
        { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        req.Headers.Add("Authorization", "Bearer " + key);
        var (ok, json, error) = await SendAsync(req);
        if (!ok) return new AiResult(false, null, error, "openai", s.Model, 0, 0, 0, 0);
        var text = json?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
        var u = json?["usage"];
        var cached = Int(u?["prompt_tokens_details"]?["cached_tokens"]);
        return new AiResult(true, text, null, "openai", s.Model, Int(u?["prompt_tokens"]) - cached, Int(u?["completion_tokens"]), cached, 0);
    }

    /// <summary>Trascrizione dei vocali accesa e chiave OpenAI inserita (Anthropic non trascrive l'audio).</summary>
    public bool TranscribeEnabled => _config.Current.Ai.Transcribe && Key(_config.Current.Ai.OpenAi) is not null;

    /// <summary>Trasforma un vocale in testo con OpenAI. I secondi servono a calcolare il costo (se OpenAI non li dice, si stimano dalla grandezza del file).</summary>
    public async Task<Transcription> TranscribeAsync(byte[] audio, string fileName, string? mime)
    {
        var a = _config.Current.Ai;
        var key = Key(a.OpenAi);
        var model = string.IsNullOrWhiteSpace(a.TranscribeModel) ? "gpt-4o-mini-transcribe" : a.TranscribeModel;
        var estimate = Math.Max(1, audio.Length / 2500m); // un vocale di WhatsApp occupa circa 2-3 KB al secondo
        if (key is null) return new Transcription(false, null, 0, model, "Chiave OpenAI non inserita (Impostazioni AI).");
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(audio);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(string.IsNullOrWhiteSpace(mime) ? "audio/ogg" : mime.Split(';')[0].Trim());
        form.Add(file, "file", fileName);
        form.Add(new StringContent(model), "model");
        form.Add(new StringContent("it"), "language");
        form.Add(new StringContent("json"), "response_format");
        using var req = new HttpRequestMessage(HttpMethod.Post, a.OpenAi.BaseUrl.TrimEnd('/') + "/v1/audio/transcriptions") { Content = form };
        req.Headers.Add("Authorization", "Bearer " + key);
        var (ok, json, error) = await SendAsync(req);
        if (!ok) return new Transcription(false, null, estimate, model, error);
        var u = json?["usage"];
        decimal seconds = estimate;
        try { if (u?["type"]?.GetValue<string>() == "duration") seconds = u["seconds"]!.GetValue<decimal>(); } catch { }
        try { if (json?["duration"] is JsonNode d) seconds = d.GetValue<decimal>(); } catch { }
        return new Transcription(true, json?["text"]?.GetValue<string>()?.Trim() ?? "", seconds, model, null);
    }

    /// <summary>Due messaggi di fila dello stesso autore diventano uno solo: alcuni fornitori lo richiedono.</summary>
    private static List<AiTurn> Merge(List<AiTurn> turns)
    {
        var res = new List<AiTurn>();
        foreach (var t in turns)
            if (res.Count > 0 && res[^1].Role == t.Role) res[^1] = res[^1] with { Text = res[^1].Text + "\n" + t.Text };
            else res.Add(t);
        return res;
    }

    private static int Int(JsonNode? n) { try { return n?.GetValue<int>() ?? 0; } catch { return 0; } }

    private async Task<(bool, JsonNode?, string?)> SendAsync(HttpRequestMessage req)
    {
        try
        {
            using var res = await _http.SendAsync(req);
            var text = await res.Content.ReadAsStringAsync();
            JsonNode? json = null;
            try { json = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text); } catch { }
            if (res.IsSuccessStatusCode) return (true, json, null);
            var msg = json?["error"]?["message"]?.GetValue<string>() ?? $"risposta {(int)res.StatusCode}";
            return (false, json, $"Il fornitore AI ha risposto con un errore: {msg}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (false, null, "Fornitore AI non raggiungibile: " + ex.Message);
        }
    }
}
