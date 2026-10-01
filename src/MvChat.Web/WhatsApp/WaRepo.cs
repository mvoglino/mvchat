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

    /// <summary>Numero di una palestra, senza filtro di perimetro: chi chiama deve aver già controllato la palestra.</summary>
    public Task<WaNumber?> NumberForGymAsync(int gymId) => _db.FirstAsync(NumberSelect + " WHERE n.GymId=@gymId", new { gymId }, MapNumber);
    public Task<WaNumber?> NumberAsync(int id) => _db.FirstAsync(NumberSelect + " WHERE n.Id=@id", new { id }, MapNumber);
    public Task<WaNumber?> NumberByPhoneNumberIdAsync(string phoneNumberId) =>
        _db.FirstAsync(NumberSelect + " WHERE n.PhoneNumberId=@phoneNumberId", new { phoneNumberId }, MapNumber);

    private static WaNumber MapNumber(DbDataReader r) => new()
    {
        Id = r.Int("Id"), OrganizationId = r.Int("OrganizationId"), GymId = r.Int("GymId"), GymName = r.Str("GymName")!,
        DisplayPhone = r.Str("DisplayPhone")!, DisplayName = r.Str("DisplayName"), PhoneNumberId = r.Str("PhoneNumberId"), WabaId = r.Str("WabaId"),
        AccessTokenEnc = r.Str("AccessTokenEnc"), IsSimulated = r.Bool("IsSimulated"), Status = r.Str("Status")!, QualityRating = r.Str("QualityRating"),
        MessagingLimit = r.Str("MessagingLimit"), LastError = r.Str("LastError"), LastCheckAt = r.Date("LastCheckAt")
    };

    public async Task<int> SaveNumberAsync(WaNumber n)
    {
        var args = new { n.Id, n.OrganizationId, n.GymId, n.DisplayPhone, n.DisplayName, n.PhoneNumberId, n.WabaId, n.AccessTokenEnc, n.IsSimulated, n.Status };
        if (n.Id == 0)
            return await _db.ScalarAsync<int>(
                @"INSERT INTO WaNumbers (OrganizationId, GymId, DisplayPhone, DisplayName, PhoneNumberId, WabaId, AccessTokenEnc, IsSimulated, Status)
                  VALUES (@OrganizationId, @GymId, @DisplayPhone, @DisplayName, @PhoneNumberId, @WabaId, @AccessTokenEnc, @IsSimulated, @Status); SELECT LAST_INSERT_ID();", args);
        await _db.ExecuteAsync(
            @"UPDATE WaNumbers SET DisplayPhone=@DisplayPhone, DisplayName=@DisplayName, PhoneNumberId=@PhoneNumberId, WabaId=@WabaId,
              AccessTokenEnc=@AccessTokenEnc, IsSimulated=@IsSimulated, Status=@Status, LastError=NULL WHERE Id=@Id AND GymId=@GymId", args);
        return n.Id;
    }

    public Task SetNumberCheckAsync(int id, string status, string? quality, string? limit, string? displayName, string? error) => _db.ExecuteAsync(
        @"UPDATE WaNumbers SET Status=@status, QualityRating=COALESCE(@quality, QualityRating), MessagingLimit=COALESCE(@limit, MessagingLimit),
          DisplayName=COALESCE(@displayName, DisplayName), LastError=@error, LastCheckAt=UTC_TIMESTAMP() WHERE Id=@id",
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

    public Task SetTemplateStatusAsync(int id, string status, string? metaId, string? reason) => _db.ExecuteAsync(
        "UPDATE WaTemplates SET Status=@status, MetaTemplateId=COALESCE(@metaId, MetaTemplateId), RejectReason=@reason WHERE Id=@id",
        new { id, status, metaId, reason });

    public Task<int> SetTemplateStatusByMetaAsync(string metaId, string status, string? reason) => _db.ExecuteAsync(
        "UPDATE WaTemplates SET Status=@status, RejectReason=@reason WHERE MetaTemplateId=@metaId", new { metaId, status, reason });

    // ---------- Messaggi ----------
    public Task<long> InsertMessageAsync(WaNumber n, string phone, string direction, string kind, string? body, string? template, string? metaId, string status, string? error, int? userId) =>
        _db.ScalarAsync<long>(
            @"INSERT INTO WaMessages (OrganizationId, GymId, WaNumberId, ContactPhone, Direction, Kind, Body, TemplateName, MetaMessageId, Status, Error, SentBy, StatusAt)
              VALUES (@org, @gym, @num, @phone, @direction, @kind, @body, @template, @metaId, @status, @error, @userId, UTC_TIMESTAMP()); SELECT LAST_INSERT_ID();",
            new { org = n.OrganizationId, gym = n.GymId, num = n.Id, phone, direction, kind, body, template, metaId, status, error, userId });

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
        _db.ExecuteAsync("UPDATE WaWebhookEvents SET Processed=@ok, Error=@error WHERE Id=@id", new { id, ok = error is null, error });
}
