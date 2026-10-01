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
