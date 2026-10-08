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
    /// <summary>Servizio o corso del cliente e nota libera, dalla lista contatti: l'assistente li usa per personalizzare.</summary>
    public string? Service { get; set; }
    public string? Notes { get; set; }
    /// <summary>Solo per le prove inviate da una campagna: di quale campagna usare le istruzioni in più.</summary>
    public int? TestOfCampaignId { get; set; }
    public DateTime? ExpiresOn { get; set; }
    /// <summary>Nullo per i messaggi spontanei (il cliente scrive senza una campagna).</summary>
    public int? GoalModelId { get; set; }
    public string GoalName { get; set; } = "";
    public int? OfferId { get; set; }
    public int? CampaignId { get; set; }
    public bool IsTest { get; set; }
    public string Status { get; set; } = "ai";
    public string Outcome { get; set; } = "in_corso";
    public string? OutcomeNote { get; set; }
    /// <summary>Solo per i rifiuti: il motivo (codice di <see cref="RefusalReasons"/>).</summary>
    public string? RefusalReason { get; set; }
    public int AiReplies { get; set; }
    public bool NeedsReply { get; set; }
    public DateTime? ProcessingSince { get; set; }
    public int? AssignedUserId { get; set; }
    public string? AssignedName { get; set; }
    public bool HumanInvolved { get; set; }
    /// <summary>L'ultimo messaggio è del cliente: aspetta una risposta.</summary>
    public bool Awaiting => LastInboundAt is DateTime i && (LastMessageAt is null || i >= LastMessageAt.Value);
    /// <summary>L'assistente ha un messaggio a cui rispondere (o lo sta già scrivendo).</summary>
    public bool AiWriting => Status == "ai" && (NeedsReply || ProcessingSince is not null);
    public DateTime? LastInboundAt { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed record UsageRow(string Purpose, string Provider, int Calls, int Failed, long InputTokens, long OutputTokens, long CacheTokens, decimal CostUsd);

public sealed record ConvMessage(long Id, string Direction, string Kind, string? Body, string Status, string? Error, int? SentBy, string? SentByName, DateTime CreatedAt,
    bool HasMedia = false, bool MediaGone = false);

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
        @"SELECT c.*, g.Name AS GymName, COALESCE(m.Name, 'Messaggio spontaneo') AS GoalName, au.FullName AS AssignedName FROM Conversations c
          JOIN Gyms g ON g.Id=c.GymId LEFT JOIN GoalModels m ON m.Id=c.GoalModelId LEFT JOIN Users au ON au.Id=c.AssignedUserId";

    private static Conversation Map(DbDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("Id")), OrganizationId = r.Int("OrganizationId"), GymId = r.Int("GymId"), GymName = r.Str("GymName")!,
        WaNumberId = r.Int("WaNumberId"), ContactPhone = r.Str("ContactPhone")!, ContactName = r.Str("ContactName")!, Membership = r.Str("Membership"), Service = r.Str("Service"), Notes = r.Str("Notes"), TestOfCampaignId = r.IntN("TestOfCampaignId"),
        ExpiresOn = r.Date("ExpiresOn"), GoalModelId = r.IntN("GoalModelId"), GoalName = r.Str("GoalName")!, OfferId = r.IntN("OfferId"),
        CampaignId = r.IntN("CampaignId"), IsTest = r.Bool("IsTest"), Status = r.Str("Status")!, Outcome = r.Str("Outcome")!,
        OutcomeNote = r.Str("OutcomeNote"), RefusalReason = r.Str("RefusalReason"), AiReplies = r.Int("AiReplies"), NeedsReply = r.Bool("NeedsReply"),
        ProcessingSince = r.Date("ProcessingSince"), AssignedUserId = r.IntN("AssignedUserId"),
        AssignedName = r.Str("AssignedName"), HumanInvolved = r.Bool("HumanInvolved"), LastInboundAt = r.Date("LastInboundAt"),
        LastMessageAt = r.Date("LastMessageAt"), CreatedAt = r.Date("CreatedAt")!.Value
    };

    public Task<long> CreateAsync(Conversation c) => _db.ScalarAsync<long>(
        @"INSERT INTO Conversations (OrganizationId, GymId, WaNumberId, ContactPhone, ContactName, Membership, ExpiresOn, Service, Notes, GoalModelId, OfferId, CampaignId, TestOfCampaignId, IsTest, Status, Outcome, LastMessageAt)
          VALUES (@OrganizationId, @GymId, @WaNumberId, @ContactPhone, @ContactName, @Membership, @ExpiresOn, @Service, @Notes, @GoalModelId, @OfferId, @CampaignId, @TestOfCampaignId, @IsTest, 'ai', 'in_corso', UTC_TIMESTAMP());
          SELECT LAST_INSERT_ID();",
        new { c.OrganizationId, c.GymId, c.WaNumberId, c.ContactPhone, c.ContactName, c.Membership, c.ExpiresOn, c.Service, c.Notes, c.GoalModelId, c.OfferId, c.CampaignId, c.TestOfCampaignId, c.IsTest });

    /// <summary>Le istruzioni in più della campagna da cui nasce la conversazione (lette a ogni risposta: se cambiano, valgono subito).</summary>
    public Task<string?> CampaignInstructionsAsync(int campaignId) =>
        _db.ScalarAsync<string>("SELECT ExtraInstructions FROM Campaigns WHERE Id=@campaignId", new { campaignId });

    /// <summary>Un cliente scrive senza una campagna in corso: si apre una conversazione per la reception (l'assistente non risponde).</summary>
    public Task<long> CreateSpontaneousAsync(MvChat.Web.WhatsApp.WaNumber n, string phone, string name) => _db.ScalarAsync<long>(
        @"INSERT INTO Conversations (OrganizationId, GymId, WaNumberId, ContactPhone, ContactName, Status, Outcome, OutcomeNote, HumanInvolved, LastInboundAt, LastMessageAt)
          VALUES (@org, @gym, @num, @phone, @name, 'operatore', 'operatore', 'messaggio arrivato fuori da una campagna', 1, UTC_TIMESTAMP(), UTC_TIMESTAMP());
          SELECT LAST_INSERT_ID();",
        new { org = n.OrganizationId, gym = n.GymId, num = n.Id, phone, name = name.Length > 150 ? name[..150] : name });

    /// <summary>Il primo messaggio della campagna non è stato consegnato: destinatario in errore e conversazione chiusa (se il cliente non ha mai scritto).</summary>
    public async Task TemplateFailedAsync(long id, string reason)
    {
        await _db.ExecuteAsync("UPDATE CampaignRecipients SET Status='errore', Reason=@reason WHERE ConversationId=@id AND Status='inviato'", new { id, reason });
        await _db.ExecuteAsync(
            @"UPDATE Conversations SET Status='chiusa', Outcome='nessuna_risposta', OutcomeNote=@reason, NeedsReply=0
              WHERE Id=@id AND Status='ai' AND AiReplies=0 AND LastInboundAt IS NULL", new { id, reason });
    }

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

    private const string ScopeWhere = "(@All=1 OR (@IsOrg=1 AND c.OrganizationId=@Org) OR c.GymId=@Gym)";
    private static object ScopeArgs(Scope s) => new { All = s.IsSuperAdmin ? 1 : 0, IsOrg = s.IsOrgAdmin ? 1 : 0, Org = s.OrganizationId ?? -1, Gym = s.GymId ?? -1 };

    /// <summary>
    /// La postazione della reception. Viste: da_gestire (passate a una persona, libere o mie) · mie · ai · chiuse · tutte.
    /// Prima chi aspetta da più tempo.
    /// </summary>
    public Task<List<Conversation>> InboxAsync(Scope s, int? gymId, string view, int limit) => _db.QueryAsync(
        Select + $@" WHERE {ScopeWhere} AND (@FGym=-1 OR c.GymId=@FGym) AND (
            (@view='da_gestire' AND c.Status='operatore' AND (c.AssignedUserId IS NULL OR c.AssignedUserId=@Me)) OR
            (@view='mie' AND c.AssignedUserId=@Me AND c.Status<>'chiusa') OR
            (@view='ai' AND c.Status='ai') OR (@view='chiuse' AND c.Status='chiusa') OR @view='tutte')
          ORDER BY (c.Status='operatore' AND c.LastInboundAt >= c.LastMessageAt) DESC,
                   CASE WHEN c.Status='operatore' THEN c.LastInboundAt END ASC, c.LastMessageAt DESC LIMIT @limit",
        new { All = s.IsSuperAdmin ? 1 : 0, IsOrg = s.IsOrgAdmin ? 1 : 0, Org = s.OrganizationId ?? -1, Gym = s.GymId ?? -1,
              FGym = gymId ?? -1, view, Me = s.UserId, limit }, Map);

    /// <summary>Per l'avviso nel menu: conversazioni che aspettano una persona (libere, oppure mie con il cliente in attesa).</summary>
    public async Task<(int Count, long? LatestId, string? LatestName)> BadgeAsync(Scope s)
    {
        var rows = await _db.QueryAsync(
            $@"SELECT c.Id, c.ContactName FROM Conversations c WHERE {ScopeWhere} AND c.Status='operatore'
                 AND (c.AssignedUserId IS NULL OR (c.AssignedUserId=@Me AND c.LastInboundAt >= c.LastMessageAt))
               ORDER BY c.LastMessageAt DESC LIMIT 100",
            new { All = s.IsSuperAdmin ? 1 : 0, IsOrg = s.IsOrgAdmin ? 1 : 0, Org = s.OrganizationId ?? -1, Gym = s.GymId ?? -1, Me = s.UserId },
            r => (r.GetInt64(0), r.GetString(1)));
        return rows.Count == 0 ? (0, null, null) : (rows.Count, rows[0].Item1, rows[0].Item2);
    }

    public Task<int> AssignAsync(long id, int? userId) => _db.ExecuteAsync(
        "UPDATE Conversations SET AssignedUserId=@userId, HumanInvolved=CASE WHEN @userId IS NULL THEN HumanInvolved ELSE 1 END WHERE Id=@id",
        new { id, userId });

    /// <summary>
    /// La conversazione a cui appartiene un messaggio in arrivo: la più recente con quel cliente su quel numero.
    /// Le conversazioni di prova passano dopo quelle vere, così una prova non "ruba" il messaggio di un cliente.
    /// </summary>
    public Task<Conversation?> FindForInboundAsync(int waNumberId, string phone) => _db.FirstAsync(
        Select + " WHERE c.WaNumberId=@waNumberId AND c.ContactPhone=@phone ORDER BY (c.IsTest=1 AND c.Status='chiusa') ASC, c.CreatedAt DESC LIMIT 1",
        new { waNumberId, phone }, Map);

    public Task<List<ConvMessage>> MessagesAsync(long conversationId, int limit = 200) => _db.QueryAsync(
        @"SELECT * FROM (SELECT m.Id, m.Direction, m.Kind, m.Body, m.Status, m.Error, m.SentBy, u.FullName AS SentByName, m.CreatedAt, m.MediaId, m.MediaFile
            FROM WaMessages m LEFT JOIN Users u ON u.Id=m.SentBy WHERE m.ConversationId=@conversationId ORDER BY m.Id DESC LIMIT @limit) x ORDER BY Id",
        new { conversationId, limit },
        r => new ConvMessage(r.GetInt64(r.GetOrdinal("Id")), r.Str("Direction")!, r.Str("Kind")!, r.Str("Body"), r.Str("Status")!, r.Str("Error"),
            r.IntN("SentBy"), r.Str("SentByName"), r.Date("CreatedAt")!.Value, r.Str("MediaId") is not null, r.Str("MediaFile") == "-"));

    /// <summary>
    /// Vocali del cliente ancora da trascrivere, arrivati dopo l'ultima risposta (quelli prima li ha già gestiti qualcuno).
    /// Quelli che Meta non ha più non si riprovano.
    /// </summary>
    public Task<List<long>> PendingVoicesAsync(long id) => _db.QueryAsync(
        @"SELECT m.Id FROM WaMessages m WHERE m.ConversationId=@id AND m.Direction='in' AND m.Kind IN ('audio','voice') AND m.Transcribed=0
            AND m.MediaId IS NOT NULL AND (m.MediaFile IS NULL OR m.MediaFile<>'-')
            AND m.Id > COALESCE((SELECT MAX(o.Id) FROM WaMessages o WHERE o.ConversationId=@id AND o.Direction='out'), 0) ORDER BY m.Id",
        new { id }, r => r.GetInt64(0));

    public Task SetTranscriptAsync(long messageId, string body) =>
        _db.ExecuteAsync("UPDATE WaMessages SET Body=@body, Transcribed=1 WHERE Id=@messageId", new { messageId, body });

    public Task InboundAsync(long id, bool needsReply, string? reopenAs) => _db.ExecuteAsync(
        @"UPDATE Conversations SET LastInboundAt=UTC_TIMESTAMP(), LastMessageAt=UTC_TIMESTAMP(),
            NeedsReply=CASE WHEN @needsReply=1 THEN 1 ELSE NeedsReply END,
            Status=COALESCE(@reopenAs, Status), HumanInvolved=CASE WHEN @reopenAs='operatore' THEN 1 ELSE HumanInvolved END WHERE Id=@id",
        new { id, needsReply = needsReply ? 1 : 0, reopenAs });

    /// <summary>
    /// Prende in carico la risposta: solo un processo alla volta per conversazione.
    /// Se un lavoro si è interrotto a metà (riavvio dell'hosting), dopo 5 minuti la conversazione si può riprendere.
    /// </summary>
    public async Task<bool> ClaimAsync(long id) => await _db.ExecuteAsync(
        @"UPDATE Conversations SET NeedsReply=0, ProcessingSince=UTC_TIMESTAMP()
          WHERE Id=@id AND Status='ai' AND (
            (NeedsReply=1 AND (ProcessingSince IS NULL OR ProcessingSince < UTC_TIMESTAMP() - INTERVAL 5 MINUTE)) OR
            (NeedsReply=0 AND ProcessingSince < UTC_TIMESTAMP() - INTERVAL 5 MINUTE))", new { id }) > 0;

    /// <summary>Prima di mandare la risposta: la conversazione è ancora dell'assistente e non sono arrivati altri messaggi nel frattempo?</summary>
    public async Task<(string Status, bool NeedsReply)?> FreshStateAsync(long id)
    {
        var row = await _db.FirstAsync("SELECT Status, NeedsReply FROM Conversations WHERE Id=@id", new { id }, r => Tuple.Create(r.GetString(0), r.GetBoolean(1)));
        return row is null ? null : (row.Item1, row.Item2);
    }

    public Task ReleaseAsync(long id) => _db.ExecuteAsync("UPDATE Conversations SET ProcessingSince=NULL WHERE Id=@id", new { id });

    public Task<List<long>> PendingAsync(int olderThanSeconds) => _db.QueryAsync(
        @"SELECT Id FROM Conversations WHERE Status='ai' AND (
            (NeedsReply=1 AND LastInboundAt < UTC_TIMESTAMP() - INTERVAL @olderThanSeconds SECOND) OR
            ProcessingSince < UTC_TIMESTAMP() - INTERVAL 5 MINUTE) LIMIT 50",
        new { olderThanSeconds }, r => r.GetInt64(0));

    public Task AfterAiReplyAsync(long id, string status, string outcome, string? note, string? reason = null) => _db.ExecuteAsync(
        @"UPDATE Conversations SET AiReplies=AiReplies+1, Status=@status, Outcome=@outcome, OutcomeNote=COALESCE(@note, OutcomeNote),
            RefusalReason=CASE WHEN @outcome='rifiuto' THEN COALESCE(@reason, 'altro') ELSE NULL END,
            HumanInvolved=CASE WHEN @status='operatore' THEN 1 ELSE HumanInvolved END,
            LastMessageAt=UTC_TIMESTAMP() WHERE Id=@id AND Status='ai'",
        new { id, status, outcome, note, reason });

    /// <summary>Motivo del rifiuto scelto dall'operatore (vuoto se l'esito non è un rifiuto).</summary>
    public Task SetRefusalReasonAsync(long id, string? reason) =>
        _db.ExecuteAsync("UPDATE Conversations SET RefusalReason=@reason WHERE Id=@id", new { id, reason });

    public Task SetStateAsync(long id, string status, string outcome, string? note, int? assignedUserId = null) => _db.ExecuteAsync(
        @"UPDATE Conversations SET Status=@status, Outcome=@outcome, OutcomeNote=COALESCE(@note, OutcomeNote),
            HumanInvolved=CASE WHEN @status='operatore' OR @assignedUserId IS NOT NULL THEN 1 ELSE HumanInvolved END,
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
