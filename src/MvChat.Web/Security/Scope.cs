using System.Security.Claims;

namespace MvChat.Web.Security;

public static class Roles
{
    public const string SuperAdmin = "superadmin"; // MVitalia: vede tutte le strutture
    public const string OrgAdmin = "orgadmin";     // direzione di una struttura: tutte le sue sedi
    public const string Manager = "manager";       // responsabile di una sede
    public const string Operator = "operator";     // reception: solo le conversazioni della sua sede

    public static readonly string[] All = { SuperAdmin, OrgAdmin, Manager, Operator };

    public static string Label(string role) => role switch
    {
        SuperAdmin => "Amministratore MVitalia",
        OrgAdmin => "Direzione struttura",
        Manager => "Responsabile sede",
        Operator => "Operatore",
        _ => role
    };

    /// <summary>Quali ruoli può assegnare chi ha questo ruolo.</summary>
    public static string[] Assignable(string role) => role switch
    {
        SuperAdmin => All,
        OrgAdmin => new[] { OrgAdmin, Manager, Operator },
        Manager => new[] { Operator },
        _ => Array.Empty<string>()
    };
}

/// <summary>
/// Chi sta usando il software e fin dove può vedere. Ogni query sui dati di lavoro
/// passa da qui: è la regola che separa le strutture e le sedi tra loro.
/// </summary>
public sealed class Scope
{
    public int UserId { get; init; }
    public string Role { get; init; } = "";
    public string Name { get; init; } = "";
    public int? OrganizationId { get; init; }
    public int? GymId { get; init; }

    public bool IsSuperAdmin => Role == Roles.SuperAdmin;
    public bool IsOrgAdmin => Role == Roles.OrgAdmin;
    public bool IsManager => Role == Roles.Manager;
    public bool CanManageGyms => IsSuperAdmin || IsOrgAdmin;
    public bool CanManageUsers => IsSuperAdmin || IsOrgAdmin || IsManager;

    public bool CanSeeOrganization(int orgId) => IsSuperAdmin || OrganizationId == orgId;
    public bool CanSeeGym(int orgId, int gymId) => IsSuperAdmin || (IsOrgAdmin && OrganizationId == orgId) || GymId == gymId;

    public static Scope From(ClaimsPrincipal p) => new()
    {
        UserId = int.TryParse(p.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0,
        Role = p.FindFirstValue(ClaimTypes.Role) ?? "",
        Name = p.FindFirstValue(ClaimTypes.Name) ?? "",
        OrganizationId = int.TryParse(p.FindFirstValue("org"), out var o) ? o : null,
        GymId = int.TryParse(p.FindFirstValue("gym"), out var g) ? g : null,
    };
}

public static class ScopeExtensions
{
    public static Scope Scope(this ClaimsPrincipal p) => Security.Scope.From(p);
}
