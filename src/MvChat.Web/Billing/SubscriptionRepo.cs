using MvChat.Web.Infrastructure;
using MvChat.Web.Security;

namespace MvChat.Web.Billing;

/// <summary>Un'attività con il suo abbonamento mvchat (canone mensile e periodo).</summary>
public sealed record SubGym(int Id, string Name, bool Active, int OrgId, string OrgName, bool InGroup, decimal? Fee, DateTime? From, DateTime? To)
{
    /// <summary>Canone del mese: attività attiva e abbonamento valido almeno un giorno del mese (stessa regola dei rendiconti).</summary>
    public decimal FeeIn(DateTime firstOfMonth, decimal defaultFee)
    {
        var last = firstOfMonth.AddMonths(1).AddDays(-1);
        var on = Active && From is DateTime f && f.Date <= last && (To is null || To.Value.Date >= firstOfMonth);
        return on ? Math.Round(Fee ?? defaultFee, 2, MidpointRounding.AwayFromZero) : 0m;
    }
}

/// <summary>
/// Costi e consumi di un'attività in un mese: abbonamento mvchat, messaggi Meta (stima dal listino), risposte dell'assistente.
/// Closed = valori fissati alla chiusura del mese (storico); altrimenti calcolati adesso.
/// AiUsd è il costo AI di MVitalia: alle attività non si mostra (è compreso nell'abbonamento).
/// </summary>
public sealed record SubMonth(int Month, int GymId, decimal Fee, int Marketing, int Utility, decimal MetaEur, int AiReplies, decimal AiUsd, bool Closed)
{
    public decimal Total => Fee + MetaEur;
    public int Messages => Marketing + Utility;
}

/// <summary>
/// Dati della pagina «Abbonamento e costi», del riquadro in Panoramica e dello storico fissato alla chiusura del mese.
/// Il perimetro è quello di chi guarda (gruppo, attività del responsabile di area, attività singola).
/// </summary>
public sealed class SubscriptionRepo
{
    private readonly Db _db; private readonly AppConfigStore _config;
    public SubscriptionRepo(Db db, AppConfigStore config) { _db = db; _config = config; }

    private const string ScopeWhere = "(@All=1 OR (@IsOrg=1 AND g.OrganizationId=@Org) OR g.Id=@Gym OR FIND_IN_SET(g.Id, @Gyms))";
    private static object ScopeArgs(Scope s, int? orgId, int? gymId) => new
    {
        All = s.IsSuperAdmin ? 1 : 0, IsOrg = s.IsOrgAdmin ? 1 : 0, Org = s.OrganizationId ?? -1, Gym = s.GymId ?? -1, Gyms = s.AreaGymsCsv,
        FOrg = orgId ?? -1, FGym = gymId ?? -1
    };

    public Task<List<SubGym>> GymsAsync(Scope s, int? orgId = null, int? gymId = null) => _db.QueryAsync(
        $@"SELECT g.Id, g.Name, g.IsActive, g.OrganizationId, o.Name AS OrgName, o.IsGroup, g.MonthlyFeeEur, g.FeeStartsOn, g.FeeEndsOn
           FROM Gyms g JOIN Organizations o ON o.Id=g.OrganizationId
           WHERE {ScopeWhere} AND (@FOrg=-1 OR g.OrganizationId=@FOrg) AND (@FGym=-1 OR g.Id=@FGym)
           ORDER BY o.Name, g.Name", ScopeArgs(s, orgId, gymId),
        r => new SubGym(r.Int("Id"), r.Str("Name")!, r.Bool("IsActive"), r.Int("OrganizationId"), r.Str("OrgName")!, r.Bool("IsGroup"),
            r.IsDBNull(r.GetOrdinal("MonthlyFeeEur")) ? null : r.GetDecimal(r.GetOrdinal("MonthlyFeeEur")), r.Date("FeeStartsOn"), r.Date("FeeEndsOn")));

    /// <summary>
    /// Per ogni attività e ogni mese indicato (mese italiano): i valori fissati alla chiusura se il mese è chiuso,
    /// altrimenti calcolati adesso (canone, messaggi Meta a pagamento, risposte e costo dell'assistente).
    /// </summary>
    public async Task<List<SubMonth>> MonthsAsync(IReadOnlyList<SubGym> gyms, int year, IEnumerable<int> months, bool liveOnly = false)
    {
        var list = new List<SubMonth>();
        if (gyms.Count == 0) return list;
        var ids = string.Join(",", gyms.Select(g => g.Id));
        var monthList = months.ToList();
        var keys = monthList.Select(m => $"{year:D4}-{m:D2}").ToList();
        var history = liveOnly ? new() : (await _db.QueryAsync(
            "SELECT * FROM CostHistory WHERE FIND_IN_SET(GymId, @ids) AND FIND_IN_SET(Month, @keys)", new { ids, keys = string.Join(",", keys) },
            r => new SubMonth(int.Parse(r.Str("Month")![5..]), r.Int("GymId"), r.GetDecimal(r.GetOrdinal("FeeEur")), r.Int("MarketingMsgs"), r.Int("UtilityMsgs"),
                r.GetDecimal(r.GetOrdinal("MetaEur")), r.Int("AiReplies"), r.GetDecimal(r.GetOrdinal("AiUsd")), true)))
            .ToDictionary(x => (x.Month, x.GymId));

        var meta = _config.Current.Meta; var defaultFee = _config.Current.Billing.DefaultMonthlyFeeEur;
        foreach (var m in monthList)
        {
            var missing = gyms.Where(g => !history.ContainsKey((m, g.Id))).ToList();
            foreach (var g in gyms.Where(g => history.ContainsKey((m, g.Id)))) list.Add(history[(m, g.Id)]);
            if (missing.Count == 0) continue;
            var first = new DateTime(year, m, 1);
            var from = first.FromRome(); var to = first.AddMonths(1).FromRome();
            var mids = string.Join(",", missing.Select(g => g.Id));
            // Meta fattura i template (marketing o servizio); le risposte entro 24 ore sono gratuite.
            var counts = (await _db.QueryAsync(
                @"SELECT m.GymId,
                         SUM(CASE WHEN EXISTS (SELECT 1 FROM WaTemplates t WHERE t.WaNumberId=m.WaNumberId AND t.Name=m.TemplateName AND t.Category='UTILITY') THEN 0 ELSE 1 END) AS Mk,
                         SUM(CASE WHEN EXISTS (SELECT 1 FROM WaTemplates t WHERE t.WaNumberId=m.WaNumberId AND t.Name=m.TemplateName AND t.Category='UTILITY') THEN 1 ELSE 0 END) AS Ut
                  FROM WaMessages m
                  WHERE FIND_IN_SET(m.GymId, @mids) AND m.Direction='out' AND m.Kind='template' AND m.Status<>'failed'
                    AND m.CreatedAt>=@from AND m.CreatedAt<@to
                  GROUP BY m.GymId", new { mids, from, to },
                r => (Gym: r.Int("GymId"), Mk: Convert.ToInt32(r.GetValue(1)), Ut: Convert.ToInt32(r.GetValue(2)))))
                .ToDictionary(x => x.Gym);
            var ai = (await _db.QueryAsync(
                @"SELECT a.GymId, COALESCE(SUM(a.CostUsd),0) AS Usd, SUM(CASE WHEN a.Ok=1 AND a.Purpose='chat' THEN 1 ELSE 0 END) AS Replies
                  FROM AiUsage a WHERE FIND_IN_SET(a.GymId, @mids) AND a.CreatedAt>=@from AND a.CreatedAt<@to GROUP BY a.GymId", new { mids, from, to },
                r => (Gym: r.Int("GymId"), Usd: Convert.ToDecimal(r.GetValue(1)), Replies: Convert.ToInt32(r.GetValue(2)))))
                .ToDictionary(x => x.Gym);
            foreach (var g in missing)
            {
                var (_, mk, ut) = counts.GetValueOrDefault(g.Id);
                var (_, usd, replies) = ai.GetValueOrDefault(g.Id);
                var metaEur = Math.Round(mk * meta.MarketingPriceEur + ut * meta.UtilityPriceEur, 2, MidpointRounding.AwayFromZero);
                list.Add(new SubMonth(m, g.Id, g.FeeIn(first, defaultFee), mk, ut, metaEur, replies, Math.Round(usd, 6), false));
            }
        }
        return list.OrderBy(x => x.Month).ToList();
    }

    /// <summary>
    /// Alla chiusura del mese in Fatturazione: fissa costi e consumi di tutte le attività in quel mese.
    /// Una riga già fissata non si cambia (anche se poi un rendiconto viene riaperto e richiuso).
    /// </summary>
    public async Task<int> SnapshotAsync(Scope superAdmin, string month, int userId)
    {
        var year = int.Parse(month[..4]); var m = int.Parse(month[5..]);
        var gyms = await GymsAsync(superAdmin);
        var rows = await MonthsAsync(gyms, year, new[] { m }, liveOnly: true);
        var meta = _config.Current.Meta; var n = 0;
        foreach (var r in rows)
        {
            var g = gyms.First(x => x.Id == r.GymId);
            if (r.Fee == 0 && r.Messages == 0 && r.AiReplies == 0 && r.AiUsd == 0) continue; // niente da ricordare
            n += await _db.ExecuteAsync(
                @"INSERT IGNORE INTO CostHistory (Month, GymId, OrganizationId, FeeEur, MarketingMsgs, UtilityMsgs, MarketingPriceEur, UtilityPriceEur, MetaEur, AiReplies, AiUsd, ClosedBy)
                  VALUES (@month, @GymId, @OrgId, @Fee, @Marketing, @Utility, @mp, @up, @MetaEur, @AiReplies, @AiUsd, @userId)",
                new { month, r.GymId, g.OrgId, r.Fee, r.Marketing, r.Utility, mp = meta.MarketingPriceEur, up = meta.UtilityPriceEur, r.MetaEur, r.AiReplies, r.AiUsd, userId });
        }
        return n;
    }

    /// <summary>L'ultimo mese chiuso (storico fissato): il contatore della spesa Meta riparte dal mese dopo.</summary>
    public async Task<string?> LastClosedMonthAsync(IReadOnlyList<SubGym> gyms) => gyms.Count == 0 ? null :
        await _db.ScalarAsync<string?>("SELECT MAX(Month) FROM CostHistory WHERE FIND_IN_SET(GymId, @ids)", new { ids = string.Join(",", gyms.Select(g => g.Id)) });

    /// <summary>
    /// Spesa da quando il contatore è stato azzerato (mesi dopo l'ultima chiusura, fino a oggi):
    /// abbonamento, spesa Meta stimata e messaggi, nel perimetro di chi guarda.
    /// </summary>
    public async Task<(decimal Fee, decimal Meta, int Messages, DateTime Since)> SinceLastCloseAsync(IReadOnlyList<SubGym> gyms)
    {
        var now = DateTime.UtcNow.ToRome();
        var last = await LastClosedMonthAsync(gyms);
        var since = last is { Length: 7 } ? new DateTime(int.Parse(last[..4]), int.Parse(last[5..]), 1).AddMonths(1) : new DateTime(now.Year, now.Month, 1);
        if (since > now) since = new DateTime(now.Year, now.Month, 1);
        decimal fee = 0, meta = 0; int msgs = 0;
        for (var d = since; d <= now; d = d.AddMonths(1))
        {
            var rows = await MonthsAsync(gyms, d.Year, new[] { d.Month }, liveOnly: true);
            fee += rows.Sum(r => r.Fee); meta += rows.Sum(r => r.MetaEur); msgs += rows.Sum(r => r.Messages);
        }
        return (fee, meta, msgs, since);
    }

    /// <summary>Riquadro della Panoramica: abbonamento del mese e spesa Meta dall'ultima chiusura.</summary>
    public async Task<(decimal Fee, decimal Meta, DateTime Since)> CurrentAsync(Scope s)
    {
        var gyms = await GymsAsync(s);
        var now = DateTime.UtcNow.ToRome();
        var month = await MonthsAsync(gyms, now.Year, new[] { now.Month }, liveOnly: true);
        var since = await SinceLastCloseAsync(gyms);
        return (month.Sum(r => r.Fee), since.Meta, since.Since);
    }
}
