using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Data;
using MvChat.Web.Infrastructure;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Orgs;

/// <summary>Abilitazione di una struttura da parte di MVitalia. Logo e dati dell'attività stanno nella pagina «Dati e logo».</summary>
public class EditModel : PageModel
{
    private readonly Repos _repos;
    public EditModel(Repos repos) => _repos = repos;

    [BindProperty] public OrgInput Input { get; set; } = new();
    public bool IsNew => Input.Id is null;

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is null) return Page();
        var o = await _repos.OrganizationAsync(User.Scope(), id.Value);
        if (o is null) return NotFound();
        Input = new OrgInput { Id = o.Id, Name = o.Name, Slug = o.Slug, Sector = o.Sector, BillingEmail = o.BillingEmail, IsActive = o.IsActive };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Input.Slug = Slugify(string.IsNullOrWhiteSpace(Input.Slug) ? Input.Name : Input.Slug);
        if (!Sectors.All.Any(s => s.Key == Input.Sector)) ModelState.AddModelError("Input.Sector", "Scegli il tipo di attività.");
        if (!ModelState.IsValid) return Page();
        int id;
        try
        {
            id = await _repos.SaveOrganizationAsync(Input.Id, Input.Name.Trim(), Input.Slug, Input.Sector, Input.BillingEmail?.Trim(), Input.IsActive);
            await _repos.AuditAsync(User.Scope(), IsNew ? "org.created" : "org.updated", $"{Input.Name} ({Input.Sector})", HttpContext.Connection.RemoteIpAddress?.ToString(), id);
        }
        catch (Exception ex) when (ex.Message.Contains("Duplicate", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError("Input.Slug", "Questo codice è già usato da un'altra struttura.");
            return Page();
        }
        TempData["Ok"] = IsNew ? "Struttura abilitata: ora completa logo e dati dell'attività." : "Struttura aggiornata.";
        return Redirect(IsNew ? $"/Struttura/{id}" : "/Orgs");
    }

    private static string Slugify(string s) =>
        Regex.Replace(Regex.Replace(s.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD), @"\p{Mn}", ""), "[^a-z0-9]+", "-").Trim('-');

    public class OrgInput
    {
        public int? Id { get; set; }
        [Required(ErrorMessage = "Indica il nome della struttura."), StringLength(150)] public string Name { get; set; } = "";
        [StringLength(60)] public string Slug { get; set; } = "";
        public string Sector { get; set; } = "palestra";
        [EmailAddress(ErrorMessage = "Email non valida.")] public string? BillingEmail { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
