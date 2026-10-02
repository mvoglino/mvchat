using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Data;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Gyms;

public class EditModel : PageModel
{
    private readonly Repos _repos;
    public EditModel(Repos repos) => _repos = repos;

    [BindProperty] public GymInput Input { get; set; } = new();
    public List<Organization> Orgs { get; private set; } = new();
    public Scope Me { get; private set; } = new();
    public bool IsNew => Input.Id is null;

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        await LoadAsync();
        if (id is null)
        {
            Input.OrganizationId = Me.OrganizationId ?? Orgs.FirstOrDefault()?.Id ?? 0;
            return Page();
        }
        var g = await _repos.GymAsync(Me, id.Value);
        if (g is null) return NotFound();
        Input = new GymInput { Id = g.Id, OrganizationId = g.OrganizationId, Name = g.Name, City = g.City, Address = g.Address, Phone = g.Phone, IsActive = g.IsActive };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        // La struttura non si sceglie liberamente: chi non è MVitalia lavora sempre nella propria.
        if (!Me.IsSuperAdmin) Input.OrganizationId = Me.OrganizationId ?? 0;
        if (!Orgs.Any(o => o.Id == Input.OrganizationId)) ModelState.AddModelError("Input.OrganizationId", "Scegli una struttura valida.");
        if (Input.Id is int existing && await _repos.GymAsync(Me, existing) is null) return NotFound();
        if (!ModelState.IsValid) return Page();

        var id = await _repos.SaveGymAsync(Input.Id, Input.OrganizationId, Input.Name.Trim(), Input.City?.Trim(), Input.Address?.Trim(), Input.Phone?.Trim(), Input.IsActive);
        await _repos.AuditAsync(Me, IsNew ? "gym.created" : "gym.updated", Input.Name, HttpContext.Connection.RemoteIpAddress?.ToString(), Input.OrganizationId, id);
        TempData["Ok"] = IsNew ? "Sede creata." : "Sede aggiornata.";
        return Redirect("/Gyms");
    }

    private async Task LoadAsync() { Me = User.Scope(); Orgs = await _repos.OrganizationsAsync(Me); }

    public class GymInput
    {
        public int? Id { get; set; }
        public int OrganizationId { get; set; }
        [Required(ErrorMessage = "Indica il nome della sede."), StringLength(150)] public string Name { get; set; } = "";
        [StringLength(100)] public string? City { get; set; }
        [StringLength(250)] public string? Address { get; set; }
        [StringLength(40)] public string? Phone { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
