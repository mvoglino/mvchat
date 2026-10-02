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
        Model = "gpt-5-mini", BaseUrl = "https://api.openai.com"
    };
    [System.Text.Json.Serialization.JsonIgnore]
    public AiProviderSettings Current => Provider == "openai" ? OpenAi : Anthropic;
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

    private AppConfig Load()
    {
        if (!File.Exists(_path)) return new AppConfig();
        try { return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_path)) ?? new AppConfig(); }
        catch { return new AppConfig(); }
    }

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
