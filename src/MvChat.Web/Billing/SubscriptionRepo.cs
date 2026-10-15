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

/// <summary>Quanto spende un'attività in un mese: abbonamento mvchat + messaggi Meta (stima dal listino). L'AI è compresa nell'abbonamento.</summary>
public sealed record SubMonth(int Month, int GymId, decimal Fee, int Marketing, int Utility, decimal MetaEur)
{
    public decimal Total => Fee + MetaEur;
}

/// <summary>
/// Dati della pagina «Abbonamento e costi» e del riquadro in Panoramica.
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

    /// <summary>Per ogni attività e ogni mese indicato (mese italiano): canone e messaggi Meta a pagamento (primi messaggi e promemoria).</summary>
    public async Task<List<SubMonth>> MonthsAsync(IReadOnlyList<SubGym> gyms, int year, IEnumerable<int> months)
    {
        var list = new List<SubMonth>();
        if (gyms.Count == 0) return list;
        var meta = _config.Current.Meta; var defaultFee = _config.Current.Billing.DefaultMonthlyFeeEur;
        var ids = string.Join(",", gyms.Select(g => g.Id));
        foreach (var m in months)
        {
            var first = new DateTime(year, m, 1);
            var from = first.FromRome(); var to = first.AddMonths(1).FromRome();
            // Meta fattura i template (marketing o servizio); le risposte nelle 24 ore sono gratuite.
            var counts = (await _db.QueryAsync(
                @"SELECT m.GymId,
                         SUM(CASE WHEN EXISTS (SELECT 1 FROM WaTemplates t WHERE t.WaNumberId=m.WaNumberId AND t.Name=m.TemplateName AND t.Category='UTILITY') THEN 0 ELSE 1 END) AS Mk,
                         SUM(CASE WHEN EXISTS (SELECT 1 FROM WaTemplates t WHERE t.WaNumberId=m.WaNumberId AND t.Name=m.TemplateName AND t.Category='UTILITY') THEN 1 ELSE 0 END) AS Ut
                  FROM WaMessages m
                  WHERE FIND_IN_SET(m.GymId, @ids) AND m.Direction='out' AND m.Kind='template' AND m.Status<>'failed'
                    AND m.CreatedAt>=@from AND m.CreatedAt<@to
                  GROUP BY m.GymId", new { ids, from, to },
                r => (Gym: r.Int("GymId"), Mk: Convert.ToInt32(r.GetValue(1)), Ut: Convert.ToInt32(r.GetValue(2)))))
                .ToDictionary(x => x.Gym);
            foreach (var g in gyms)
            {
                var (_, mk, ut) = counts.GetValueOrDefault(g.Id);
                var metaEur = Math.Round(mk * meta.MarketingPriceEur + ut * meta.UtilityPriceEur, 2, MidpointRounding.AwayFromZero);
                list.Add(new SubMonth(m, g.Id, g.FeeIn(first, defaultFee), mk, ut, metaEur));
            }
        }
        return list;
    }

    /// <summary>Riquadro della Panoramica: canone e spesa Meta del mese in corso nel perimetro di chi guarda.</summary>
    public async Task<(decimal Fee, decimal Meta, int Activities)> CurrentAsync(Scope s)
    {
        var gyms = await GymsAsync(s);
        var now = DateTime.UtcNow.ToRome();
        var rows = await MonthsAsync(gyms, now.Year, new[] { now.Month });
        return (rows.Sum(r => r.Fee), rows.Sum(r => r.MetaEur), rows.Count(r => r.Fee > 0));
    }
}
