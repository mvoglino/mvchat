using Microsoft.AspNetCore.DataProtection;

namespace MvChat.Web.WhatsApp;

public sealed record SendResult(bool Ok, string? Error, long MessageId);

/// <summary>
/// Le operazioni WhatsApp di mvchat. Per i numeri simulati non si contatta Meta:
/// tutto viene registrato come se fosse successo, così si può provare il software senza account.
/// </summary>
public sealed class WaService
{
    private readonly WaRepo _repo;
    private readonly CloudApi _api;
    private readonly IDataProtector _protector;
    private readonly MvChat.Web.Infrastructure.AppConfigStore _config;
    public WaService(WaRepo repo, CloudApi api, IDataProtectionProvider dp, MvChat.Web.Infrastructure.AppConfigStore config)
    {
        _repo = repo; _api = api; _config = config;
        _protector = dp.CreateProtector("mvchat.wa.token.v1");
    }

    public string Protect(string token) => _protector.Protect(token.Trim());
    private string? Token(WaNumber n)
    {
        if (string.IsNullOrEmpty(n.AccessTokenEnc)) return null;
        try { return _protector.Unprotect(n.AccessTokenEnc); } catch { return null; }
    }

    /// <summary>Risposta automatica a chi chiede di non essere più contattato.</summary>
    public static string StopConfirmation(string gymName) =>
        $"Fatto: non riceverai più messaggi promozionali da {gymName}. Per qualsiasi cosa puoi sempre contattare la reception.";

    /// <summary>Scarica da Meta un file mandato dal cliente (vocale, foto). I numeri simulati non hanno file veri.</summary>
    public async Task<(byte[]? Bytes, string? Mime, string? Error, bool Gone)> DownloadMediaAsync(WaNumber n, string mediaId, long maxBytes)
    {
        if (n.IsSimulated) return (null, null, "numero simulato: nessun file", true);
        var token = Token(n);
        if (token is null) return (null, null, "chiave di accesso del numero non valida", false);
        var info = await _api.MediaInfoAsync(mediaId, token);
        if (!info.Ok)
            // Meta tiene i file per un tempo limitato: se non lo trova più, è inutile riprovare.
            return (null, null, info.Error, info.Error?.Contains("codice 100") == true || info.Error?.Contains("codice 131052") == true);
        var url = info.Body?["url"]?.GetValue<string>();
        var size = info.Body?["file_size"]?.ToString();
        if (long.TryParse(size, out var sz) && sz > maxBytes) return (null, null, "file troppo grande", true);
        if (url is null) return (null, null, "Meta non ha indicato dove scaricare il file", false);
        var (bytes, error) = await _api.DownloadMediaAsync(url, token, maxBytes);
        return (bytes, info.Body?["mime_type"]?.GetValue<string>(), error, error == "file troppo grande");
    }

    private static string? NotReady(WaNumber n, string? token) =>
        n.IsSimulated ? null
        : string.IsNullOrEmpty(n.PhoneNumberId) ? "Manca l'identificativo del numero (Phone number ID)."
        : token is null ? "Manca la chiave di accesso del numero, oppure va reinserita."
        : null;

    public async Task<SendResult> SendTemplateAsync(WaNumber n, WaTemplate t, string to, IDictionary<string, string?> values, int? userId, long? conversationId = null)
    {
        if (!t.IsApproved) return new SendResult(false, "Il template non è ancora approvato da Meta.", 0);
        // Meta rifiuta i valori con a capo, tabulazioni o più di 4 spazi di fila: si puliscono prima dell'invio.
        var parameters = t.Variables.Select(v => TemplateText.TryValue(values, v, out var x) && !string.IsNullOrWhiteSpace(x)
            ? System.Text.RegularExpressions.Regex.Replace(x!, @"\s+", " ").Trim() : "-").ToList();
        var preview = TemplateText.Fill(t.Body, values);
        if (n.IsSimulated)
        {
            var id = await _repo.InsertMessageAsync(n, to, "out", "template", preview, t.Name, "sim-" + Guid.NewGuid().ToString("N"), "delivered", null, userId, conversationId);
            return new SendResult(true, null, id);
        }
        var token = Token(n);
        if (NotReady(n, token) is { } why) return new SendResult(false, why, 0);
        var r = await _api.SendTemplateAsync(n.PhoneNumberId!, token!, to, t.Name, t.Language, parameters);
        var mid = await _repo.InsertMessageAsync(n, to, "out", "template", preview, t.Name, r.Id, r.Ok ? "sent" : "failed", r.Error, userId, conversationId);
        return new SendResult(r.Ok, r.Error, mid);
    }

    /// <summary>Messaggio libero: Meta lo consegna solo entro 24 ore dall'ultimo messaggio del cliente.</summary>
    public async Task<SendResult> SendTextAsync(WaNumber n, string to, string text, int? userId, long? conversationId = null)
    {
        if (n.IsSimulated)
        {
            var id = await _repo.InsertMessageAsync(n, to, "out", "text", text, null, "sim-" + Guid.NewGuid().ToString("N"), "delivered", null, userId, conversationId);
            return new SendResult(true, null, id);
        }
        var token = Token(n);
        if (NotReady(n, token) is { } why) return new SendResult(false, why, 0);
        var r = await _api.SendTextAsync(n.PhoneNumberId!, token!, to, text);
        var mid = await _repo.InsertMessageAsync(n, to, "out", "text", text, null, r.Id, r.Ok ? "sent" : "failed", r.Error, userId, conversationId);
        return new SendResult(r.Ok, r.Error, mid);
    }

    /// <summary>Invia il template a Meta per l'approvazione. Sui numeri simulati è approvato subito.</summary>
    public async Task<(bool Ok, string? Error)> SubmitTemplateAsync(WaNumber n, WaTemplate t)
    {
        if (n.IsSimulated) { await _repo.SetTemplateStatusAsync(t.Id, "approvato", "sim-" + t.Id, null); return (true, null); }
        var token = Token(n);
        if (string.IsNullOrEmpty(n.WabaId)) return (false, "Manca l'identificativo dell'account WhatsApp (WABA ID).");
        if (token is null) return (false, "Manca la chiave di accesso del numero.");
        var metaBody = TemplateText.ToMeta(t.Body, t.Variables);
        var examples = t.Variables.Select(v => TemplateText.Placeholders.GetValueOrDefault(v, "esempio"));
        var r = await _api.CreateTemplateAsync(n.WabaId!, token, t.Name, t.Language, t.Category, metaBody, examples);
        if (!r.Ok) { await _repo.SetTemplateStatusAsync(t.Id, "errore", null, r.Error); return (false, r.Error); }
        var status = MapTemplateStatus(r.Body?["status"]?.GetValue<string>() ?? "PENDING");
        await _repo.SetTemplateStatusAsync(t.Id, status, r.Id, null);
        return (true, null);
    }

    /// <summary>Riallinea gli stati dei template con quelli di Meta (utile se un avviso è andato perso).</summary>
    public async Task<(int Updated, string? Error)> RefreshTemplatesAsync(WaNumber n)
    {
        if (n.IsSimulated) return (0, null);
        var token = Token(n);
        if (string.IsNullOrEmpty(n.WabaId) || token is null) return (0, "Numero non collegato del tutto: mancano WABA ID o chiave.");
        var r = await _api.TemplatesAsync(n.WabaId!, token);
        if (!r.Ok) return (0, r.Error);
        var updated = 0;
        foreach (var item in r.Body?["data"]?.AsArray() ?? new())
        {
            var id = item?["id"]?.GetValue<string>();
            if (id is null) continue;
            updated += await _repo.SetTemplateStatusByMetaAsync(id, MapTemplateStatus(item!["status"]?.GetValue<string>() ?? ""), item["rejected_reason"]?.GetValue<string>() is { } rr && rr != "NONE" ? rr : null);
        }
        return (updated, null);
    }

    /// <summary>Chiede a Meta i dati del numero: nome verificato, qualità, limite di invio.</summary>
    public async Task<string?> CheckNumberAsync(WaNumber n)
    {
        if (n.IsSimulated) { await _repo.SetNumberCheckAsync(n.Id, "attivo", "simulato", "nessun limite", null, null); return null; }
        var token = Token(n);
        if (NotReady(n, token) is { } why) { await _repo.SetNumberCheckAsync(n.Id, "incompleto", null, null, null, why); return why; }
        var r = await _api.PhoneInfoAsync(n.PhoneNumberId!, token!);
        if (!r.Ok) { await _repo.SetNumberCheckAsync(n.Id, "errore", null, null, null, r.Error); return r.Error; }
        var warning = await EnsureSubscribedAsync(n, token!);
        await _repo.SetNumberCheckAsync(n.Id, "attivo",
            r.Body?["quality_rating"]?.GetValue<string>(), LimitOf(r.Body),
            r.Body?["verified_name"]?.GetValue<string>(), warning);
        return warning;
    }

    /// <summary>
    /// Il limite di invio letto da Meta, scritto sempre come TIER_250 / TIER_2K / TIER_UNLIMITED.
    /// Prima il limite dell'azienda (dal 7 ottobre 2025 è quello che vale), altrimenti il vecchio limite del numero.
    /// </summary>
    public static string? LimitOf(System.Text.Json.Nodes.JsonNode? body)
    {
        static string? Text(System.Text.Json.Nodes.JsonNode? n) => n switch
        {
            null => null,
            System.Text.Json.Nodes.JsonValue v when v.TryGetValue<string>(out var s) => s,
            _ => n.ToJsonString()
        };
        var raw = Text(body?["whatsapp_business_manager_messaging_limit"]) ?? Text(body?["messaging_limit_tier"]);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (raw.ToUpperInvariant().Contains("UNLIMITED")) return "TIER_UNLIMITED";
        var n = MvChat.Web.Campaigns.SendWindows.MetaDailyLimit(raw, false) ?? 0;
        return n >= 1000 && n % 1000 == 0 ? $"TIER_{n / 1000}K" : $"TIER_{n}";
    }

    /// <summary>
    /// Perché Meta mandi a mvchat i messaggi dei clienti, l'app deve essere iscritta all'account WhatsApp Business.
    /// A volte (account creati a mano, numeri di prova) l'iscrizione manca: mvchat la controlla e, se serve, la fa da sé.
    /// Restituisce un avviso da mostrare se non ci riesce.
    /// </summary>
    private async Task<string?> EnsureSubscribedAsync(WaNumber n, string token)
    {
        if (string.IsNullOrEmpty(n.WabaId)) return null;
        var appId = _config.Current.Meta.AppId?.Trim() ?? "";
        var list = await _api.SubscribedAppsAsync(n.WabaId, token);
        // Non basta che ci sia "un'app" iscritta: deve esserci proprio quella di mvchat (ID app in Impostazioni Meta).
        if (list.Ok && IsSubscribed(list.Body, appId)) return null;
        var sub = await _api.SubscribeAppAsync(n.WabaId, token);
        if (!sub.Ok) return "Meta non manderà a mvchat i messaggi dei clienti finché l'app non è iscritta all'account WhatsApp: iscrizione non riuscita (" + sub.Error + ")";
        if (appId.Length == 0) return null;
        var after = await _api.SubscribedAppsAsync(n.WabaId, token);
        if (!after.Ok || IsSubscribed(after.Body, appId)) return null;
        return $"Meta non manderà a mvchat i messaggi dei clienti: all'account WhatsApp è iscritta un'altra app ({AppNames(after.Body)}), non quella di mvchat (ID app {appId}). "
             + "La chiave di accesso è stata generata per un'altra app: rigenerala dall'utente di sistema scegliendo l'app con ID " + appId + ", inseriscila qui e premi «Controlla collegamento».";
    }

    private static IEnumerable<System.Text.Json.Nodes.JsonNode?> Apps(System.Text.Json.Nodes.JsonNode? body) =>
        body?["data"] is System.Text.Json.Nodes.JsonArray a ? a : Enumerable.Empty<System.Text.Json.Nodes.JsonNode?>();

    private static bool IsSubscribed(System.Text.Json.Nodes.JsonNode? body, string appId)
    {
        var apps = Apps(body).ToList();
        if (appId.Length == 0) return apps.Count > 0; // ID app non inserito: ci si accontenta come prima
        return apps.Any(x => (x?["whatsapp_business_api_data"]?["id"]?.ToString() ?? x?["id"]?.ToString()) == appId);
    }

    private static string AppNames(System.Text.Json.Nodes.JsonNode? body)
    {
        var names = Apps(body).Select(x => x?["whatsapp_business_api_data"]?["name"]?.ToString() is { Length: > 0 } nm
            ? $"{nm}, ID {x?["whatsapp_business_api_data"]?["id"]}" : "ID " + x?["whatsapp_business_api_data"]?["id"]).ToList();
        return names.Count == 0 ? "nessuna" : string.Join("; ", names);
    }

    public static string MapTemplateStatus(string meta) => meta.ToUpperInvariant() switch
    {
        "APPROVED" or "REINSTATED" or "FLAGGED" => "approvato", // FLAGGED: ancora utilizzabile, ma Meta segnala qualità bassa
        "REJECTED" => "rifiutato",
        "PENDING" or "IN_APPEAL" or "PENDING_DELETION" => "in revisione",
        "PAUSED" => "in pausa",
        "DISABLED" or "DELETED" => "disattivato",
        _ => "in revisione"
    };

    public static string StatusLabel(string s) => s switch
    {
        "queued" => "In coda", "sent" => "Inviato", "delivered" => "Consegnato", "read" => "Letto", "failed" => "Non consegnato",
        "received" => "Ricevuto", _ => s
    };

    // ---------- Parole che significano "non scrivetemi più" ----------
    // Una sola parola (STOP, basta, cancellami…) oppure una frase chiara («non scrivetemi più», «toglietemi dalla lista»).
    private static readonly System.Text.RegularExpressions.Regex StopWord = new(
        @"^(stop+|basta|cancellami|cancellatemi|disiscrivimi|disiscrivetemi|unsubscribe|rimuovimi|rimuovetemi|toglimi|toglietemi|annulla iscrizione|no grazie stop|stop grazie|basta messaggi|stop messaggi)( (per favore|grazie|subito))?$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    private static readonly System.Text.RegularExpressions.Regex StopPhrase = new(
        @"\bnon (mi )?(scrivete|scrivetemi|scrivermi|scrivere|contattate|contattatemi|contattarmi|mandatemi|mandate|inviatemi|inviate)\b.*\b(più|piu)\b"
        + @"|\bnon (voglio|desidero) (più|piu) (ricevere )?(messaggi|notifiche|comunicazioni|promozioni)"
        + @"|\b(toglietemi|toglimi|rimuovetemi|rimuovimi|cancellatemi|cancellami) (dalla|dalle|dai) (lista|liste|contatti|messaggi)"
        + @"|\b(smettete|smettetela) di (scrivermi|contattarmi|mandarmi)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public static bool IsStop(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim().ToLowerInvariant();
        t = System.Text.RegularExpressions.Regex.Replace(t, @"[^\p{L}\p{N}' ]+", " "); // via punteggiatura ed emoji
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\s+", " ").Trim();
        if (t.Length == 0) return false;
        return StopWord.IsMatch(t) || (t.Length <= 200 && StopPhrase.IsMatch(t));
    }
}
