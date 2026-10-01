using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Data;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Orgs;

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
        Input = new OrgInput { Id = o.Id, Name = o.Name, Slug = o.Slug, LogoUrl = o.LogoUrl, PrimaryColor = o.PrimaryColor, VatNumber = o.VatNumber, BillingEmail = o.BillingEmail, IsActive = o.IsActive };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Input.Slug = Slugify(string.IsNullOrWhiteSpace(Input.Slug) ? Input.Name : Input.Slug);
        if (!Regex.IsMatch(Input.PrimaryColor ?? "", "^#[0-9A-Fa-f]{6}$"))
            ModelState.AddModelError("Input.PrimaryColor", "Usa un colore nel formato #RRGGBB.");
        if (!ModelState.IsValid) return Page();
        try
        {
            var id = await _repos.SaveOrganizationAsync(Input.Id, Input.Name.Trim(), Input.Slug, Input.LogoUrl?.Trim(), Input.PrimaryColor!, Input.VatNumber?.Trim(), Input.BillingEmail?.Trim(), Input.IsActive);
            await _repos.AuditAsync(User.Scope(), IsNew ? "org.created" : "org.updated", Input.Name, HttpContext.Connection.RemoteIpAddress?.ToString(), id);
        }
        catch (Exception ex) when (ex.Message.Contains("Duplicate", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError("Input.Slug", "Questo codice è già usato da un'altra catena.");
            return Page();
        }
        TempData["Ok"] = IsNew ? "Catena creata." : "Catena aggiornata.";
        return Redirect("/Orgs");
    }

    private static string Slugify(string s) =>
        Regex.Replace(Regex.Replace(s.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD), @"\p{Mn}", ""), "[^a-z0-9]+", "-").Trim('-');

    public class OrgInput
    {
        public int? Id { get; set; }
        [Required(ErrorMessage = "Indica il nome della catena."), StringLength(150)] public string Name { get; set; } = "";
        [StringLength(60)] public string Slug { get; set; } = "";
        [Url(ErrorMessage = "Indirizzo del logo non valido."), StringLength(400)] public string? LogoUrl { get; set; }
        public string? PrimaryColor { get; set; } = "#F6931E";
        [StringLength(20)] public string? VatNumber { get; set; }
        [EmailAddress(ErrorMessage = "Email non valida.")] public string? BillingEmail { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
