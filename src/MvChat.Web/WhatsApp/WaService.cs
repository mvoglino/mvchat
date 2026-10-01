using Microsoft.AspNetCore.DataProtection;
using MvChat.Web.Contacts;

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
    public WaService(WaRepo repo, CloudApi api, IDataProtectionProvider dp)
    {
        _repo = repo; _api = api;
        _protector = dp.CreateProtector("mvchat.wa.token.v1");
    }

    public string Protect(string token) => _protector.Protect(token.Trim());
    private string? Token(WaNumber n)
    {
        if (string.IsNullOrEmpty(n.AccessTokenEnc)) return null;
        try { return _protector.Unprotect(n.AccessTokenEnc); } catch { return null; }
    }

    private static string? NotReady(WaNumber n, string? token) =>
        n.IsSimulated ? null
        : string.IsNullOrEmpty(n.PhoneNumberId) ? "Manca l'identificativo del numero (Phone number ID)."
        : token is null ? "Manca la chiave di accesso del numero, oppure va reinserita."
        : null;

    public async Task<SendResult> SendTemplateAsync(WaNumber n, WaTemplate t, string to, IDictionary<string, string?> values, int? userId)
    {
        if (!t.IsApproved) return new SendResult(false, "Il template non è ancora approvato da Meta.", 0);
        var parameters = t.Variables.Select(v => values.TryGetValue(v, out var x) && !string.IsNullOrWhiteSpace(x) ? x! : "-").ToList();
        var preview = TemplateText.Fill(t.Body, values);
        if (n.IsSimulated)
        {
            var id = await _repo.InsertMessageAsync(n, to, "out", "template", preview, t.Name, "sim-" + Guid.NewGuid().ToString("N"), "delivered", null, userId);
            return new SendResult(true, null, id);
        }
        var token = Token(n);
        if (NotReady(n, token) is { } why) return new SendResult(false, why, 0);
        var r = await _api.SendTemplateAsync(n.PhoneNumberId!, token!, to, t.Name, t.Language, parameters);
        var mid = await _repo.InsertMessageAsync(n, to, "out", "template", preview, t.Name, r.Id, r.Ok ? "sent" : "failed", r.Error, userId);
        return new SendResult(r.Ok, r.Error, mid);
    }

    /// <summary>Messaggio libero: Meta lo consegna solo entro 24 ore dall'ultimo messaggio del cliente.</summary>
    public async Task<SendResult> SendTextAsync(WaNumber n, string to, string text, int? userId)
    {
        if (n.IsSimulated)
        {
            var id = await _repo.InsertMessageAsync(n, to, "out", "text", text, null, "sim-" + Guid.NewGuid().ToString("N"), "delivered", null, userId);
            return new SendResult(true, null, id);
        }
        var token = Token(n);
        if (NotReady(n, token) is { } why) return new SendResult(false, why, 0);
        var r = await _api.SendTextAsync(n.PhoneNumberId!, token!, to, text);
        var mid = await _repo.InsertMessageAsync(n, to, "out", "text", text, null, r.Id, r.Ok ? "sent" : "failed", r.Error, userId);
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
        await _repo.SetNumberCheckAsync(n.Id, "attivo",
            r.Body?["quality_rating"]?.GetValue<string>(), r.Body?["messaging_limit_tier"]?.GetValue<string>(),
            r.Body?["verified_name"]?.GetValue<string>(), null);
        return null;
    }

    public static string MapTemplateStatus(string meta) => meta.ToUpperInvariant() switch
    {
        "APPROVED" => "approvato",
        "REJECTED" => "rifiutato",
        "PENDING" or "IN_APPEAL" or "PENDING_DELETION" => "in revisione",
        "PAUSED" => "in pausa",
        "DISABLED" => "disattivato",
        _ => "in revisione"
    };

    public static string StatusLabel(string s) => s switch
    {
        "queued" => "In coda", "sent" => "Inviato", "delivered" => "Consegnato", "read" => "Letto", "failed" => "Non consegnato",
        "received" => "Ricevuto", _ => s
    };

    // ---------- Parole che significano "non scrivetemi più" ----------
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "stop", "basta", "cancellami", "disiscrivimi", "unsubscribe", "non scrivetemi più", "non scrivetemi piu",
        "non scrivermi più", "non scrivermi piu", "non voglio più messaggi", "non voglio piu messaggi", "rimuovimi"
    };

    public static bool IsStop(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = System.Text.RegularExpressions.Regex.Replace(text.Trim().ToLowerInvariant(), @"[!.,;\s]+$", "");
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\s+", " ");
        return StopWords.Contains(t);
    }
}
