using MvChat.Web.Infrastructure;

namespace MvChat.Web.Privacy;

public sealed record CleanupResult(int Conversations, int Messages, int Lists, int Recipients, int WebhookEvents, int AuditRows, bool Skipped);

/// <summary>
/// Conservazione limitata dei dati (GDPR): una volta al giorno cancella quello che ha superato il periodo deciso
/// (di base 12 mesi). Si cancella a piccoli blocchi per non tenere occupato il database sull'hosting condiviso.
/// La lista STOP non si cancella mai: serve proprio a non ricontattare chi l'ha chiesto.
/// </summary>
public sealed class RetentionService
{
    private readonly Db _db; private readonly AppConfigStore _config; private readonly MvChat.Web.WhatsApp.MediaStore _media;
    public RetentionService(Db db, AppConfigStore config, MvChat.Web.WhatsApp.MediaStore media) { _db = db; _config = config; _media = media; }

    public Task<DateTime?> LastRunAsync() => _db.ScalarAsync<DateTime?>("SELECT MAX(At) FROM AuditLog WHERE Action='privacy.cleanup'");

    // Un solo giro di pulizia alla volta (operazione pianificata e lavoro automatico possono partire insieme).
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private DateTime _deadline = DateTime.MaxValue;
    private bool _unfinished;

    private async Task<int> InBlocksAsync(string sql, object args)
    {
        var total = 0;
        for (var i = 0; i < 200; i++)
        {
            if (DateTime.UtcNow >= _deadline) { _unfinished = true; break; } // il resto al prossimo giro
            var n = await _db.ExecuteAsync(sql + " LIMIT 1000", args);
            total += n;
            if (n < 1000) break;
        }
        return total;
    }

    /// <param name="budget">Tempo massimo: dall'operazione pianificata si resta nei limiti di Aruba; se non basta, si finisce al giro dopo.</param>
    public async Task<CleanupResult> RunAsync(bool force = false, TimeSpan? budget = null)
    {
        if (!await Gate.WaitAsync(TimeSpan.Zero)) return new(0, 0, 0, 0, 0, 0, true);
        try
        {
            if (!force && await LastRunAsync() is DateTime last && last > DateTime.UtcNow.AddHours(-20))
                return new(0, 0, 0, 0, 0, 0, true);
            _deadline = DateTime.UtcNow + (budget ?? TimeSpan.FromMinutes(10));
            _unfinished = false;
            var p = _config.Current.Privacy;
            var cutoff = DateTime.UtcNow.AddMonths(-Math.Clamp(p.RetentionMonths, 1, 120));
            var hooks = DateTime.UtcNow.AddDays(-Math.Clamp(p.WebhookDays, 1, 365));

            // Conversazioni ferme da più del periodo: prima si staccano i collegamenti, poi si cancellano messaggi e conversazioni.
            // (Scritto senza COALESCE così il database può usare gli indici sulle date.)
            const string oldWhere = "(LastMessageAt < @cutoff OR (LastMessageAt IS NULL AND CreatedAt < @cutoff))";
            var old = $"SELECT Id FROM Conversations WHERE {oldWhere}";
            await _db.ExecuteAsync($"UPDATE AiUsage SET ConversationId=NULL WHERE ConversationId IN (SELECT Id FROM ({old}) x)", new { cutoff });
            await _db.ExecuteAsync($"UPDATE CampaignRecipients SET ConversationId=NULL WHERE ConversationId IN (SELECT Id FROM ({old}) x)", new { cutoff });
            var msgs = await InBlocksAsync($"DELETE FROM WaMessages WHERE ConversationId IN (SELECT Id FROM ({old}) x)", new { cutoff });
            // Le conversazioni si cancellano solo dopo i loro messaggi: se il tempo finisce prima, restano per il giro successivo.
            var convs = _unfinished ? 0 : await InBlocksAsync(
                $"DELETE FROM Conversations WHERE {oldWhere} AND NOT EXISTS (SELECT 1 FROM WaMessages w WHERE w.ConversationId=Conversations.Id)", new { cutoff });
            // Messaggi vecchi senza conversazione (o con una conversazione che non esiste più).
            msgs += await InBlocksAsync(
                "DELETE FROM WaMessages WHERE CreatedAt < @cutoff AND (ConversationId IS NULL OR NOT EXISTS (SELECT 1 FROM Conversations c WHERE c.Id=WaMessages.ConversationId))", new { cutoff });
            var recipients = await InBlocksAsync("DELETE FROM CampaignRecipients WHERE CampaignId IN (SELECT Id FROM (SELECT Id FROM Campaigns WHERE CreatedAt < @cutoff) x)", new { cutoff });
            var lists = _unfinished ? 0 : await _db.ExecuteAsync("DELETE FROM ContactLists WHERE CreatedAt < @cutoff", new { cutoff }); // contatti e scarti vanno via con la lista
            // Etichette di clienti senza più conversazioni, messe prima del periodo di conservazione.
            await InBlocksAsync(@"DELETE FROM ContactTags WHERE CreatedAt < @cutoff
                AND NOT EXISTS (SELECT 1 FROM Conversations c WHERE c.GymId=ContactTags.GymId AND c.ContactPhone=ContactTags.Phone)", new { cutoff });
            var events = await InBlocksAsync("DELETE FROM WaWebhookEvents WHERE ReceivedAt < @hooks", new { hooks });
            // Il registro si accorcia, ma restano le righe di fatturazione e privacy: servono come prova di cosa è stato fatto.
            var audit = await InBlocksAsync("DELETE FROM AuditLog WHERE At < @cutoff AND Action NOT LIKE 'billing.%' AND Action NOT LIKE 'privacy.%'", new { cutoff });

            // Vocali e foto dei messaggi cancellati.
            if (!_unfinished) await _media.CleanupOrphansAsync();

            var r = new CleanupResult(convs, msgs, lists, recipients, events, audit, false);
            // Se il tempo non è bastato, non si segna come fatta: riparte al prossimo giro e finisce il lavoro.
            if (!_unfinished)
                await _db.ExecuteAsync("INSERT INTO AuditLog (Action, Detail) VALUES ('privacy.cleanup', @d)",
                    new { d = $"conversazioni {convs}, messaggi {msgs}, liste {lists}, destinatari {recipients}, avvisi Meta {events}, registro {audit}" });
            return r;
        }
        finally { Gate.Release(); }
    }
}

/// <summary>Una volta all'ora controlla se è il momento della pulizia giornaliera (parte anche da /jobs/tick).</summary>
public sealed class RetentionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes; private readonly AppConfigStore _config; private readonly ILogger<RetentionWorker> _log;
    public RetentionWorker(IServiceScopeFactory scopes, AppConfigStore config, ILogger<RetentionWorker> log) { _scopes = scopes; _config = config; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_config.Current.Installed)
            {
                try { using var s = _scopes.CreateScope(); await s.ServiceProvider.GetRequiredService<RetentionService>().RunAsync(); }
                catch (Exception ex) { _log.LogError(ex, "Pulizia dei dati non riuscita"); }
            }
            // Una riga all'ora nel registro tecnico: se la memoria cresce, si vede prima che l'hosting chiuda il sito.
            using (var p = System.Diagnostics.Process.GetCurrentProcess())
                _log.LogInformation("Memoria in uso: {Mb} MB (gestita {Managed} MB)", p.PrivateMemorySize64 / 1048576, GC.GetTotalMemory(false) / 1048576);
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}
