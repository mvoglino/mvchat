using MvChat.Web.Infrastructure;

namespace MvChat.Web.WhatsApp;

public sealed record MediaMsg(long Id, int WaNumberId, long? ConversationId, string Kind, string? MediaId, string? MediaMime, string? MediaFile, bool Transcribed);

/// <summary>
/// Vocali e foto mandati dai clienti: si scaricano da Meta e si tengono in App_Data/media, così la reception
/// li ascolta e li vede dentro mvchat (il numero collegato a Meta non sta su un telefono).
/// Seguono le stesse regole dei messaggi: si cancellano con la pulizia automatica e con le richieste privacy.
/// </summary>
public sealed class MediaStore
{
    public const long MaxBytes = 16 * 1024 * 1024;
    /// <summary>Tipi di messaggio di cui si scarica il file.</summary>
    public static readonly string[] Kinds = { "audio", "voice", "image", "sticker" };
    public static bool IsVoice(string kind) => kind is "audio" or "voice";

    private readonly Db _db; private readonly AppConfigStore _config; private readonly WaRepo _repo; private readonly WaService _wa;
    private readonly ILogger<MediaStore> _log;
    public MediaStore(Db db, AppConfigStore config, WaRepo repo, WaService wa, ILogger<MediaStore> log)
    { _db = db; _config = config; _repo = repo; _wa = wa; _log = log; }

    private string Dir => Path.Combine(_config.DataDir, "media");

    public Task<MediaMsg?> GetAsync(long id) => _db.FirstAsync(
        "SELECT Id, WaNumberId, ConversationId, Kind, MediaId, MediaMime, MediaFile, Transcribed FROM WaMessages WHERE Id=@id", new { id },
        r => new MediaMsg(r.GetInt64(0), r.GetInt32(1), r.IsDBNull(2) ? null : r.GetInt64(2), r.GetString(3), r.Str("MediaId"), r.Str("MediaMime"), r.Str("MediaFile"), r.Bool("Transcribed")));

    public Task SetMediaAsync(long messageId, string? mediaId, string? mime) => _db.ExecuteAsync(
        "UPDATE WaMessages SET MediaId=@mediaId, MediaMime=@mime WHERE Id=@messageId",
        new { messageId, mediaId = WaRepo.Clip(mediaId, 128), mime = WaRepo.Clip(mime, 100) });

    /// <summary>Il file sul disco: se non c'è ancora lo scarica da Meta.</summary>
    public async Task<(string? Path, string? Mime, string? Error)> EnsureAsync(long id)
    {
        var m = await GetAsync(id);
        if (m?.MediaId is null) return (null, null, "nessun file collegato al messaggio");
        if (m.MediaFile == "-") return (null, null, "file non più disponibile su Meta");
        if (m.MediaFile is { Length: > 1 } f && File.Exists(Path.Combine(Dir, f))) return (Path.Combine(Dir, f), m.MediaMime, null);

        var n = await _repo.NumberAsync(m.WaNumberId);
        if (n is null) return (null, null, "numero WhatsApp non più collegato");
        var (bytes, mime, error, gone) = await _wa.DownloadMediaAsync(n, m.MediaId, MaxBytes);
        if (bytes is null)
        {
            if (gone) await _db.ExecuteAsync("UPDATE WaMessages SET MediaFile='-' WHERE Id=@id", new { id });
            return (null, null, error);
        }
        mime ??= m.MediaMime;
        var name = id + Ext(mime);
        Directory.CreateDirectory(Dir);
        var path = Path.Combine(Dir, name);
        var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        await File.WriteAllBytesAsync(tmp, bytes);
        try { File.Move(tmp, path, overwrite: true); }
        catch (IOException) { File.Delete(tmp); } // scaricato nello stesso momento da un'altra richiesta: va bene la sua copia
        await _db.ExecuteAsync("UPDATE WaMessages SET MediaFile=@name, MediaMime=@mime WHERE Id=@id", new { id, name, mime = WaRepo.Clip(mime, 100) });
        return (path, mime, null);
    }

    private static string Ext(string? mime) => (mime ?? "").Split(';')[0].Trim().ToLowerInvariant() switch
    {
        "audio/ogg" or "audio/opus" => ".ogg", "audio/mpeg" => ".mp3", "audio/mp4" or "audio/m4a" => ".m4a", "audio/aac" => ".aac", "audio/amr" => ".amr",
        "image/jpeg" => ".jpg", "image/png" => ".png", "image/webp" => ".webp", _ => ".bin"
    };

    /// <summary>Scarica subito i file degli ultimi due giorni (Meta li tiene per un tempo limitato). Dall'operazione pianificata.</summary>
    public async Task<int> PrefetchAsync(int max)
    {
        var ids = await _db.QueryAsync(
            $@"SELECT Id FROM WaMessages WHERE Direction='in' AND MediaId IS NOT NULL AND MediaFile IS NULL
                 AND Kind IN ('{string.Join("','", Kinds)}') AND CreatedAt > UTC_TIMESTAMP() - INTERVAL 2 DAY ORDER BY Id LIMIT @max",
            new { max }, r => r.GetInt64(0));
        var n = 0;
        foreach (var id in ids)
        {
            try { if ((await EnsureAsync(id)).Path is not null) n++; }
            catch (Exception ex) { _log.LogWarning(ex, "File del messaggio {Id} non scaricato", id); }
        }
        return n;
    }

    /// <summary>Cancella i file indicati (richiesta privacy di un cliente).</summary>
    public void DeleteFiles(IEnumerable<string?> files)
    {
        foreach (var f in files)
            if (f is { Length: > 1 } && f == Path.GetFileName(f))
                try { File.Delete(Path.Combine(Dir, f)); } catch (IOException) { }
    }

    /// <summary>Cancella i file dei messaggi che non ci sono più (pulizia automatica dei dati vecchi).</summary>
    public async Task<int> CleanupOrphansAsync()
    {
        if (!Directory.Exists(Dir)) return 0;
        var files = Directory.GetFiles(Dir)
            .Select(f => (Path: f, Id: !f.EndsWith(".tmp") && long.TryParse(Path.GetFileName(f).Split('.')[0], out var id) ? id : (long?)null)).ToList();
        var deleted = 0;
        // Copie a metà rimaste da uno scaricamento interrotto.
        foreach (var f in files.Where(x => x.Path.EndsWith(".tmp") && File.GetLastWriteTimeUtc(x.Path) < DateTime.UtcNow.AddHours(-1)))
        { try { File.Delete(f.Path); deleted++; } catch (IOException) { } }
        foreach (var chunk in files.Where(x => x.Id is not null).Chunk(500))
        {
            // Gli Id vengono dai nomi dei file già convertiti in numeri: nessun testo libero nella query.
            var existing = (await _db.QueryAsync($"SELECT Id FROM WaMessages WHERE Id IN ({string.Join(",", chunk.Select(x => x.Id))})", null, r => r.GetInt64(0))).ToHashSet();
            foreach (var f in chunk.Where(x => !existing.Contains(x.Id!.Value)))
            { try { File.Delete(f.Path); deleted++; } catch (IOException) { } }
        }
        return deleted;
    }
}
