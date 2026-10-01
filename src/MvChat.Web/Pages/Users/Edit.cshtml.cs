using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Data;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Users;

public class EditModel : PageModel
{
    private readonly Repos _repos;
    private readonly PasswordService _pwd;
    public EditModel(Repos repos, PasswordService pwd) { _repos = repos; _pwd = pwd; }

    [BindProperty] public UserInput Input { get; set; } = new();
    [BindProperty] public bool ResetPassword { get; set; }
    public Scope Me { get; private set; } = new();
    public List<Organization> Orgs { get; private set; } = new();
    public List<Gym> Gyms { get; private set; } = new();
    public string[] AssignableRoles { get; private set; } = Array.Empty<string>();
    public bool IsNew => Input.Id is null;
    public bool IsSelf => Input.Id == Me.UserId;

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        await LoadAsync();
        if (id is null)
        {
            Input.Role = AssignableRoles.Contains(Roles.Operator) ? Roles.Operator : AssignableRoles.FirstOrDefault() ?? "";
            Input.OrganizationId = Me.OrganizationId;
            Input.GymId = Me.GymId;
            return Page();
        }
        var u = await _repos.UserAsync(Me, id.Value);
        if (u is null || (!AssignableRoles.Contains(u.Role) && u.Id != Me.UserId)) return NotFound();
        Input = new UserInput { Id = u.Id, FullName = u.FullName, Email = u.Email, Role = u.Role, OrganizationId = u.OrganizationId, GymId = u.GymId, IsActive = u.IsActive };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        UserRow? existing = null;
        if (Input.Id is int uid)
        {
            existing = await _repos.UserAsync(Me, uid);
            if (existing is null || (!AssignableRoles.Contains(existing.Role) && existing.Id != Me.UserId)) return NotFound();
        }

        // Su se stessi non si cambiano ruolo, catena, palestra né stato: si evita di chiudersi fuori.
        if (existing is not null && existing.Id == Me.UserId)
        {
            Input.Role = existing.Role; Input.OrganizationId = existing.OrganizationId; Input.GymId = existing.GymId; Input.IsActive = true;
        }
        else if (!AssignableRoles.Contains(Input.Role))
            ModelState.AddModelError("Input.Role", "Non puoi assegnare questo ruolo.");

        // Ogni ruolo ha il suo perimetro: catena e palestra si ricavano dalle regole, non da quello che arriva dal modulo.
        switch (Input.Role)
        {
            case Roles.SuperAdmin:
                Input.OrganizationId = null; Input.GymId = null; break;
            case Roles.OrgAdmin:
                Input.GymId = null;
                if (!Me.IsSuperAdmin) Input.OrganizationId = Me.OrganizationId;
                if (Input.OrganizationId is null || !Orgs.Any(o => o.Id == Input.OrganizationId))
                    ModelState.AddModelError("Input.OrganizationId", "Scegli la catena.");
                break;
            default:
                var gym = Gyms.FirstOrDefault(g => g.Id == Input.GymId);
                if (gym is null) ModelState.AddModelError("Input.GymId", "Scegli la palestra.");
                else Input.OrganizationId = gym.OrganizationId;
                break;
        }

        Input.Email = (Input.Email ?? "").Trim().ToLowerInvariant();
        if (await _repos.EmailTakenAsync(Input.Email, Input.Id))
            ModelState.AddModelError("Input.Email", "Questa email è già usata da un altro utente.");
        if (!ModelState.IsValid) return Page();

        string? temp = null;
        if (IsNew || ResetPassword) temp = PasswordService.Generate();
        var id = await _repos.SaveUserAsync(Input.Id, Input.OrganizationId, Input.GymId, Input.Email, Input.FullName.Trim(), Input.Role, Input.IsActive, temp is null ? null : _pwd.Hash(temp));
        await _repos.AuditAsync(Me, IsNew ? "user.created" : (ResetPassword ? "user.password_reset" : "user.updated"), $"{Input.Email} ({Input.Role})",
            HttpContext.Connection.RemoteIpAddress?.ToString(), Input.OrganizationId, Input.GymId);

        if (temp is not null) { TempData["TempPassword"] = temp; TempData["TempFor"] = Input.FullName; }
        TempData["Ok"] = IsNew ? "Utente creato." : "Utente aggiornato.";
        return Redirect("/Users");
    }

    private async Task LoadAsync()
    {
        Me = User.Scope();
        AssignableRoles = Roles.Assignable(Me.Role);
        Orgs = await _repos.OrganizationsAsync(Me);
        Gyms = (await _repos.GymsAsync(Me)).Where(g => g.IsActive).ToList();
    }

    public class UserInput
    {
        public int? Id { get; set; }
        [Required(ErrorMessage = "Indica nome e cognome."), StringLength(150)] public string FullName { get; set; } = "";
        [Required(ErrorMessage = "Indica l'email."), EmailAddress(ErrorMessage = "Email non valida."), StringLength(200)] public string Email { get; set; } = "";
        public string Role { get; set; } = "";
        public int? OrganizationId { get; set; }
        public int? GymId { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
