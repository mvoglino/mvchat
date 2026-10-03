using System.Data.Common;
using System.Text.Json;
using MvChat.Web.Infrastructure;
using MvChat.Web.Security;

namespace MvChat.Web.WhatsApp;

public sealed class WaNumber
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int GymId { get; set; }
    public string GymName { get; set; } = "";
    public string DisplayPhone { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? PhoneNumberId { get; set; }
    public string? WabaId { get; set; }
    public string? AccessTokenEnc { get; set; }
    public bool IsSimulated { get; set; } = true;
    public string Status { get; set; } = "attivo";
    public string? QualityRating { get; set; }
    public string? MessagingLimit { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastCheckAt { get; set; }
    /// <summary>Quando Meta ha confermato per la prima volta numero e chiave: da lì in poi se ne accettano i messaggi.</summary>
    public DateTime? VerifiedAt { get; set; }
    public bool HasToken => !string.IsNullOrEmpty(AccessTokenEnc);
}

public sealed class WaTemplate
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int GymId { get; set; }
    public int WaNumberId { get; set; }
    public int? GoalModelId { get; set; }
    public string Name { get; set; } = "";
    public string Language { get; set; } = "it";
    public string Category { get; set; } = "MARKETING";
    public string Body { get; set; } = "";
    public List<string> Variables { get; set; } = new();
    public string Status { get; set; } = "bozza";
    public string? MetaTemplateId { get; set; }
    public string? RejectReason { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsApproved => Status == "approvato";
}

public sealed record SentMsg(long Id, int OrganizationId, int GymId, string Phone, long? ConversationId, string Kind);

public sealed record WaMessage(long Id, string ContactPhone, string Direction, string Kind, string? Body, string? TemplateName, string Status, string? Error, DateTime CreatedAt);

public sealed class WaRepo
{
    private readonly Db _db;
    public WaRepo(Db db) => _db = db;

    // ---------- Numeri ----------
    private const string NumberSelect = "SELECT n.*, g.Name AS GymName FROM WaNumbers n JOIN Gyms g ON g.Id=n.GymId";

    public Task<List<WaNumber>> NumbersAsync(Scope s) => _db.QueryAsync(
        NumberSelect + " WHERE (@All=1 OR (@IsOrg=1 AND n.OrganizationId=@Org) OR n.GymId=@Gym) ORDER BY g.Name",
        new { All = s.IsSuperAdmin ? 1 : 0, IsOrg = s.IsOrgAdmin ? 1 : 0, Org = s.OrganizationId ?? -1, Gym = s.GymId ?? -1 }, MapNumber);

    /// <summary>Numero di un'attività, senza filtro di perimetro: chi chiama deve aver già controllato l'attività.</summary>
    public Task<WaNumber?> NumberForGymAsync(int gymId) => _db.FirstAsync(NumberSelect + " WHERE n.GymId=@gymId", new { gymId }, MapNumber);
    public Task<WaNumber?> NumberAsync(int id) => _db.FirstAsync(NumberSelect + " WHERE n.Id=@id", new { id }, MapNumber);
    public Task<WaNumber?> NumberByPhoneNumberIdAsync(string phoneNumberId) =>
        _db.FirstAsync(NumberSelect + " WHERE n.PhoneNumberId=@phoneNumberId", new { phoneNumberId }, MapNumber);

    /// <summary>Numeri collegati a Meta (non simulati), per aggiornare qualità e limiti.</summary>
    public Task<List<WaNumber>> MetaNumbersAsync(int? notCheckedForHours = null) => _db.QueryAsync(
        NumberSelect + " WHERE n.IsSimulated=0 AND n.PhoneNumberId IS NOT NULL AND (@h IS NULL OR n.LastCheckAt IS NULL OR n.LastCheckAt < UTC_TIMESTAMP() - INTERVAL @h HOUR) ORDER BY n.LastCheckAt",
        new { h = notCheckedForHours }, MapNumber);

    private static WaNumber MapNumber(DbDataReader r) => new()
    {
        Id = r.Int("Id"), OrganizationId = r.Int("OrganizationId"), GymId = r.Int("GymId"), GymName = r.Str("GymName")!,
        DisplayPhone = r.Str("DisplayPhone")!, DisplayName = r.Str("DisplayName"), PhoneNumberId = r.Str("PhoneNumberId"), WabaId = r.Str("WabaId"),
        AccessTokenEnc = r.Str("AccessTokenEnc"), IsSimulated = r.Bool("IsSimulated"), Status = r.Str("Status")!, QualityRating = r.Str("QualityRating"),
        MessagingLimit = r.Str("MessagingLimit"), LastError = r.Str("LastError"), LastCheckAt = r.Date("LastCheckAt"), VerifiedAt = r.Date("VerifiedAt")
    };

    public async Task<int> SaveNumberAsync(WaNumber n)
    {
        var args = new { n.Id, n.OrganizationId, n.GymId, n.DisplayPhone, n.DisplayName, n.PhoneNumberId, n.WabaId, n.AccessTokenEnc, n.IsSimulated, n.Status };
        if (n.Id == 0)
            return await _db.ScalarAsync<int>(
                @"INSERT INTO WaNumbers (OrganizationId, GymId, DisplayPhone, DisplayName, PhoneNumberId, WabaId, AccessTokenEnc, IsSimulated, Status)
                  VALUES (@OrganizationId, @GymId, @DisplayPhone, @DisplayName, @PhoneNumberId, @WabaId, @AccessTokenEnc, @IsSimulated, @Status); SELECT LAST_INSERT_ID();", args);
        await _db.ExecuteAsync(
            // Se cambia l'identificativo del numero, la verifica di Meta va rifatta (VerifiedAt prima di PhoneNumberId: MySQL assegna in ordine).
            @"UPDATE WaNumbers SET VerifiedAt=CASE WHEN PhoneNumberId <=> @PhoneNumberId AND IsSimulated=@IsSimulated THEN VerifiedAt ELSE NULL END,
              DisplayPhone=@DisplayPhone, DisplayName=@DisplayName, PhoneNumberId=@PhoneNumberId, WabaId=@WabaId,
              AccessTokenEnc=@AccessTokenEnc, IsSimulated=@IsSimulated, Status=@Status, LastError=NULL WHERE Id=@Id AND GymId=@GymId", args);
        return n.Id;
    }

    public Task SetNumberCheckAsync(int id, string status, string? quality, string? limit, string? displayName, string? error) => _db.ExecuteAsync(
        @"UPDATE WaNumbers SET Status=@status, QualityRating=COALESCE(@quality, QualityRating), MessagingLimit=COALESCE(@limit, MessagingLimit),
          DisplayName=COALESCE(@displayName, DisplayName), LastError=@error, LastCheckAt=UTC_TIMESTAMP(),
          VerifiedAt=CASE WHEN @status='attivo' THEN COALESCE(VerifiedAt, UTC_TIMESTAMP()) ELSE VerifiedAt END WHERE Id=@id",
        new { id, status, quality, limit, displayName, error });

    // ---------- Template ----------
    private const string TemplateSelect = "SELECT * FROM WaTemplates";

    public Task<List<WaTemplate>> TemplatesAsync(int gymId) => _db.QueryAsync(
        TemplateSelect + " WHERE GymId=@gymId ORDER BY UpdatedAt DESC", new { gymId }, MapTemplate);

    public Task<WaTemplate?> TemplateAsync(int id) => _db.FirstAsync(TemplateSelect + " WHERE Id=@id", new { id }, MapTemplate);

    private static WaTemplate MapTemplate(DbDataReader r) => new()
    {
        Id = r.Int("Id"), OrganizationId = r.Int("OrganizationId"), GymId = r.Int("GymId"), WaNumberId = r.Int("WaNumberId"),
        GoalModelId = r.IntN("GoalModelId"), Name = r.Str("Name")!, Language = r.Str("Language")!, Category = r.Str("Category")!,
        Body = r.Str("Body")!, Variables = JsonSerializer.Deserialize<List<string>>(r.Str("Variables") ?? "[]") ?? new(),
        Status = r.Str("Status")!, MetaTemplateId = r.Str("MetaTemplateId"), RejectReason = r.Str("RejectReason"), UpdatedAt = r.Date("UpdatedAt")!.Value
    };

    public async Task<bool> TemplateNameTakenAsync(int waNumberId, string name, string language) =>
        await _db.ScalarAsync<long>("SELECT COUNT(*) FROM WaTemplates WHERE WaNumberId=@waNumberId AND Name=@name AND Language=@language",
            new { waNumberId, name, language }) > 0;

    public Task<int> InsertTemplateAsync(WaTemplate t, int userId) => _db.ScalarAsync<int>(
        @"INSERT INTO WaTemplates (OrganizationId, GymId, WaNumberId, GoalModelId, Name, Language, Category, Body, Variables, Status, MetaTemplateId, RejectReason, CreatedBy)
          VALUES (@OrganizationId, @GymId, @WaNumberId, @GoalModelId, @Name, @Language, @Category, @Body, @Vars, @Status, @MetaTemplateId, @RejectReason, @userId);
          SELECT LAST_INSERT_ID();",
        new { t.OrganizationId, t.GymId, t.WaNumberId, t.GoalModelId, t.Name, t.Language, t.Category, t.Body, Vars = JsonSerializer.Serialize(t.Variables),
              t.Status, t.MetaTemplateId, t.RejectReason, userId });

    /// <summary>Si elimina solo un template mai approvato e non usato da nessuna campagna.</summary>
    public Task<int> DeleteTemplateAsync(int id, int gymId) => _db.ExecuteAsync(
        @"DELETE FROM WaTemplates WHERE Id=@id AND GymId=@gymId AND Status IN ('bozza','errore','rifiutato')
          AND NOT EXISTS (SELECT 1 FROM Campaigns c WHERE c.TemplateId=WaTemplates.Id)", new { id, gymId });

    public Task SetTemplateStatusAsync(int id, string status, string? metaId, string? reason) => _db.ExecuteAsync(
        "UPDATE WaTemplates SET Status=@status, MetaTemplateId=COALESCE(@metaId, MetaTemplateId), RejectReason=@reason WHERE Id=@id",
        new { id, status, metaId, reason });

    public Task<int> SetTemplateStatusByMetaAsync(string metaId, string status, string? reason) => _db.ExecuteAsync(
        "UPDATE WaTemplates SET Status=@status, RejectReason=@reason WHERE MetaTemplateId=@metaId", new { metaId, status, reason });

    // ---------- Messaggi ----------
    public Task<long> InsertMessageAsync(WaNumber n, string phone, string direction, string kind, string? body, string? template, string? metaId, string status, string? error, int? userId, long? conversationId = null) =>
        _db.ScalarAsync<long>(
            @"INSERT INTO WaMessages (OrganizationId, GymId, WaNumberId, ContactPhone, Direction, Kind, Body, TemplateName, MetaMessageId, Status, Error, SentBy, StatusAt, ConversationId)
              VALUES (@org, @gym, @num, @phone, @direction, @kind, @body, @template, @metaId, @status, @error, @userId, UTC_TIMESTAMP(), @conversationId); SELECT LAST_INSERT_ID();",
            new { org = n.OrganizationId, gym = n.GymId, num = n.Id, phone, direction, kind = Clip(kind, 20), body, template, metaId, status, error = Clip(error, 500), userId, conversationId });

    public static string? Clip(string? s, int max) => s is null || s.Length <= max ? s : s[..max];

    public Task DeleteMessageAsync(long id) => _db.ExecuteAsync("DELETE FROM WaMessages WHERE Id=@id", new { id });

    /// <summary>A chi era diretto un messaggio inviato (per collegare gli errori di consegna al cliente e alla campagna).</summary>
    public Task<SentMsg?> SentMessageAsync(string metaId) => _db.FirstAsync(
        "SELECT Id, OrganizationId, GymId, ContactPhone, ConversationId, Kind FROM WaMessages WHERE MetaMessageId=@metaId AND Direction='out'", new { metaId },
        r => new SentMsg(r.GetInt64(0), r.GetInt32(1), r.GetInt32(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetInt64(4), r.GetString(5)));

    public Task SetMessageConversationAsync(long messageId, long conversationId) =>
        _db.ExecuteAsync("UPDATE WaMessages SET ConversationId=@conversationId WHERE Id=@messageId", new { messageId, conversationId });

    public async Task<bool> MessageExistsAsync(string metaId) =>
        await _db.ScalarAsync<long>("SELECT COUNT(*) FROM WaMessages WHERE MetaMessageId=@metaId", new { metaId }) > 0;

    /// <summary>Gli stati arrivano anche fuori ordine: "letto" non torna mai indietro a "consegnato".</summary>
    public Task<int> UpdateStatusAsync(string metaId, string status, string? error) => _db.ExecuteAsync(
        @"UPDATE WaMessages SET Status=@status, Error=COALESCE(@error, Error), StatusAt=UTC_TIMESTAMP()
          WHERE MetaMessageId=@metaId AND (
            @status='failed' OR
            FIELD(Status,'queued','sent','delivered','read') < FIELD(@status,'queued','sent','delivered','read'))",
        new { metaId, status, error });

    public Task<List<WaMessage>> MessagesAsync(int gymId, int limit) => _db.QueryAsync(
        "SELECT * FROM WaMessages WHERE GymId=@gymId ORDER BY CreatedAt DESC, Id DESC LIMIT @limit", new { gymId, limit },
        r => new WaMessage(r.GetInt64(r.GetOrdinal("Id")), r.Str("ContactPhone")!, r.Str("Direction")!, r.Str("Kind")!, r.Str("Body"),
            r.Str("TemplateName"), r.Str("Status")!, r.Str("Error"), r.Date("CreatedAt")!.Value));

    // ---------- Eventi grezzi ----------
    public Task<long> LogEventAsync(string payload) =>
        _db.ScalarAsync<long>("INSERT INTO WaWebhookEvents (Payload) VALUES (@payload); SELECT LAST_INSERT_ID();", new { payload });

    public Task MarkEventAsync(long id, string? error) =>
        _db.ExecuteAsync("UPDATE WaWebhookEvents SET Processed=@ok, Error=@error WHERE Id=@id", new { id, ok = error is null, error = Clip(error, 500) });

    /// <summary>Avvisi non elaborati per un problema momentaneo (database occupato, errore imprevisto): si riprovano fino a 5 volte nelle 24 ore.</summary>
    public async Task<List<(long Id, string Payload)>> RetryEventsAsync(int limit)
    {
        var rows = await _db.QueryAsync(
            @"SELECT Id, Payload FROM WaWebhookEvents WHERE Processed=0 AND Error IS NOT NULL AND Attempts < 5
                AND ReceivedAt > UTC_TIMESTAMP() - INTERVAL 1 DAY AND ReceivedAt < UTC_TIMESTAMP() - INTERVAL 1 MINUTE ORDER BY Id LIMIT @limit",
            new { limit }, r => (r.GetInt64(0), r.GetString(1)));
        foreach (var (id, _) in rows) await _db.ExecuteAsync("UPDATE WaWebhookEvents SET Attempts=Attempts+1 WHERE Id=@id", new { id });
        return rows;
    }
}
