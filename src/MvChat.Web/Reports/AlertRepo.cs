using MvChat.Web.Infrastructure;
using MvChat.Web.Security;

namespace MvChat.Web.Reports;

/// <summary>Una cosa che qualcuno dovrebbe guardare: "bad" urgente, "warn" da controllare.</summary>
public sealed record Alert(string Level, string Text, string? Link);

public sealed record Pulse(int Activities, int Users, int CampaignsRunning, int OpenConversations, int Reached30, decimal AiMonthUsd);

/// <summary>Il pannello di controllo: numeri del momento e problemi da sistemare, ognuno nel suo perimetro.</summary>
public sealed class AlertRepo
{
    private readonly Db _db; private readonly AppConfigStore _config;
    public AlertRepo(Db db, AppConfigStore config) { _db = db; _config = config; }

    private const string G = "(@All=1 OR (@IsOrg=1 AND g.OrganizationId=@Org) OR g.Id=@Gym)";
    private static object A(Scope s) => new { All = s.IsSuperAdmin ? 1 : 0, IsOrg = s.IsOrgAdmin ? 1 : 0, Org = s.OrganizationId ?? -1, Gym = s.GymId ?? -1 };

    public async Task<Pulse> PulseAsync(Scope s)
    {
        var monthStart = new DateTime(DateTime.UtcNow.ToRome().Year, DateTime.UtcNow.ToRome().Month, 1).FromRome();
        return (await _db.QueryAsync(
            $@"SELECT
                 (SELECT COUNT(*) FROM Gyms g WHERE {G} AND g.IsActive=1),
                 (SELECT COUNT(*) FROM Users u LEFT JOIN Gyms g ON g.Id=u.GymId WHERE u.IsActive=1 AND (@All=1 OR (@IsOrg=1 AND u.OrganizationId=@Org) OR u.GymId=@Gym)),
                 (SELECT COUNT(*) FROM Campaigns k JOIN Gyms g ON g.Id=k.GymId WHERE {G} AND k.Status IN ('in_corso','programmata')),
                 (SELECT COUNT(*) FROM Conversations v JOIN Gyms g ON g.Id=v.GymId WHERE {G} AND v.IsTest=0 AND v.Status<>'chiusa'),
                 (SELECT COUNT(*) FROM Conversations v JOIN Gyms g ON g.Id=v.GymId WHERE {G} AND v.IsTest=0 AND v.Outcome='obiettivo_raggiunto'
                    AND v.LastMessageAt > UTC_TIMESTAMP() - INTERVAL 30 DAY),
                 (SELECT COALESCE(SUM(a.CostUsd),0) FROM AiUsage a LEFT JOIN Gyms g ON g.Id=a.GymId WHERE (@All=1 OR a.GymId IS NOT NULL AND {G}) AND a.CreatedAt >= @monthStart)",
            new { All = s.IsSuperAdmin ? 1 : 0, IsOrg = s.IsOrgAdmin ? 1 : 0, Org = s.OrganizationId ?? -1, Gym = s.GymId ?? -1, monthStart },
            r => new Pulse(Convert.ToInt32(r.GetValue(0)), Convert.ToInt32(r.GetValue(1)), Convert.ToInt32(r.GetValue(2)), Convert.ToInt32(r.GetValue(3)),
                Convert.ToInt32(r.GetValue(4)), Convert.ToDecimal(r.GetValue(5))))).First();
    }

    public async Task<List<Alert>> AlertsAsync(Scope s)
    {
        var list = new List<Alert>();
        var manage = s.CanManageUsers;

        // Chi aspetta una persona da più di 2 ore: lo vedono tutti, operatori compresi.
        foreach (var (gym, name, n) in await _db.QueryAsync(
            $@"SELECT g.Id, g.Name, COUNT(*) FROM Conversations v JOIN Gyms g ON g.Id=v.GymId
               WHERE {G} AND v.Status='operatore' AND v.LastInboundAt >= v.LastMessageAt AND v.LastInboundAt < UTC_TIMESTAMP() - INTERVAL 2 HOUR
               GROUP BY g.Id, g.Name", A(s), r => (r.GetInt32(0), r.GetString(1), Convert.ToInt32(r.GetValue(2)))))
            list.Add(new("bad", $"{name}: {n} {(n == 1 ? "cliente aspetta" : "clienti aspettano")} una risposta da più di 2 ore", $"/Conversazioni?view=da_gestire&gym={gym}"));
        if (!manage) return list;

        foreach (var (gym, name, status, quality, error) in await _db.QueryAsync(
            $@"SELECT g.Id, g.Name, n.Status, n.QualityRating, n.LastError FROM WaNumbers n JOIN Gyms g ON g.Id=n.GymId
               WHERE {G} AND g.IsActive=1 AND n.IsSimulated=0 AND (n.Status IN ('errore','incompleto','da controllare') OR n.QualityRating IN ('YELLOW','RED'))",
            A(s), r => (r.GetInt32(0), r.GetString(1), r.GetString(2), r.Str("QualityRating"), r.Str("LastError"))))
            list.Add(quality is "RED" or "YELLOW"
                ? new(quality == "RED" ? "bad" : "warn", $"{name}: Meta segnala qualità {(quality == "RED" ? "bassa (rossa)" : "media (gialla)")} del numero WhatsApp: troppi clienti bloccano o segnalano i messaggi", $"/WhatsApp/Numero/{gym}")
                : new("bad", $"{name}: numero WhatsApp «{status}»{(error is null ? "" : " · " + error)}", $"/WhatsApp/Numero/{gym}"));

        foreach (var (id, name, gymName, reason) in await _db.QueryAsync(
            $@"SELECT k.Id, k.Name, g.Name, k.PauseReason FROM Campaigns k JOIN Gyms g ON g.Id=k.GymId WHERE {G} AND k.Status='in_pausa'",
            A(s), r => (r.GetInt32(0), r.GetString(1), r.GetString(2), r.Str("PauseReason"))))
            list.Add(new("warn", $"{gymName}: campagna «{name}» in pausa{(reason is null ? "" : " · " + reason)}", $"/Campagne/{id}"));

        foreach (var (gym, gymName, name, reason) in await _db.QueryAsync(
            $@"SELECT g.Id, g.Name, t.Name, t.RejectReason FROM WaTemplates t JOIN Gyms g ON g.Id=t.GymId
               WHERE {G} AND t.Status='rifiutato' AND t.UpdatedAt > UTC_TIMESTAMP() - INTERVAL 30 DAY",
            A(s), r => (r.GetInt32(0), r.GetString(1), r.GetString(2), r.Str("RejectReason"))))
            list.Add(new("warn", $"{gymName}: Meta ha rifiutato il template «{name}»{(reason is null ? "" : " · " + reason)}", $"/WhatsApp/Template/{gym}"));

        if (s.IsSuperAdmin)
        {
            var c = _config.Current;
            if (c.Ai.Provider == "") list.Add(new("warn", "Assistente AI spento: le risposte dei clienti passano tutte agli operatori", "/Impostazioni/AI"));
            if (string.IsNullOrEmpty(c.Meta.AppSecret)) list.Add(new("warn", "Impostazioni Meta incomplete: senza chiave segreta dell'app i messaggi dei clienti vengono rifiutati", "/Impostazioni/WhatsApp"));
            var aiErrors = await _db.ScalarAsync<int>("SELECT COUNT(*) FROM AiUsage WHERE Ok=0 AND CreatedAt > UTC_TIMESTAMP() - INTERVAL 24 HOUR");
            if (aiErrors > 0) list.Add(new("bad", $"Il fornitore AI non ha risposto {aiErrors} {(aiErrors == 1 ? "volta" : "volte")} nelle ultime 24 ore", "/Impostazioni/AI"));
            var hookErrors = await _db.ScalarAsync<int>("SELECT COUNT(*) FROM WaWebhookEvents WHERE Error IS NOT NULL AND ReceivedAt > UTC_TIMESTAMP() - INTERVAL 24 HOUR");
            if (hookErrors > 0) list.Add(new("bad", $"{hookErrors} avvisi di Meta non elaborati nelle ultime 24 ore", null));
            var lastTick = await _db.ScalarAsync<DateTime?>("SELECT MAX(At) FROM AuditLog WHERE Action='jobs.tick'");
            if (lastTick is null || lastTick < DateTime.UtcNow.AddHours(-1))
                list.Add(new("warn", lastTick is null ? "L'operazione pianificata (/jobs/tick) non è ancora mai partita" : $"L'operazione pianificata non parte dal {lastTick.Value.ToRome():dd/MM HH:mm}: controllala nel pannello Aruba", null));
        }
        return list.OrderBy(a => a.Level == "bad" ? 0 : 1).ToList();
    }
}
