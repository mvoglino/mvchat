using System.Data.Common;
using MvChat.Web.Infrastructure;
using MvChat.Web.Security;

namespace MvChat.Web.Ai;

public sealed class Conversation
{
    public long Id { get; set; }
    public int OrganizationId { get; set; }
    public int GymId { get; set; }
    public string GymName { get; set; } = "";
    public int WaNumberId { get; set; }
    public string ContactPhone { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string? Membership { get; set; }
    public DateTime? ExpiresOn { get; set; }
    public int GoalModelId { get; set; }
    public string GoalName { get; set; } = "";
    public int? OfferId { get; set; }
    public int? CampaignId { get; set; }
    public bool IsTest { get; set; }
    public string Status { get; set; } = "ai";
    public string Outcome { get; set; } = "in_corso";
    public string? OutcomeNote { get; set; }
    public int AiReplies { get; set; }
    public bool NeedsReply { get; set; }
    public DateTime? ProcessingSince { get; set; }
    public int? AssignedUserId { get; set; }
    /// <summary>L'assistente ha un messaggio a cui rispondere (o lo sta già scrivendo).</summary>
    public bool AiWriting => Status == "ai" && (NeedsReply || ProcessingSince is not null);
    public DateTime? LastInboundAt { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed record UsageRow(string Purpose, string Provider, int Calls, int Failed, long InputTokens, long OutputTokens, long CacheTokens, decimal CostUsd);

public sealed record ConvMessage(long Id, string Direction, string Kind, string? Body, string Status, string? Error, int? SentBy, string? SentByName, DateTime CreatedAt);

public static class Outcomes
{
    public const string InCorso = "in_corso", Raggiunto = "obiettivo_raggiunto", Rifiuto = "rifiuto", Operatore = "operatore", OptOut = "opt_out", NessunaRisposta = "nessuna_risposta";
    public static readonly string[] All = { InCorso, Raggiunto, Rifiuto, Operatore, OptOut, NessunaRisposta };
    public static string Label(string o) => o switch
    {
        InCorso => "In corso", Raggiunto => "Obiettivo raggiunto", Rifiuto => "Rifiuto", Operatore => "Passata a operatore",
        OptOut => "Non contattare più", NessunaRisposta => "Nessuna risposta", _ => o
    };
    public static string Css(string o) => o switch { Raggiunto => "good", Rifiuto or OptOut => "bad", Operatore => "warn", _ => "" };
}

public sealed class ConversationRepo
{
    private readonly Db _db;
    public ConversationRepo(Db db) => _db = db;

    private const string Select =
        @"SELECT c.*, g.Name AS GymName, m.Name AS GoalName FROM Conversations c
          JOIN Gyms g ON g.Id=c.GymId JOIN GoalModels m ON m.Id=c.GoalModelId";

    private static Conversation Map(DbDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("Id")), OrganizationId = r.Int("OrganizationId"), GymId = r.Int("GymId"), GymName = r.Str("GymName")!,
        WaNumberId = r.Int("WaNumberId"), ContactPhone = r.Str("ContactPhone")!, ContactName = r.Str("ContactName")!, Membership = r.Str("Membership"),
        ExpiresOn = r.Date("ExpiresOn"), GoalModelId = r.Int("GoalModelId"), GoalName = r.Str("GoalName")!, OfferId = r.IntN("OfferId"),
        CampaignId = r.IntN("CampaignId"), IsTest = r.Bool("IsTest"), Status = r.Str("Status")!, Outcome = r.Str("Outcome")!,
        OutcomeNote = r.Str("OutcomeNote"), AiReplies = r.Int("AiReplies"), NeedsReply = r.Bool("NeedsReply"),
        ProcessingSince = r.Date("ProcessingSince"), AssignedUserId = r.IntN("AssignedUserId"), LastInboundAt = r.Date("LastInboundAt"),
        LastMessageAt = r.Date("LastMessageAt"), CreatedAt = r.Date("CreatedAt")!.Value
    };

    public Task<long> CreateAsync(Conversation c) => _db.ScalarAsync<long>(
        @"INSERT INTO Conversations (OrganizationId, GymId, WaNumberId, ContactPhone, ContactName, Membership, ExpiresOn, GoalModelId, OfferId, CampaignId, IsTest, Status, Outcome, LastMessageAt)
          VALUES (@OrganizationId, @GymId, @WaNumberId, @ContactPhone, @ContactName, @Membership, @ExpiresOn, @GoalModelId, @OfferId, @CampaignId, @IsTest, 'ai', 'in_corso', UTC_TIMESTAMP());
          SELECT LAST_INSERT_ID();",
        new { c.OrganizationId, c.GymId, c.WaNumberId, c.ContactPhone, c.ContactName, c.Membership, c.ExpiresOn, c.GoalModelId, c.OfferId, c.CampaignId, c.IsTest });

    /// <summary>Senza filtro di perimetro: per il lavoro automatico (webhook, assistente).</summary>
    public Task<Conversation?> GetAsync(long id) => _db.FirstAsync(Select + " WHERE c.Id=@id", new { id }, Map);

    public async Task<Conversation?> GetAsync(Scope s, long id) =>
        (await ListAsync(s, null, null, 1, id)).FirstOrDefault();

    public Task<List<Conversation>> ListAsync(Scope s, int? gymId, string? status, int limit, long? id = null) => _db.QueryAsync(
        Select + @" WHERE (@All=1 OR (@IsOrg=1 AND c.OrganizationId=@Org) OR c.GymId=@Gym)
          AND (@FGym=-1 OR c.GymId=@FGym) AND (@FStatus='' OR c.Status=@FStatus) AND (@FId=-1 OR c.Id=@FId)
          ORDER BY (c.Status='operatore') DESC, c.LastMessageAt DESC LIMIT @limit",
        new { All = s.IsSuperAdmin ? 1 : 0, IsOrg = s.IsOrgAdmin ? 1 : 0, Org = s.OrganizationId ?? -1, Gym = s.GymId ?? -1,
              FGym = gymId ?? -1, FStatus = status ?? "", FId = id ?? -1, limit }, Map);

    /// <summary>La conversazione a cui appartiene un messaggio in arrivo: la più recente con quel cliente su quel numero, negli ultimi 30 giorni.</summary>
    public Task<Conversation?> FindForInboundAsync(int waNumberId, string phone) => _db.FirstAsync(
        Select + " WHERE c.WaNumberId=@waNumberId AND c.ContactPhone=@phone AND c.CreatedAt > UTC_TIMESTAMP() - INTERVAL 30 DAY ORDER BY c.CreatedAt DESC LIMIT 1",
        new { waNumberId, phone }, Map);

    public Task<List<ConvMessage>> MessagesAsync(long conversationId, int limit = 200) => _db.QueryAsync(
        @"SELECT * FROM (SELECT m.Id, m.Direction, m.Kind, m.Body, m.Status, m.Error, m.SentBy, u.FullName AS SentByName, m.CreatedAt
            FROM WaMessages m LEFT JOIN Users u ON u.Id=m.SentBy WHERE m.ConversationId=@conversationId ORDER BY m.Id DESC LIMIT @limit) x ORDER BY Id",
        new { conversationId, limit },
        r => new ConvMessage(r.GetInt64(r.GetOrdinal("Id")), r.Str("Direction")!, r.Str("Kind")!, r.Str("Body"), r.Str("Status")!, r.Str("Error"),
            r.IntN("SentBy"), r.Str("SentByName"), r.Date("CreatedAt")!.Value));

    public Task InboundAsync(long id, bool needsReply, string? reopenAs) => _db.ExecuteAsync(
        @"UPDATE Conversations SET LastInboundAt=UTC_TIMESTAMP(), LastMessageAt=UTC_TIMESTAMP(),
            NeedsReply=CASE WHEN @needsReply=1 THEN 1 ELSE NeedsReply END,
            Status=COALESCE(@reopenAs, Status) WHERE Id=@id",
        new { id, needsReply = needsReply ? 1 : 0, reopenAs });

    /// <summary>Prende in carico la risposta: solo un processo alla volta per conversazione.</summary>
    public async Task<bool> ClaimAsync(long id) => await _db.ExecuteAsync(
        @"UPDATE Conversations SET NeedsReply=0, ProcessingSince=UTC_TIMESTAMP()
          WHERE Id=@id AND NeedsReply=1 AND (ProcessingSince IS NULL OR ProcessingSince < UTC_TIMESTAMP() - INTERVAL 2 MINUTE)", new { id }) > 0;

    public Task ReleaseAsync(long id) => _db.ExecuteAsync("UPDATE Conversations SET ProcessingSince=NULL WHERE Id=@id", new { id });

    public Task<List<long>> PendingAsync(int olderThanSeconds) => _db.QueryAsync(
        "SELECT Id FROM Conversations WHERE NeedsReply=1 AND Status='ai' AND LastInboundAt < UTC_TIMESTAMP() - INTERVAL @olderThanSeconds SECOND LIMIT 50",
        new { olderThanSeconds }, r => r.GetInt64(0));

    public Task AfterAiReplyAsync(long id, string status, string outcome, string? note) => _db.ExecuteAsync(
        @"UPDATE Conversations SET AiReplies=AiReplies+1, Status=@status, Outcome=@outcome, OutcomeNote=COALESCE(@note, OutcomeNote),
            LastMessageAt=UTC_TIMESTAMP() WHERE Id=@id",
        new { id, status, outcome, note });

    public Task SetStateAsync(long id, string status, string outcome, string? note, int? assignedUserId = null) => _db.ExecuteAsync(
        @"UPDATE Conversations SET Status=@status, Outcome=@outcome, OutcomeNote=COALESCE(@note, OutcomeNote),
            AssignedUserId=COALESCE(@assignedUserId, AssignedUserId), NeedsReply=CASE WHEN @status='ai' THEN NeedsReply ELSE 0 END WHERE Id=@id",
        new { id, status, outcome, note, assignedUserId });

    /// <summary>Restituisce la conversazione all'assistente; se l'ultimo messaggio è del cliente, l'assistente gli risponde.</summary>
    public Task<int> GiveBackToAiAsync(long id) => _db.ExecuteAsync(
        @"UPDATE Conversations SET Status='ai', Outcome='in_corso', AssignedUserId=NULL,
            NeedsReply=CASE WHEN (SELECT x.Direction FROM (SELECT Direction FROM WaMessages WHERE ConversationId=@id ORDER BY Id DESC LIMIT 1) x)='in' THEN 1 ELSE 0 END
          WHERE Id=@id", new { id });

    public async Task<Dictionary<string, int>> CountsAsync(Scope s) => (await _db.QueryAsync(
        @"SELECT c.Status, COUNT(*) FROM Conversations c WHERE (@All=1 OR (@IsOrg=1 AND c.OrganizationId=@Org) OR c.GymId=@Gym) GROUP BY c.Status",
        new { All = s.IsSuperAdmin ? 1 : 0, IsOrg = s.IsOrgAdmin ? 1 : 0, Org = s.OrganizationId ?? -1, Gym = s.GymId ?? -1 },
        r => (r.GetString(0), Convert.ToInt32(r.GetValue(1))))).ToDictionary(x => x.Item1, x => x.Item2);

    public Task TouchAsync(long id) => _db.ExecuteAsync("UPDATE Conversations SET LastMessageAt=UTC_TIMESTAMP() WHERE Id=@id", new { id });

    public Task LogUsageAsync(int? orgId, int? gymId, long? conversationId, string purpose, AiResult r, decimal cost) => _db.ExecuteAsync(
        @"INSERT INTO AiUsage (OrganizationId, GymId, ConversationId, Purpose, Provider, Model, InputTokens, OutputTokens, CacheReadTokens, CacheWriteTokens, CostUsd, Ok, Error)
          VALUES (@orgId, @gymId, @conversationId, @purpose, @Provider, @Model, @InputTokens, @OutputTokens, @CacheReadTokens, @CacheWriteTokens, @cost, @ok, @Error)",
        new { orgId, gymId, conversationId, purpose, r.Provider, r.Model, r.InputTokens, r.OutputTokens, r.CacheReadTokens, r.CacheWriteTokens, cost, ok = r.Ok, Error = r.Error is { Length: > 480 } e ? e[..480] : r.Error });

    /// <summary>Riepilogo dei consumi AI per uso e fornitore: la base per la rifatturazione del Passo 9.</summary>
    public Task<List<UsageRow>> UsageSummaryAsync(int days) => _db.QueryAsync(
        @"SELECT Purpose, Provider, COUNT(*) AS Calls, SUM(Ok=0) AS Failed, SUM(InputTokens) AS I, SUM(OutputTokens) AS O,
                 SUM(CacheReadTokens + CacheWriteTokens) AS C, SUM(CostUsd) AS Cost
          FROM AiUsage WHERE CreatedAt > UTC_TIMESTAMP() - INTERVAL @days DAY GROUP BY Purpose, Provider ORDER BY Purpose, Provider",
        new { days },
        r => new UsageRow(r.Str("Purpose")!, r.Str("Provider")!, Convert.ToInt32(r.GetValue(2)), Convert.ToInt32(r.GetValue(3)),
            Convert.ToInt64(r.GetValue(4)), Convert.ToInt64(r.GetValue(5)), Convert.ToInt64(r.GetValue(6)), Convert.ToDecimal(r.GetValue(7))));
}
