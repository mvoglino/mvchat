using System.Data.Common;
using MvChat.Web.Infrastructure;
using MvChat.Web.Security;

namespace MvChat.Web.Campaigns;

public sealed class Campaign
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int GymId { get; set; }
    public string GymName { get; set; } = "";
    public string Name { get; set; } = "";
    public int? ListId { get; set; }
    public string ListName { get; set; } = "";
    public int GoalModelId { get; set; }
    public string GoalName { get; set; } = "";
    public int? OfferId { get; set; }
    public string? OfferTitle { get; set; }
    public int WaNumberId { get; set; }
    public int TemplateId { get; set; }
    public string TemplateName { get; set; } = "";
    public string Status { get; set; } = "bozza";
    public string? PauseReason { get; set; }
    public DateTime? StartAt { get; set; }
    public int? DailyLimit { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? LastRunAt { get; set; }
    public string? LastRunNote { get; set; }
    // Conteggi
    public int Total { get; set; }
    public int Waiting { get; set; }
    public int Sent { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public int Replied { get; set; }
    public int Reached { get; set; }
    public int Refused { get; set; }
    public int OptOut { get; set; }
    public int ToOperator { get; set; }

    public bool IsEditable => Status == "bozza";
    public bool CanStart => Status == "bozza";
    public bool CanPause => Status is "in_corso" or "programmata";
    public bool CanResume => Status == "in_pausa";
    public bool CanCancel => Status is "bozza" or "programmata" or "in_corso" or "in_pausa";
    public int Done => Sent + Skipped + Errors;
    public int Percent => Total == 0 ? 0 : (int)Math.Round(100.0 * Done / Total);

    public static string StatusLabel(string s) => s switch
    {
        "bozza" => "Bozza", "programmata" => "Programmata", "in_corso" => "In invio", "in_pausa" => "In pausa",
        "completata" => "Completata", "annullata" => "Annullata", _ => s
    };
    public static string StatusCss(string s) => s switch { "in_corso" => "good", "in_pausa" => "warn", "annullata" => "bad", _ => "" };
}

public sealed record CampaignRecipient(long Id, string Phone, string FirstName, string? LastName, string? Membership, DateTime? ExpiresOn,
    string Status, string? Reason, long? ConversationId, DateTime? SentAt, string? Outcome);

public sealed class CampaignRepo
{
    private readonly Db _db;
    public CampaignRepo(Db db) => _db = db;

    private const string Select = @"
        SELECT c.*, g.Name AS GymName, m.Name AS GoalName, o.Title AS OfferTitle, t.Name AS TemplateName,
          (SELECT COUNT(*) FROM CampaignRecipients r WHERE r.CampaignId=c.Id) AS Total,
          (SELECT COUNT(*) FROM CampaignRecipients r WHERE r.CampaignId=c.Id AND r.Status IN ('in_attesa','in_invio')) AS Waiting,
          (SELECT COUNT(*) FROM CampaignRecipients r WHERE r.CampaignId=c.Id AND r.Status='inviato') AS Sent,
          (SELECT COUNT(*) FROM CampaignRecipients r WHERE r.CampaignId=c.Id AND r.Status='saltato') AS Skipped,
          (SELECT COUNT(*) FROM CampaignRecipients r WHERE r.CampaignId=c.Id AND r.Status='errore') AS Errors,
          (SELECT COUNT(*) FROM Conversations v WHERE v.CampaignId=c.Id AND v.LastInboundAt IS NOT NULL) AS Replied,
          (SELECT COUNT(*) FROM Conversations v WHERE v.CampaignId=c.Id AND v.Outcome='obiettivo_raggiunto') AS Reached,
          (SELECT COUNT(*) FROM Conversations v WHERE v.CampaignId=c.Id AND v.Outcome='rifiuto') AS Refused,
          (SELECT COUNT(*) FROM Conversations v WHERE v.CampaignId=c.Id AND v.Outcome='opt_out') AS OptOut,
          (SELECT COUNT(*) FROM Conversations v WHERE v.CampaignId=c.Id AND v.Status='operatore') AS ToOperator
        FROM Campaigns c JOIN Gyms g ON g.Id=c.GymId JOIN GoalModels m ON m.Id=c.GoalModelId
        LEFT JOIN Offers o ON o.Id=c.OfferId LEFT JOIN WaTemplates t ON t.Id=c.TemplateId";

    private static int I(DbDataReader r, string n) => Convert.ToInt32(r.GetValue(r.GetOrdinal(n)));

    private static Campaign Map(DbDataReader r) => new()
    {
        Id = r.Int("Id"), OrganizationId = r.Int("OrganizationId"), GymId = r.Int("GymId"), GymName = r.Str("GymName")!, Name = r.Str("Name")!,
        ListId = r.IntN("ListId"), ListName = r.Str("ListName")!, GoalModelId = r.Int("GoalModelId"), GoalName = r.Str("GoalName")!,
        OfferId = r.IntN("OfferId"), OfferTitle = r.Str("OfferTitle"), WaNumberId = r.Int("WaNumberId"), TemplateId = r.Int("TemplateId"),
        TemplateName = r.Str("TemplateName") ?? "(eliminato)", Status = r.Str("Status")!, PauseReason = r.Str("PauseReason"),
        StartAt = r.Date("StartAt"), DailyLimit = r.IntN("DailyLimit"), CreatedAt = r.Date("CreatedAt")!.Value, StartedAt = r.Date("StartedAt"),
        CompletedAt = r.Date("CompletedAt"), LastRunAt = r.Date("LastRunAt"), LastRunNote = r.Str("LastRunNote"),
        Total = I(r, "Total"), Waiting = I(r, "Waiting"), Sent = I(r, "Sent"), Skipped = I(r, "Skipped"), Errors = I(r, "Errors"),
        Replied = I(r, "Replied"), Reached = I(r, "Reached"), Refused = I(r, "Refused"), OptOut = I(r, "OptOut"), ToOperator = I(r, "ToOperator")
    };

    private static object ScopeArgs(Scope s, int? gymId, int? id) => new
    {
        All = s.IsSuperAdmin ? 1 : 0, IsOrg = s.IsOrgAdmin ? 1 : 0, Org = s.OrganizationId ?? -1, Gym = s.GymId ?? -1,
        FGym = gymId ?? -1, FId = id ?? -1
    };

    public Task<List<Campaign>> ListAsync(Scope s, int? gymId, int? id = null) => _db.QueryAsync(
        Select + @" WHERE (@All=1 OR (@IsOrg=1 AND c.OrganizationId=@Org) OR c.GymId=@Gym)
          AND (@FGym=-1 OR c.GymId=@FGym) AND (@FId=-1 OR c.Id=@FId) ORDER BY c.CreatedAt DESC LIMIT 300",
        ScopeArgs(s, gymId, id), Map);

    public async Task<Campaign?> GetAsync(Scope s, int id) => (await ListAsync(s, null, id)).FirstOrDefault();

    /// <summary>Senza perimetro: per il lavoro automatico di invio.</summary>
    public Task<Campaign?> GetAsync(int id) => _db.FirstAsync(Select + " WHERE c.Id=@id", new { id }, Map);

    /// <summary>Crea la campagna e copia i destinatari dalla lista (solo chi non è nella lista STOP della catena).</summary>
    public async Task<(int Id, int Recipients, int Excluded)> CreateAsync(Campaign c, int userId)
    {
        await using var cn = await _db.OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        int id;
        await using (var cmd = Db.Command(cn,
            @"INSERT INTO Campaigns (OrganizationId, GymId, Name, ListId, ListName, GoalModelId, OfferId, WaNumberId, TemplateId, Status, StartAt, DailyLimit, CreatedBy)
              VALUES (@OrganizationId, @GymId, @Name, @ListId, @ListName, @GoalModelId, @OfferId, @WaNumberId, @TemplateId, 'bozza', @StartAt, @DailyLimit, @userId);
              SELECT LAST_INSERT_ID();",
            new { c.OrganizationId, c.GymId, c.Name, c.ListId, c.ListName, c.GoalModelId, c.OfferId, c.WaNumberId, c.TemplateId, c.StartAt, c.DailyLimit, userId }, tx))
            id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        int added, total;
        await using (var cmd = Db.Command(cn,
            @"INSERT INTO CampaignRecipients (CampaignId, ContactId, Phone, FirstName, LastName, Membership, ExpiresOn)
              SELECT @id, k.Id, k.Phone, k.FirstName, k.LastName, k.Membership, k.ExpiresOn FROM Contacts k
              WHERE k.ListId=@ListId AND k.GymId=@GymId
                AND NOT EXISTS (SELECT 1 FROM OptOuts x WHERE x.OrganizationId=@OrganizationId AND x.Phone=k.Phone)",
            new { id, c.ListId, c.GymId, c.OrganizationId }, tx))
            added = await cmd.ExecuteNonQueryAsync();
        await using (var cmd = Db.Command(cn, "SELECT COUNT(*) FROM Contacts WHERE ListId=@ListId AND GymId=@GymId", new { c.ListId, c.GymId }, tx))
            total = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        await tx.CommitAsync();
        return (id, added, total - added);
    }

    public Task<int> UpdateDraftAsync(Campaign c) => _db.ExecuteAsync(
        "UPDATE Campaigns SET Name=@Name, StartAt=@StartAt, DailyLimit=@DailyLimit WHERE Id=@Id AND Status='bozza'",
        new { c.Id, c.Name, c.StartAt, c.DailyLimit });

    public Task<int> SetStatusAsync(int id, string status, string? reason, string[] from) => _db.ExecuteAsync(
        $@"UPDATE Campaigns SET Status=@status, PauseReason=@reason,
             StartedAt=CASE WHEN @status IN ('programmata','in_corso') AND StartedAt IS NULL THEN UTC_TIMESTAMP() ELSE StartedAt END,
             CompletedAt=CASE WHEN @status IN ('completata','annullata') THEN UTC_TIMESTAMP() ELSE CompletedAt END
           WHERE Id=@id AND Status IN ({string.Join(",", from.Select(f => "'" + f + "'"))})",
        new { id, status, reason });

    /// <summary>All'annullamento i destinatari non ancora contattati vengono segnati come saltati.</summary>
    public Task SkipWaitingAsync(int id, string reason) => _db.ExecuteAsync(
        "UPDATE CampaignRecipients SET Status='saltato', Reason=@reason WHERE CampaignId=@id AND Status='in_attesa'", new { id, reason });

    public Task<List<CampaignRecipient>> RecipientsAsync(int campaignId, string? status, int limit) => _db.QueryAsync(
        @"SELECT r.*, v.Outcome FROM CampaignRecipients r LEFT JOIN Conversations v ON v.Id=r.ConversationId
          WHERE r.CampaignId=@campaignId AND (@status='' OR r.Status=@status) ORDER BY r.Id LIMIT @limit",
        new { campaignId, status = status ?? "", limit },
        r => new CampaignRecipient(r.GetInt64(r.GetOrdinal("Id")), r.Str("Phone")!, r.Str("FirstName")!, r.Str("LastName"), r.Str("Membership"),
            r.Date("ExpiresOn"), r.Str("Status")!, r.Str("Reason"), r.IsDBNull(r.GetOrdinal("ConversationId")) ? null : r.GetInt64(r.GetOrdinal("ConversationId")),
            r.Date("SentAt"), r.Str("Outcome")));

    // ---------- Orari di invio ----------
    public Task<List<SendWindow>> WindowsAsync(int gymId) => _db.QueryAsync(
        "SELECT DayOfWeek, StartMinute, EndMinute FROM GymSendWindows WHERE GymId=@gymId ORDER BY DayOfWeek, StartMinute",
        new { gymId }, r => new SendWindow(Convert.ToInt32(r.GetValue(0)), Convert.ToInt32(r.GetValue(1)), Convert.ToInt32(r.GetValue(2))));

    public async Task SaveWindowsAsync(int gymId, IEnumerable<SendWindow> windows)
    {
        await using var cn = await _db.OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        await using (var del = Db.Command(cn, "DELETE FROM GymSendWindows WHERE GymId=@gymId", new { gymId }, tx)) await del.ExecuteNonQueryAsync();
        foreach (var w in windows)
            await using (var ins = Db.Command(cn, "INSERT INTO GymSendWindows (GymId, DayOfWeek, StartMinute, EndMinute) VALUES (@gymId, @Day, @Start, @End)",
                new { gymId, w.Day, w.Start, w.End }, tx)) await ins.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }

    // ---------- Lavoro di invio ----------
    public Task<int> PromoteScheduledAsync() => _db.ExecuteAsync(
        "UPDATE Campaigns SET Status='in_corso' WHERE Status='programmata' AND (StartAt IS NULL OR StartAt <= UTC_TIMESTAMP())");

    public Task<List<int>> RunnableAsync() => _db.QueryAsync(
        "SELECT Id FROM Campaigns WHERE Status='in_corso' ORDER BY Id", null, r => Convert.ToInt32(r.GetValue(0)));

    /// <summary>Un invio rimasto a metà (per esempio per un riavvio): non si ripete da solo, per non mandare due volte lo stesso messaggio.</summary>
    public Task<int> FailStaleClaimsAsync() => _db.ExecuteAsync(
        @"UPDATE CampaignRecipients SET Status='errore', Reason='invio interrotto: controlla nella conversazione se è partito', ClaimToken=NULL
          WHERE Status='in_invio' AND ClaimedAt < UTC_TIMESTAMP() - INTERVAL 10 MINUTE");

    public async Task<List<CampaignRecipient>> ClaimAsync(int campaignId, int count)
    {
        var token = Guid.NewGuid().ToString("N");
        await _db.ExecuteAsync(
            @"UPDATE CampaignRecipients SET Status='in_invio', ClaimToken=@token, ClaimedAt=UTC_TIMESTAMP()
              WHERE CampaignId=@campaignId AND Status='in_attesa' ORDER BY Id LIMIT @count", new { campaignId, count, token });
        return await _db.QueryAsync(
            "SELECT r.*, NULL AS Outcome FROM CampaignRecipients r WHERE ClaimToken=@token AND Status='in_invio' ORDER BY Id", new { token },
            r => new CampaignRecipient(r.GetInt64(r.GetOrdinal("Id")), r.Str("Phone")!, r.Str("FirstName")!, r.Str("LastName"), r.Str("Membership"),
                r.Date("ExpiresOn"), r.Str("Status")!, null, null, null, null));
    }

    public Task MarkAsync(long id, string status, string? reason, long? conversationId) => _db.ExecuteAsync(
        @"UPDATE CampaignRecipients SET Status=@status, Reason=@reason, ConversationId=@conversationId, ClaimToken=NULL,
            SentAt=CASE WHEN @status='inviato' THEN UTC_TIMESTAMP() ELSE SentAt END WHERE Id=@id",
        new { id, status, reason, conversationId });

    public Task ReleaseAsync(IEnumerable<long> ids)
    {
        var list = ids.ToList();
        if (list.Count == 0) return Task.CompletedTask;
        return _db.ExecuteAsync($"UPDATE CampaignRecipients SET Status='in_attesa', ClaimToken=NULL WHERE Status='in_invio' AND Id IN ({string.Join(",", list)})");
    }

    /// <summary>Persone diverse contattate per primi da questo numero nelle ultime 24 ore (il limite di Meta).</summary>
    public Task<int> FirstContactsLast24hAsync(int waNumberId) => _db.ScalarAsync<int>(
        @"SELECT COUNT(DISTINCT ContactPhone) FROM WaMessages WHERE WaNumberId=@waNumberId AND Direction='out' AND Kind='template'
          AND Status<>'failed' AND CreatedAt > UTC_TIMESTAMP() - INTERVAL 24 HOUR", new { waNumberId });

    public Task<int> SentSinceAsync(int campaignId, DateTime sinceUtc) => _db.ScalarAsync<int>(
        "SELECT COUNT(*) FROM CampaignRecipients WHERE CampaignId=@campaignId AND Status='inviato' AND SentAt >= @sinceUtc", new { campaignId, sinceUtc });

    /// <summary>Il cliente sta già parlando con la palestra (altra campagna o prova ancora aperta)?</summary>
    public async Task<bool> HasOpenConversationAsync(int waNumberId, string phone) => await _db.ScalarAsync<int>(
        @"SELECT COUNT(*) FROM Conversations WHERE WaNumberId=@waNumberId AND ContactPhone=@phone AND Status IN ('ai','operatore')
          AND CreatedAt > UTC_TIMESTAMP() - INTERVAL 7 DAY", new { waNumberId, phone }) > 0;

    public Task<int> RemainingAsync(int campaignId) => _db.ScalarAsync<int>(
        "SELECT COUNT(*) FROM CampaignRecipients WHERE CampaignId=@campaignId AND Status IN ('in_attesa','in_invio')", new { campaignId });

    public Task NoteRunAsync(int id, string? note) => _db.ExecuteAsync(
        "UPDATE Campaigns SET LastRunAt=UTC_TIMESTAMP(), LastRunNote=@note WHERE Id=@id", new { id, note });
}
