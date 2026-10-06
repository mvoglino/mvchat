using System.Collections.Concurrent;
using System.Text;

namespace MvChat.Web.Infrastructure;

/// <summary>
/// Registro tecnico in App_Data/logs/mvchat-AAAAMMGG.log: avvii, chiusure, avvisi ed errori.
/// Sull'hosting condiviso non si vedono i log di sistema, quindi mvchat tiene il suo (14 giorni, poi si cancella da solo).
/// Non contiene messaggi dei clienti né password: solo cosa è successo al programma.
/// </summary>
public sealed class FileLogProvider : ILoggerProvider
{
    private readonly string _dir;
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    private DateTime _lastCleanup = DateTime.MinValue;

    public FileLogProvider(string dataDir)
    {
        _dir = Path.Combine(dataDir, "logs");
        try { Directory.CreateDirectory(_dir); } catch { /* senza cartella il registro tace, il programma no */ }
    }

    public ILogger CreateLogger(string categoryName) => _loggers.GetOrAdd(categoryName, c => new FileLogger(this, c));

    /// <summary>Scrive una riga; se il disco non risponde, la riga si perde ma mvchat continua.</summary>
    public void Write(string level, string category, string message, Exception? ex)
    {
        try
        {
            var now = DateTime.UtcNow.ToRome();
            var sb = new StringBuilder();
            sb.Append(now.ToString("yyyy-MM-dd HH:mm:ss")).Append(' ').Append(level).Append(' ');
            sb.Append(category.StartsWith("MvChat.Web.") ? category[11..] : category).Append(": ").Append(message.Replace("\r", " ").Replace("\n", " "));
            if (ex is not null) sb.Append(" | ").Append(ex.GetType().Name).Append(": ").Append(ex.Message.Replace("\r", " ").Replace("\n", " "))
                                .Append(" | ").Append((ex.StackTrace ?? "").Replace("\r", "").Replace("\n", " ⏎ "));
            sb.Append(Environment.NewLine);
            lock (_lock)
            {
                File.AppendAllText(Path.Combine(_dir, $"mvchat-{now:yyyyMMdd}.log"), sb.ToString(), Encoding.UTF8);
                if (_lastCleanup.Date != now.Date) { _lastCleanup = now; Cleanup(); }
            }
        }
        catch { /* il registro non deve mai fermare il programma */ }
    }

    private void Cleanup()
    {
        foreach (var f in Directory.GetFiles(_dir, "mvchat-*.log"))
            if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-14)) File.Delete(f);
    }

    /// <summary>Le ultime righe del registro (per la pagina Impostazioni → Registro tecnico).</summary>
    public static List<string> Tail(string dataDir, int lines)
    {
        try
        {
            var dir = Path.Combine(dataDir, "logs");
            var files = Directory.Exists(dir) ? Directory.GetFiles(dir, "mvchat-*.log").OrderByDescending(f => f).Take(3).ToList() : new();
            var result = new List<string>();
            foreach (var f in files)
            {
                using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs, Encoding.UTF8);
                var all = new List<string>();
                while (sr.ReadLine() is { } l) all.Add(l);
                result.InsertRange(0, all);
                if (result.Count >= lines) break;
            }
            return result.Skip(Math.Max(0, result.Count - lines)).ToList();
        }
        catch (Exception ex) { return new() { "Registro non leggibile: " + ex.Message }; }
    }

    public void Dispose() { }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLogProvider _p; private readonly string _category;
        public FileLogger(FileLogProvider p, string category) { _p = p; _category = category; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        // Del programma: informazioni, avvisi ed errori. Del sistema (Microsoft…): solo avvisi ed errori, più avvii e chiusure.
        public bool IsEnabled(LogLevel level) =>
            level != LogLevel.None && (
                level >= LogLevel.Warning
                || (level >= LogLevel.Information && (_category.StartsWith("MvChat") || _category == "Program" || _category == "Microsoft.Hosting.Lifetime")));

        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            var tag = level switch { LogLevel.Critical => "GRAVE ", LogLevel.Error => "ERRORE", LogLevel.Warning => "AVVISO", _ => "info  " };
            _p.Write(tag, _category, formatter(state, exception), exception);
        }
    }
}
