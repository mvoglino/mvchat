using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Data;
using MvChat.Web.Infrastructure;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Attivita;

/// <summary>
/// Nome, logo, colore e dati propri di un'attività (ragione sociale, P.IVA, presentazione).
/// La modificano MVitalia, l'amministratore del suo gruppo e il suo amministratore.
/// </summary>
public class IndexModel : PageModel
{
    private readonly Repos _repos;
    public IndexModel(Repos repos) => _repos = repos;

    public ActivityProfile P { get; private set; } = new();
    public Scope Me { get; private set; } = new();
    /// <summary>Il tipo di attività lo decide MVitalia; dentro un gruppo anche l'amministratore del gruppo.</summary>
    public bool CanChangeSector => Me.IsSuperAdmin || (Me.IsOrgAdmin && P.InGroup);
    [BindProperty] public ProfileInput Input { get; set; } = new();
    [BindProperty] public IFormFile? Logo { get; set; }
    [BindProperty] public bool RemoveLogo { get; set; }

    private async Task<bool> LoadAsync(int? id)
    {
        Me = User.Scope();
        var gymId = id ?? Me.GymId;
        if (gymId is null || await _repos.GymAsync(Me, gymId.Value) is null) return false; // perimetro: solo attività visibili a chi chiede
        var p = await _repos.ActivityProfileAsync(gymId.Value);
        if (p is null) return false;
        P = p;
        return true;
    }

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (!await LoadAsync(id)) return NotFound();
        Input = new ProfileInput
        {
            Name = P.Name, Sector = P.InGroup ? P.Sector ?? "" : P.GroupSector, PrimaryColor = P.PrimaryColor, LegalName = P.LegalName, VatNumber = P.VatNumber,
            Address = P.Address, City = P.City, Phone = P.Phone, ContactEmail = P.ContactEmail, Website = P.Website, Description = P.Description
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!string.IsNullOrEmpty(Input.PrimaryColor) && !Regex.IsMatch(Input.PrimaryColor, "^#[0-9A-Fa-f]{6}$")) ModelState.AddModelError("Input.PrimaryColor", "Usa un colore nel formato #RRGGBB.");
        if (CanChangeSector && !string.IsNullOrEmpty(Input.Sector) && !Sectors.All.Any(s => s.Key == Input.Sector)) ModelState.AddModelError("Input.Sector", "Scegli il tipo di attività.");
        if (!string.IsNullOrWhiteSpace(Input.Website) && !Uri.TryCreate(Input.Website.Trim().StartsWith("http") ? Input.Website.Trim() : "https://" + Input.Website.Trim(), UriKind.Absolute, out _))
            ModelState.AddModelError("Input.Website", "Indirizzo del sito non valido.");
        var (logo, logoType, logoError) = await Gruppo.IndexModel.ReadLogoAsync(Logo);
        if (logoError is not null) ModelState.AddModelError("Logo", logoError);
        if (!ModelState.IsValid) return Page();

        P.Name = Input.Name.Trim(); P.PrimaryColor = T(Input.PrimaryColor);
        P.Sector = string.IsNullOrEmpty(Input.Sector) ? null : Input.Sector; // vuoto = come il gruppo
        P.LegalName = T(Input.LegalName); P.VatNumber = T(Input.VatNumber); P.Address = T(Input.Address); P.City = T(Input.City); P.Phone = T(Input.Phone);
        P.ContactEmail = T(Input.ContactEmail); P.Website = T(Input.Website); P.Description = T(Input.Description);
        await _repos.SaveActivityProfileAsync(P, CanChangeSector);
        if (logo is not null) await _repos.SaveActivityLogoAsync(P.Id, logo, logoType);
        else if (RemoveLogo) await _repos.SaveActivityLogoAsync(P.Id, null, null);
        await _repos.AuditAsync(Me, "gym.profile", P.Name + (logo is not null ? " · nuovo logo" : ""), HttpContext.Connection.RemoteIpAddress?.ToString(), P.OrganizationId, P.Id);
        TempData["Ok"] = "Dati dell'attività salvati.";
        return Redirect($"/Attivita/{P.Id}");
    }

    private static string? T(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public class ProfileInput
    {
        [Required(ErrorMessage = "Indica il nome."), StringLength(150)] public string Name { get; set; } = "";
        public string? Sector { get; set; }
        public string? PrimaryColor { get; set; }
        [StringLength(200)] public string? LegalName { get; set; }
        [StringLength(20)] public string? VatNumber { get; set; }
        [StringLength(250)] public string? Address { get; set; }
        [StringLength(100)] public string? City { get; set; }
        [StringLength(40)] public string? Phone { get; set; }
        [EmailAddress(ErrorMessage = "Email non valida."), StringLength(200)] public string? ContactEmail { get; set; }
        [StringLength(300)] public string? Website { get; set; }
        [StringLength(4000)] public string? Description { get; set; }
    }
}
