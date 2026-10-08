using System.Security.Claims;

namespace MvChat.Web.Security;

public static class Roles
{
    public const string SuperAdmin = "superadmin"; // MVitalia: vede tutti i gruppi
    public const string OrgAdmin = "orgadmin";     // amministratore di gruppo (marchio): tutte le attività del gruppo
    public const string AreaManager = "areamanager"; // responsabile di area: solo alcune attività del gruppo, assegnate in UserGyms
    public const string Manager = "manager";       // amministratore di una singola attività: la sua attività e i suoi operatori
    public const string Operator = "operator";     // reception: solo le conversazioni della sua attività

    public static readonly string[] All = { SuperAdmin, OrgAdmin, AreaManager, Manager, Operator };

    public static string Label(string role) => role switch
    {
        SuperAdmin => "Amministratore MVitalia",
        OrgAdmin => "Amministratore di gruppo",
        AreaManager => "Responsabile di area",
        Manager => "Amministratore attività",
        Operator => "Operatore",
        _ => role
    };

    /// <summary>Quali ruoli può assegnare chi ha questo ruolo.</summary>
    public static string[] Assignable(string role) => role switch
    {
        SuperAdmin => All,
        OrgAdmin => new[] { OrgAdmin, AreaManager, Manager, Operator },
        AreaManager => new[] { Manager, Operator },
        Manager => new[] { Operator },
        _ => Array.Empty<string>()
    };
}

/// <summary>
/// Chi sta usando il software e fin dove può vedere. Ogni query sui dati di lavoro
/// passa da qui: è la regola che separa i gruppi e le attività tra loro.
/// </summary>
public sealed class Scope
{
    public int UserId { get; init; }
    public string Role { get; init; } = "";
    public string Name { get; init; } = "";
    public int? OrganizationId { get; init; }
    public int? GymId { get; init; }
    /// <summary>Solo per il responsabile di area: le sue attività, separate da virgole (vuoto per tutti gli altri). Si usa nelle query con FIND_IN_SET.</summary>
    public string AreaGymsCsv { get; init; } = "";
    public IReadOnlyList<int> AreaGymIds => AreaGymsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();

    public bool IsSuperAdmin => Role == Roles.SuperAdmin;
    public bool IsOrgAdmin => Role == Roles.OrgAdmin;
    public bool IsManager => Role == Roles.Manager;
    public bool IsAreaManager => Role == Roles.AreaManager;
    public bool CanManageGyms => IsSuperAdmin || IsOrgAdmin;
    public bool CanManageUsers => IsSuperAdmin || IsOrgAdmin || IsAreaManager || IsManager;

    public bool CanSeeOrganization(int orgId) => IsSuperAdmin || OrganizationId == orgId;
    public bool CanSeeGym(int orgId, int gymId) => IsSuperAdmin || (IsOrgAdmin && OrganizationId == orgId) || GymId == gymId || (IsAreaManager && AreaGymIds.Contains(gymId));

    public static Scope From(ClaimsPrincipal p) => new()
    {
        UserId = int.TryParse(p.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0,
        Role = p.FindFirstValue(ClaimTypes.Role) ?? "",
        Name = p.FindFirstValue(ClaimTypes.Name) ?? "",
        OrganizationId = int.TryParse(p.FindFirstValue("org"), out var o) ? o : null,
        GymId = int.TryParse(p.FindFirstValue("gym"), out var g) ? g : null,
        // Solo numeri: il valore finisce in una query (come parametro), ma meglio non fidarsi nemmeno del contenuto del cookie.
        AreaGymsCsv = p.FindFirstValue(ClaimTypes.Role) == Roles.AreaManager
            ? string.Join(",", (p.FindFirstValue("gyms") ?? "").Split(',').Where(x => int.TryParse(x, out _)))
            : "",
    };
}

public static class ScopeExtensions
{
    public static Scope Scope(this ClaimsPrincipal p) => Security.Scope.From(p);
}
