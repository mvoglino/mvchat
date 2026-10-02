using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Data;
using MvChat.Web.Infrastructure;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Gyms;

/// <summary>
/// Creazione e modifica di un'attività. MVitalia sceglie se è un'attività singola o se fa parte di un gruppo;
/// l'amministratore di gruppo crea attività solo nel suo gruppo.
/// </summary>
public class EditModel : PageModel
{
    private readonly Repos _repos;
    public EditModel(Repos repos) => _repos = repos;

    [BindProperty] public GymInput Input { get; set; } = new();
    public List<Organization> Groups { get; private set; } = new();
    public Scope Me { get; private set; } = new();
    public bool IsNew => Input.Id is null;
    public Gym? Existing { get; private set; }

    public async Task<IActionResult> OnGetAsync(int? id, int? gruppo)
    {
        await LoadAsync();
        if (id is null)
        {
            Input.OrganizationId = Me.IsSuperAdmin ? (Groups.Any(g => g.Id == gruppo) ? gruppo : null) : Me.OrganizationId;
            return Page();
        }
        Existing = await _repos.GymAsync(Me, id.Value);
        if (Existing is null) return NotFound();
        Input = new GymInput { Id = Existing.Id, OrganizationId = Existing.InGroup ? Existing.OrganizationId : null, Name = Existing.Name, City = Existing.City,
            Address = Existing.Address, Phone = Existing.Phone, IsActive = Existing.IsActive, Sector = Existing.Sector };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        if (Input.Id is int eid)
        {
            Existing = await _repos.GymAsync(Me, eid);
            if (Existing is null) return NotFound();
            Input.OrganizationId = Existing.InGroup ? Existing.OrganizationId : null; // il gruppo di un'attività esistente non si cambia qui
        }
        else if (!Me.IsSuperAdmin) Input.OrganizationId = Me.OrganizationId; // l'amministratore di gruppo crea solo nel suo gruppo
        if (Input.OrganizationId is int org && !Groups.Any(g => g.Id == org)) ModelState.AddModelError("Input.OrganizationId", "Scegli un gruppo valido.");
        if (Input.OrganizationId is null && !Me.IsSuperAdmin) ModelState.AddModelError("Input.OrganizationId", "Scegli un gruppo valido.");
        var single = Input.OrganizationId is null;
        if (single && !Sectors.All.Any(s => s.Key == Input.Sector)) ModelState.AddModelError("Input.Sector", "Scegli il tipo di attività.");
        if (!ModelState.IsValid) return Page();

        int id; int orgId;
        var name = Input.Name.Trim();
        if (Existing is not null)
        {
            orgId = Existing.OrganizationId;
            id = await _repos.SaveGymAsync(Existing.Id, orgId, name, Input.City?.Trim(), Input.Address?.Trim(), Input.Phone?.Trim(), Input.IsActive);
        }
        else if (single)
        {
            var slug = Regex.Replace(Regex.Replace(name.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD), @"\p{Mn}", ""), "[^a-z0-9]+", "-").Trim('-');
            slug = (slug.Length > 45 ? slug[..45] : slug) + "-" + Guid.NewGuid().ToString("N")[..6];
            (orgId, id) = await _repos.CreateSingleActivityAsync(name, slug, Input.Sector!, Input.City?.Trim(), Input.Address?.Trim(), Input.Phone?.Trim(), Input.IsActive);
        }
        else
        {
            orgId = Input.OrganizationId!.Value;
            id = await _repos.SaveGymAsync(null, orgId, name, Input.City?.Trim(), Input.Address?.Trim(), Input.Phone?.Trim(), Input.IsActive);
        }
        await _repos.AuditAsync(Me, IsNew ? "gym.created" : "gym.updated", name + (IsNew && single ? " (attività singola)" : ""), HttpContext.Connection.RemoteIpAddress?.ToString(), orgId, id);
        TempData["Ok"] = IsNew ? "Attività creata: ora aggiungi il suo amministratore e completa dati e logo." : "Attività aggiornata.";
        return Redirect(IsNew ? $"/Attivita/{id}" : "/Gyms");
    }

    private async Task LoadAsync()
    {
        Me = User.Scope();
        Groups = (await _repos.OrganizationsAsync(Me)).Where(o => o.IsGroup).ToList();
    }

    public class GymInput
    {
        public int? Id { get; set; }
        /// <summary>Vuoto = attività singola (solo MVitalia può crearla).</summary>
        public int? OrganizationId { get; set; }
        public string? Sector { get; set; } = "palestra";
        [Required(ErrorMessage = "Indica il nome dell'attività."), StringLength(150)] public string Name { get; set; } = "";
        [StringLength(100)] public string? City { get; set; }
        [StringLength(250)] public string? Address { get; set; }
        [StringLength(40)] public string? Phone { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
