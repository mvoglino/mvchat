using MvChat.Web.Infrastructure;
using MvChat.Web.Security;

namespace MvChat.Web.Ai;

public sealed class ArchiveFilter
{
    public int? GymId { get; set; }
    public string Kind { get; set; } = "";       // "" tutte · ai (solo assistente) · operatore (proseguite con una persona)
    public string Outcome { get; set; } = "";
    public DateTime? From { get; set; }           // giorni in ora italiana
    public DateTime? To { get; set; }
    public int? CampaignId { get; set; }
    public string? Search { get; set; }
    public bool IncludeTests { get; set; }
}

public sealed record ArchiveRow(long Id, string GymName, string ContactName, string ContactPhone, string GoalName, string? CampaignName,
    bool IsTest, bool HumanInvolved, string Status, string Outcome, int CustomerMsgs, int AiMsgs, int HumanMsgs, string? Operators,
    DateTime CreatedAt, DateTime? LastMessageAt);

public sealed record ArchiveStats(int Total, int AiOnly, int WithOperator, int AiReached, int OperatorReached, int Replied);

/// <summary>Archivio di tutti i dialoghi, divisi tra quelli gestiti solo dall'assistente e quelli proseguiti con una persona.</summary>
public sealed class ArchiveRepo
{
    private readonly Db _db;
    public ArchiveRepo(Db db) => _db = db;

    private const string Where = @"
        WHERE (@All=1 OR (@IsOrg=1 AND c.OrganizationId=@Org) OR c.GymId=@Gym)
          AND (@FGym=-1 OR c.GymId=@FGym)
          AND (@Tests=1 OR c.IsTest=0)
          AND (@Outcome='' OR c.Outcome=@Outcome)
          AND (@Camp=-1 OR c.CampaignId=@Camp)
          AND (@FromUtc IS NULL OR c.CreatedAt >= @FromUtc)
          AND (@ToUtc IS NULL OR c.CreatedAt < @ToUtc)
          AND (@Q='' OR c.ContactName LIKE @QLike OR c.ContactPhone LIKE @QLike)";

    private static object Args(Scope s, ArchiveFilter f, int limit = 0)
    {
        var q = (f.Search ?? "").Trim();
        return new
        {
            All = s.IsSuperAdmin ? 1 : 0, IsOrg = s.IsOrgAdmin ? 1 : 0, Org = s.OrganizationId ?? -1, Gym = s.GymId ?? -1,
            FGym = f.GymId ?? -1, Tests = f.IncludeTests ? 1 : 0, Outcome = f.Outcome ?? "", Camp = f.CampaignId ?? -1,
            FromUtc = f.From?.Date.FromRome(), ToUtc = f.To?.Date.AddDays(1).FromRome(),
            Q = q, QLike = "%" + q.Replace("%", "").Replace("_", "") + "%",
            Kind = f.Kind ?? "", limit
        };
    }

    public Task<List<ArchiveRow>> ListAsync(Scope s, ArchiveFilter f, int limit) => _db.QueryAsync(
        @"SELECT c.Id, g.Name AS GymName, c.ContactName, c.ContactPhone, COALESCE(m.Name, 'Messaggio spontaneo') AS GoalName, k.Name AS CampaignName, c.IsTest, c.HumanInvolved,
                 c.Status, c.Outcome, c.CreatedAt, c.LastMessageAt,
                 (SELECT COUNT(*) FROM WaMessages w WHERE w.ConversationId=c.Id AND w.Direction='in') AS CustomerMsgs,
                 (SELECT COUNT(*) FROM WaMessages w WHERE w.ConversationId=c.Id AND w.Direction='out' AND w.Kind='text' AND w.SentBy IS NULL) AS AiMsgs,
                 (SELECT COUNT(*) FROM WaMessages w WHERE w.ConversationId=c.Id AND w.Direction='out' AND w.Kind='text' AND w.SentBy IS NOT NULL) AS HumanMsgs,
                 (SELECT GROUP_CONCAT(DISTINCT u.FullName ORDER BY u.FullName SEPARATOR ', ') FROM WaMessages w JOIN Users u ON u.Id=w.SentBy
                    WHERE w.ConversationId=c.Id AND w.Direction='out' AND w.Kind='text') AS Operators
          FROM Conversations c JOIN Gyms g ON g.Id=c.GymId LEFT JOIN GoalModels m ON m.Id=c.GoalModelId LEFT JOIN Campaigns k ON k.Id=c.CampaignId"
        + Where + @" AND (@Kind='' OR (@Kind='ai' AND c.HumanInvolved=0) OR (@Kind='operatore' AND c.HumanInvolved=1))
          ORDER BY c.CreatedAt DESC LIMIT @limit",
        Args(s, f, limit),
        r => new ArchiveRow(r.GetInt64(r.GetOrdinal("Id")), r.Str("GymName")!, r.Str("ContactName")!, r.Str("ContactPhone")!, r.Str("GoalName")!,
            r.Str("CampaignName"), r.Bool("IsTest"), r.Bool("HumanInvolved"), r.Str("Status")!, r.Str("Outcome")!,
            Convert.ToInt32(r.GetValue(r.GetOrdinal("CustomerMsgs"))), Convert.ToInt32(r.GetValue(r.GetOrdinal("AiMsgs"))),
            Convert.ToInt32(r.GetValue(r.GetOrdinal("HumanMsgs"))), r.Str("Operators"), r.Date("CreatedAt")!.Value, r.Date("LastMessageAt")));

    /// <summary>I numeri in testa all'archivio, con gli stessi filtri (tranne la divisione AI/operatore, che è proprio quello che si confronta).</summary>
    public async Task<ArchiveStats> StatsAsync(Scope s, ArchiveFilter f) =>
        (await _db.QueryAsync(
            @"SELECT COUNT(*), COALESCE(SUM(c.HumanInvolved=0),0), COALESCE(SUM(c.HumanInvolved=1),0),
                     COALESCE(SUM(c.HumanInvolved=0 AND c.Outcome='obiettivo_raggiunto'),0), COALESCE(SUM(c.HumanInvolved=1 AND c.Outcome='obiettivo_raggiunto'),0),
                     COALESCE(SUM(c.LastInboundAt IS NOT NULL),0)
              FROM Conversations c" + Where,
            Args(s, f),
            r => new ArchiveStats(Convert.ToInt32(r.GetValue(0)), Convert.ToInt32(r.GetValue(1)), Convert.ToInt32(r.GetValue(2)),
                Convert.ToInt32(r.GetValue(3)), Convert.ToInt32(r.GetValue(4)), Convert.ToInt32(r.GetValue(5))))).First();
}
