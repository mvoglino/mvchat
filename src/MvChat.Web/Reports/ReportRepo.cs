using System.Data.Common;
using MvChat.Web.Infrastructure;
using MvChat.Web.Security;

namespace MvChat.Web.Reports;

/// <summary>I numeri di una riga del report: un gruppo, un'attività o una campagna, nel periodo scelto.</summary>
public sealed class ReportRow
{
    public string Kind { get; set; } = "";      // gruppo · attivita · campagna
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Detail { get; set; }
    public string? Link { get; set; }
    public int SentMarketing { get; set; }
    public int SentUtility { get; set; }
    public int Delivered { get; set; }
    public int Read { get; set; }
    public int Conversations { get; set; }
    public int Replied { get; set; }
    public int Reached { get; set; }
    public int Refused { get; set; }
    public int OptOut { get; set; }
    public int AiOnly { get; set; }
    public int WithOperator { get; set; }
    public decimal AiCostUsd { get; set; }

    public int Sent => SentMarketing + SentUtility;
    public decimal MetaCostEur(MetaSettings m) => SentMarketing * m.MarketingPriceEur + SentUtility * m.UtilityPriceEur;
    public static int Pct(int part, int total) => total == 0 ? 0 : (int)Math.Round(100.0 * part / total);

    public void Add(ReportRow o)
    {
        SentMarketing += o.SentMarketing; SentUtility += o.SentUtility; Delivered += o.Delivered; Read += o.Read;
        Conversations += o.Conversations; Replied += o.Replied; Reached += o.Reached; Refused += o.Refused; OptOut += o.OptOut;
        AiOnly += o.AiOnly; WithOperator += o.WithOperator; AiCostUsd += o.AiCostUsd;
    }
}

/// <summary>Un giorno del grafico (ora italiana).</summary>
public sealed record DayPoint(DateTime Day, int Sent, int Received, int Reached);

/// <summary>
/// Report per MVitalia (gruppi e attività singole), per l'amministratore di gruppo (le sue attività)
/// e per l'amministratore di attività (le sue campagne). Il perimetro è sempre nella query.
/// </summary>
public sealed class ReportRepo
{
    private readonly Db _db;
    public ReportRepo(Db db) => _db = db;

    private const string ScopeWhere = "(@All=1 OR (@IsOrg=1 AND g.OrganizationId=@Org) OR g.Id=@Gym)";

    private static object Args(Scope s, DateTime fromUtc, DateTime toUtc, int? orgId = null, int? gymId = null) => new
    {
        All = s.IsSuperAdmin ? 1 : 0, IsOrg = s.IsOrgAdmin ? 1 : 0, Org = s.OrganizationId ?? -1, Gym = s.GymId ?? -1,
        fromUtc, toUtc, FOrg = orgId ?? -1, FGym = gymId ?? -1
    };

    // I primi messaggi (template) sono quelli che Meta fattura; le conversazioni di prova non contano nei risultati.
    private const string Metrics = @"
        (SELECT COUNT(*) FROM WaMessages m
           WHERE {M} AND m.Direction='out' AND m.Kind='template' AND m.Status<>'failed' AND m.CreatedAt>=@fromUtc AND m.CreatedAt<@toUtc
             AND NOT EXISTS (SELECT 1 FROM WaTemplates t WHERE t.WaNumberId=m.WaNumberId AND t.Name=m.TemplateName AND t.Category='UTILITY')) AS SentMarketing,
        (SELECT COUNT(*) FROM WaMessages m
           WHERE {M} AND m.Direction='out' AND m.Kind='template' AND m.Status<>'failed' AND m.CreatedAt>=@fromUtc AND m.CreatedAt<@toUtc
             AND EXISTS (SELECT 1 FROM WaTemplates t WHERE t.WaNumberId=m.WaNumberId AND t.Name=m.TemplateName AND t.Category='UTILITY')) AS SentUtility,
        (SELECT COUNT(*) FROM WaMessages m WHERE {M} AND m.Direction='out' AND m.Kind='template' AND m.Status IN ('delivered','read')
           AND m.CreatedAt>=@fromUtc AND m.CreatedAt<@toUtc) AS Delivered,
        (SELECT COUNT(*) FROM WaMessages m WHERE {M} AND m.Direction='out' AND m.Kind='template' AND m.Status='read'
           AND m.CreatedAt>=@fromUtc AND m.CreatedAt<@toUtc) AS ReadCount,
        (SELECT COUNT(*) FROM Conversations v WHERE {V} AND v.IsTest=0 AND v.CreatedAt>=@fromUtc AND v.CreatedAt<@toUtc) AS Conversations,
        (SELECT COUNT(*) FROM Conversations v WHERE {V} AND v.IsTest=0 AND v.CreatedAt>=@fromUtc AND v.CreatedAt<@toUtc AND v.LastInboundAt IS NOT NULL) AS Replied,
        (SELECT COUNT(*) FROM Conversations v WHERE {V} AND v.IsTest=0 AND v.CreatedAt>=@fromUtc AND v.CreatedAt<@toUtc AND v.Outcome='obiettivo_raggiunto') AS Reached,
        (SELECT COUNT(*) FROM Conversations v WHERE {V} AND v.IsTest=0 AND v.CreatedAt>=@fromUtc AND v.CreatedAt<@toUtc AND v.Outcome='rifiuto') AS Refused,
        (SELECT COUNT(*) FROM Conversations v WHERE {V} AND v.IsTest=0 AND v.CreatedAt>=@fromUtc AND v.CreatedAt<@toUtc AND v.Outcome='opt_out') AS OptOut,
        (SELECT COUNT(*) FROM Conversations v WHERE {V} AND v.IsTest=0 AND v.CreatedAt>=@fromUtc AND v.CreatedAt<@toUtc AND v.LastInboundAt IS NOT NULL AND v.HumanInvolved=0) AS AiOnly,
        (SELECT COUNT(*) FROM Conversations v WHERE {V} AND v.IsTest=0 AND v.CreatedAt>=@fromUtc AND v.CreatedAt<@toUtc AND v.HumanInvolved=1) AS WithOperator,
        (SELECT COALESCE(SUM(a.CostUsd),0) FROM AiUsage a WHERE {A} AND a.CreatedAt>=@fromUtc AND a.CreatedAt<@toUtc) AS AiCost";

    private static ReportRow Map(DbDataReader r, string kind)
    {
        int I(string n) => Convert.ToInt32(r.GetValue(r.GetOrdinal(n)));
        return new ReportRow
        {
            Kind = kind, Id = I("Id"), Name = r.Str("Name")!,
            SentMarketing = I("SentMarketing"), SentUtility = I("SentUtility"), Delivered = I("Delivered"), Read = I("ReadCount"),
            Conversations = I("Conversations"), Replied = I("Replied"), Reached = I("Reached"), Refused = I("Refused"), OptOut = I("OptOut"),
            AiOnly = I("AiOnly"), WithOperator = I("WithOperator"), AiCostUsd = Convert.ToDecimal(r.GetValue(r.GetOrdinal("AiCost")))
        };
    }

    /// <summary>Una riga per attività visibile (eventualmente solo di un gruppo), con il gruppo di appartenenza.</summary>
    public async Task<List<(ReportRow Row, int OrgId, string OrgName, bool InGroup)>> ActivitiesAsync(Scope s, DateTime fromUtc, DateTime toUtc, int? orgId)
    {
        var metrics = Metrics.Replace("{M}", "m.GymId=g.Id").Replace("{V}", "v.GymId=g.Id").Replace("{A}", "a.GymId=g.Id");
        return await _db.QueryAsync(
            $@"SELECT g.Id, g.Name, g.OrganizationId, o.Name AS OrgName, o.IsGroup, {metrics}
               FROM Gyms g JOIN Organizations o ON o.Id=g.OrganizationId
               WHERE {ScopeWhere} AND (@FOrg=-1 OR g.OrganizationId=@FOrg) ORDER BY o.Name, g.Name",
            Args(s, fromUtc, toUtc, orgId),
            r => (Map(r, "attivita"), r.Int("OrganizationId"), r.Str("OrgName")!, r.Bool("IsGroup")));
    }

    /// <summary>Una riga per campagna di un'attività attiva nel periodo (inviata o con conversazioni nel periodo).</summary>
    public Task<List<ReportRow>> CampaignsAsync(Scope s, DateTime fromUtc, DateTime toUtc, int gymId)
    {
        var metrics = Metrics
            .Replace("{M}", "m.ConversationId IN (SELECT x.Id FROM Conversations x WHERE x.CampaignId=k.Id)")
            .Replace("{V}", "v.CampaignId=k.Id")
            .Replace("{A}", "a.ConversationId IN (SELECT x.Id FROM Conversations x WHERE x.CampaignId=k.Id)");
        return _db.QueryAsync(
            $@"SELECT * FROM (SELECT k.Id, k.Name, {metrics}
               FROM Campaigns k JOIN Gyms g ON g.Id=k.GymId
               WHERE {ScopeWhere} AND k.GymId=@FGym) x WHERE x.SentMarketing + x.SentUtility + x.Conversations > 0 ORDER BY x.Id DESC",
            Args(s, fromUtc, toUtc, null, gymId), r => Map(r, "campagna"));
    }

    /// <summary>
    /// Andamento giorno per giorno. Si conta per ora (UTC) nel database e si raggruppa per giorno italiano qui:
    /// così non servono le tabelle dei fusi orari di MySQL, che sugli hosting condivisi spesso mancano.
    /// </summary>
    public async Task<List<DayPoint>> DailyAsync(Scope s, DateTime fromUtc, DateTime toUtc, int? orgId, int? gymId)
    {
        var args = Args(s, fromUtc, toUtc, orgId, gymId);
        const string gymFilter = "(@FOrg=-1 OR g.OrganizationId=@FOrg) AND (@FGym=-1 OR g.Id=@FGym)";
        var sent = await _db.QueryAsync(
            $@"SELECT DATE_FORMAT(m.CreatedAt,'%Y-%m-%d %H:00:00') AS H, COUNT(*) FROM WaMessages m JOIN Gyms g ON g.Id=m.GymId
               WHERE {ScopeWhere} AND {gymFilter} AND m.Direction='out' AND m.Kind='template' AND m.Status<>'failed'
                 AND m.CreatedAt>=@fromUtc AND m.CreatedAt<@toUtc GROUP BY H", args, r => (r.GetString(0), Convert.ToInt32(r.GetValue(1))));
        // Messaggi scritti dai clienti nelle conversazioni vere (non di prova).
        var replied = await _db.QueryAsync(
            $@"SELECT DATE_FORMAT(m.CreatedAt,'%Y-%m-%d %H:00:00') AS H, COUNT(*) FROM WaMessages m
               JOIN Conversations v ON v.Id=m.ConversationId JOIN Gyms g ON g.Id=m.GymId
               WHERE {ScopeWhere} AND {gymFilter} AND m.Direction='in' AND v.IsTest=0 AND m.CreatedAt>=@fromUtc AND m.CreatedAt<@toUtc GROUP BY H",
            args, r => (r.GetString(0), Convert.ToInt32(r.GetValue(1))));
        var reached = await _db.QueryAsync(
            $@"SELECT DATE_FORMAT(v.LastMessageAt,'%Y-%m-%d %H:00:00') AS H, COUNT(*) FROM Conversations v JOIN Gyms g ON g.Id=v.GymId
               WHERE {ScopeWhere} AND {gymFilter} AND v.IsTest=0 AND v.Outcome='obiettivo_raggiunto'
                 AND v.LastMessageAt>=@fromUtc AND v.LastMessageAt<@toUtc GROUP BY H", args, r => (r.GetString(0), Convert.ToInt32(r.GetValue(1))));

        static DateTime Day(string h) => DateTime.SpecifyKind(DateTime.Parse(h, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc).ToRome().Date;
        var days = new SortedDictionary<DateTime, (int S, int R, int G)>();
        for (var d = fromUtc.ToRome().Date; d < toUtc.ToRome().Date || d == fromUtc.ToRome().Date; d = d.AddDays(1)) days[d] = (0, 0, 0);
        foreach (var (h, n) in sent) { var d = Day(h); days.TryGetValue(d, out var v); days[d] = (v.S + n, v.R, v.G); }
        foreach (var (h, n) in replied) { var d = Day(h); days.TryGetValue(d, out var v); days[d] = (v.S, v.R + n, v.G); }
        foreach (var (h, n) in reached) { var d = Day(h); days.TryGetValue(d, out var v); days[d] = (v.S, v.R, v.G + n); }
        return days.Select(kv => new DayPoint(kv.Key, kv.Value.S, kv.Value.R, kv.Value.G)).ToList();
    }
}
