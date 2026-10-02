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
    private readonly Db _db; private readonly AppConfigStore _config;
    public RetentionService(Db db, AppConfigStore config) { _db = db; _config = config; }

    public Task<DateTime?> LastRunAsync() => _db.ScalarAsync<DateTime?>("SELECT MAX(At) FROM AuditLog WHERE Action='privacy.cleanup'");

    private async Task<int> InBlocksAsync(string sql, object args)
    {
        var total = 0;
        for (var i = 0; i < 200; i++)
        {
            var n = await _db.ExecuteAsync(sql + " LIMIT 1000", args);
            total += n;
            if (n < 1000) break;
        }
        return total;
    }

    public async Task<CleanupResult> RunAsync(bool force = false)
    {
        if (!force && await LastRunAsync() is DateTime last && last > DateTime.UtcNow.AddHours(-20))
            return new(0, 0, 0, 0, 0, 0, true);
        var p = _config.Current.Privacy;
        var cutoff = DateTime.UtcNow.AddMonths(-Math.Clamp(p.RetentionMonths, 1, 120));
        var hooks = DateTime.UtcNow.AddDays(-Math.Clamp(p.WebhookDays, 1, 365));

        // Conversazioni ferme da più del periodo: prima si staccano i collegamenti, poi si cancellano messaggi e conversazioni.
        var old = "SELECT Id FROM Conversations WHERE COALESCE(LastMessageAt, CreatedAt) < @cutoff";
        await _db.ExecuteAsync($"UPDATE AiUsage SET ConversationId=NULL WHERE ConversationId IN (SELECT Id FROM ({old}) x)", new { cutoff });
        await _db.ExecuteAsync($"UPDATE CampaignRecipients SET ConversationId=NULL WHERE ConversationId IN (SELECT Id FROM ({old}) x)", new { cutoff });
        var msgs = await InBlocksAsync($"DELETE FROM WaMessages WHERE ConversationId IN (SELECT Id FROM ({old}) x)", new { cutoff });
        var convs = await InBlocksAsync("DELETE FROM Conversations WHERE COALESCE(LastMessageAt, CreatedAt) < @cutoff", new { cutoff });
        msgs += await InBlocksAsync("DELETE FROM WaMessages WHERE ConversationId IS NULL AND CreatedAt < @cutoff", new { cutoff });
        var recipients = await InBlocksAsync("DELETE FROM CampaignRecipients WHERE CampaignId IN (SELECT Id FROM (SELECT Id FROM Campaigns WHERE CreatedAt < @cutoff) x)", new { cutoff });
        var lists = await _db.ExecuteAsync("DELETE FROM ContactLists WHERE CreatedAt < @cutoff", new { cutoff }); // contatti e scarti vanno via con la lista
        var events = await InBlocksAsync("DELETE FROM WaWebhookEvents WHERE ReceivedAt < @hooks", new { hooks });
        var audit = await InBlocksAsync("DELETE FROM AuditLog WHERE At < @cutoff", new { cutoff });

        var r = new CleanupResult(convs, msgs, lists, recipients, events, audit, false);
        await _db.ExecuteAsync("INSERT INTO AuditLog (Action, Detail) VALUES ('privacy.cleanup', @d)",
            new { d = $"conversazioni {convs}, messaggi {msgs}, liste {lists}, destinatari {recipients}, avvisi Meta {events}, registro {audit}" });
        return r;
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
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}
