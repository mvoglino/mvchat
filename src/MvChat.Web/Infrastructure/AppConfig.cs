using System.Security.Cryptography;
using System.Text.Json;

namespace MvChat.Web.Infrastructure;

/// <summary>
/// Configurazione scritta dall'installazione guidata in App_Data/mvchat.json.
/// Su hosting condiviso non si possono impostare variabili d'ambiente,
/// quindi la connessione al database vive in questo file, fuori da wwwroot.
/// </summary>
public sealed class AppConfig
{
    public bool Installed { get; set; }
    public string ProductName { get; set; } = "mvchat";
    public string ConnectionString { get; set; } = "";
    /// <summary>Chiave segreta per l'indirizzo richiamato dall'operazione pianificata di Aruba.</summary>
    public string JobToken { get; set; } = "";
    public DateTime? InstalledAt { get; set; }
    public MetaSettings Meta { get; set; } = new();
    public AiSettings Ai { get; set; } = new();
    public BillingSettings Billing { get; set; } = new();
    public PrivacySettings Privacy { get; set; } = new();
}

/// <summary>Per quanto tempo si tengono i dati dei clienti. Dopo, la pulizia automatica li cancella.</summary>
public sealed class PrivacySettings
{
    /// <summary>Conversazioni, messaggi, liste contatti, destinatari delle campagne e registro attività.</summary>
    public int RetentionMonths { get; set; } = 12;
    /// <summary>Copie grezze degli avvisi di Meta (contengono i messaggi): servono solo per controllare problemi recenti.</summary>
    public int WebhookDays { get; set; } = 30;
}

/// <summary>Regole con cui MVitalia rifattura il servizio. Quando un mese viene chiuso, i valori usati restano salvati nel rendiconto.</summary>
public sealed class BillingSettings
{
    /// <summary>Canone mensile di base per ogni attività (si può cambiare attività per attività).</summary>
    public decimal DefaultMonthlyFeeEur { get; set; }
    /// <summary>Ricarico sul costo del fornitore AI.</summary>
    public decimal AiMarkupPct { get; set; } = 20m;
    /// <summary>Quanti euro vale un dollaro: il fornitore AI fattura in dollari.</summary>
    public decimal UsdToEur { get; set; } = 0.90m;
    public decimal VatPct { get; set; } = 22m;
    /// <summary>Chi emette i rendiconti: nome e dati (ragione sociale, P.IVA, indirizzo) stampati in testa.</summary>
    public string IssuerName { get; set; } = "MVitalia";
    public string? IssuerDetails { get; set; }
}

/// <summary>Fornitore AI scelto da MVitalia. Le chiavi sono salvate cifrate.</summary>
public sealed class AiSettings
{
    /// <summary>"anthropic", "openai" oppure "" (assistente spento: le risposte passano agli operatori).</summary>
    public string Provider { get; set; } = "";
    public AiProviderSettings Anthropic { get; set; } = new()
    {
        Model = "claude-haiku-4-5-20251001", BaseUrl = "https://api.anthropic.com", InputPrice = 1m, OutputPrice = 5m, CacheReadPrice = 0.10m
    };
    public AiProviderSettings OpenAi { get; set; } = new()
    {
        Model = "gpt-5-mini", BaseUrl = "https://api.openai.com", InputPrice = 0.25m, OutputPrice = 2m, CacheReadPrice = 0.025m
    };
    [System.Text.Json.Serialization.JsonIgnore]
    public AiProviderSettings Current => Provider == "openai" ? OpenAi : Anthropic;

    /// <summary>Trascrizione dei vocali dei clienti (con la chiave OpenAI, anche se l'assistente è Anthropic).</summary>
    public bool Transcribe { get; set; }
    public string TranscribeModel { get; set; } = "gpt-4o-mini-transcribe";
    /// <summary>Prezzo in dollari per minuto di audio, dal listino OpenAI.</summary>
    public decimal TranscribePricePerMinute { get; set; } = 0.003m;
    /// <summary>Vocali più lunghi: la trascrizione resta, ma risponde una persona.</summary>
    public int TranscribeMaxSeconds { get; set; } = 180;
}

public sealed class AiProviderSettings
{
    public string KeyEnc { get; set; } = "";
    public string Model { get; set; } = "";
    /// <summary>Cambia solo nelle prove automatiche, dove il fornitore è sostituito da un finto server.</summary>
    public string BaseUrl { get; set; } = "";
    /// <summary>Prezzi in dollari per milione di token, presi dal listino del fornitore: servono a calcolare i consumi.</summary>
    public decimal InputPrice { get; set; }
    public decimal OutputPrice { get; set; }
    public decimal CacheReadPrice { get; set; }
}

/// <summary>Dati dell'app Meta di MVitalia. Si inseriscono dalla pagina Impostazioni WhatsApp.</summary>
public sealed class MetaSettings
{
    public string AppId { get; set; } = "";
    /// <summary>Chiave segreta dell'app: serve a controllare che i messaggi in arrivo vengano davvero da Meta.</summary>
    public string AppSecret { get; set; } = "";
    /// <summary>Parola d'ordine che Meta usa una sola volta per confermare l'indirizzo del webhook.</summary>
    public string WebhookVerifyToken { get; set; } = "";
    public string GraphVersion { get; set; } = "v23.0";
    /// <summary>Cambia solo nelle prove automatiche, dove Meta è sostituita da un finto server.</summary>
    public string GraphBaseUrl { get; set; } = "https://graph.facebook.com";
    /// <summary>Listino Meta per l'Italia (€ per messaggio): serve solo a stimare nei report quanto pagano le attività a Meta.</summary>
    public decimal MarketingPriceEur { get; set; } = 0.0658m;
    public decimal UtilityPriceEur { get; set; } = 0.0248m;
    /// <summary>
    /// Quante campagne può ricevere lo stesso cliente negli ultimi 30 giorni dallo stesso gruppo (o attività singola).
    /// Protegge i clienti dall'insistenza e il numero WhatsApp dalle segnalazioni. 0 = nessun limite.
    /// </summary>
    public int MaxCampaignsPerCustomer { get; set; } = 2;
}

public sealed class AppConfigStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _path;
    private readonly object _lock = new();
    private AppConfig _current;

    public AppConfigStore(IWebHostEnvironment env)
    {
        DataDir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(DataDir);
        _path = Path.Combine(DataDir, "mvchat.json");
        _current = Load();
    }

    public string DataDir { get; }
    public AppConfig Current { get { lock (_lock) return _current; } }

    /// <summary>
    /// Se il file c'è ma non si legge, mvchat si ferma invece di ripartire "da installare":
    /// altrimenti chiunque potrebbe rifare l'installazione e collegarlo a un altro database.
    /// </summary>
    private AppConfig Load()
    {
        if (!File.Exists(_path)) return new AppConfig();
        try { return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_path)) ?? throw new InvalidDataException("file vuoto"); }
        catch (Exception ex) { throw new InvalidOperationException($"App_Data/mvchat.json non è leggibile ({ex.Message}). Ripristina il file dal backup: mvchat non riparte da solo per sicurezza.", ex); }
    }

    private string CodePath => Path.Combine(DataDir, "codice-installazione.txt");

    /// <summary>
    /// Codice richiesto dall'installazione guidata: sta in App_Data/codice-installazione.txt (si legge via FTP),
    /// così può installare solo chi ha accesso ai file del sito. Nelle prove automatiche arriva da MVCHAT_INSTALL_CODE.
    /// </summary>
    public string InstallCode()
    {
        var env = Environment.GetEnvironmentVariable("MVCHAT_INSTALL_CODE");
        if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
        lock (_lock)
        {
            if (!File.Exists(CodePath)) File.WriteAllText(CodePath, NewToken()[..12]);
            return File.ReadAllText(CodePath).Trim();
        }
    }

    public void DeleteInstallCode() { try { File.Delete(CodePath); } catch { } }

    public void Save(AppConfig cfg)
    {
        lock (_lock)
        {
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(cfg, Json));
            File.Move(tmp, _path, overwrite: true);
            _current = cfg;
        }
    }

    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    /// <summary>Verifica che App_Data sia scrivibile: su Aruba va dato il permesso di scrittura dal pannello.</summary>
    public bool CanWrite(out string error)
    {
        try
        {
            var probe = Path.Combine(DataDir, ".write-test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            error = "";
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }
}
