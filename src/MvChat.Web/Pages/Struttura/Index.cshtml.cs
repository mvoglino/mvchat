using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Data;
using MvChat.Web.Infrastructure;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Struttura;

/// <summary>
/// Nome, logo, colore e dati dell'attività di una struttura.
/// MVitalia le modifica tutte; la direzione solo la propria (senza cambiare il tipo di attività).
/// </summary>
public class IndexModel : PageModel
{
    public const int MaxLogoBytes = 300 * 1024;
    private readonly Repos _repos;
    public IndexModel(Repos repos) => _repos = repos;

    public OrgProfile P { get; private set; } = new();
    public bool CanChangeSector => User.Scope().IsSuperAdmin;
    [BindProperty] public ProfileInput Input { get; set; } = new();
    [BindProperty] public IFormFile? Logo { get; set; }
    [BindProperty] public bool RemoveLogo { get; set; }

    /// <summary>La struttura si ricava da chi chiede: la direzione non può aprirne un'altra cambiando il numero nell'indirizzo.</summary>
    private async Task<bool> LoadAsync(int? id)
    {
        var me = User.Scope();
        var orgId = me.IsSuperAdmin ? id : me.OrganizationId;
        if (orgId is null || (id is not null && id != orgId)) return false;
        var p = await _repos.OrgProfileAsync(orgId.Value);
        if (p is null) return false;
        P = p;
        return true;
    }

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (!await LoadAsync(id)) return NotFound();
        Input = new ProfileInput
        {
            Name = P.Name, Sector = P.Sector, PrimaryColor = P.PrimaryColor, LegalName = P.LegalName, VatNumber = P.VatNumber, Address = P.Address,
            City = P.City, Phone = P.Phone, ContactEmail = P.ContactEmail, Website = P.Website, Description = P.Description
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!Regex.IsMatch(Input.PrimaryColor ?? "", "^#[0-9A-Fa-f]{6}$")) ModelState.AddModelError("Input.PrimaryColor", "Usa un colore nel formato #RRGGBB.");
        if (CanChangeSector && !Sectors.All.Any(s => s.Key == Input.Sector)) ModelState.AddModelError("Input.Sector", "Scegli il tipo di attività.");
        if (!string.IsNullOrWhiteSpace(Input.Website) && !Uri.TryCreate(Input.Website.Trim().StartsWith("http") ? Input.Website.Trim() : "https://" + Input.Website.Trim(), UriKind.Absolute, out _))
            ModelState.AddModelError("Input.Website", "Indirizzo del sito non valido.");

        byte[]? logo = null; string? logoType = null;
        if (Logo is { Length: > 0 })
        {
            if (Logo.Length > MaxLogoBytes) ModelState.AddModelError("Logo", "Il logo è troppo pesante: massimo 300 KB.");
            else
            {
                using var ms = new MemoryStream();
                await Logo.CopyToAsync(ms);
                logo = ms.ToArray();
                // Si guarda il contenuto vero del file, non l'estensione: solo PNG, JPEG o WebP.
                logoType = ImageType(logo);
                if (logoType is null) ModelState.AddModelError("Logo", "Il logo deve essere un'immagine PNG, JPG o WebP.");
            }
        }
        if (!ModelState.IsValid) return Page();

        var me = User.Scope();
        P.Name = Input.Name.Trim(); P.PrimaryColor = Input.PrimaryColor!; P.Sector = Input.Sector ?? P.Sector;
        P.LegalName = T(Input.LegalName); P.VatNumber = T(Input.VatNumber); P.Address = T(Input.Address); P.City = T(Input.City); P.Phone = T(Input.Phone);
        P.ContactEmail = T(Input.ContactEmail); P.Website = T(Input.Website); P.Description = T(Input.Description);
        await _repos.SaveOrgProfileAsync(P, CanChangeSector);
        if (logo is not null) await _repos.SaveLogoAsync(P.Id, logo, logoType);
        else if (RemoveLogo) await _repos.SaveLogoAsync(P.Id, null, null);
        await _repos.AuditAsync(me, "org.profile", P.Name + (logo is not null ? " · nuovo logo" : ""), HttpContext.Connection.RemoteIpAddress?.ToString(), P.Id);
        TempData["Ok"] = "Dati della struttura salvati.";
        return Redirect(me.IsSuperAdmin ? $"/Struttura/{P.Id}" : "/Struttura");
    }

    private static string? T(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public static string? ImageType(byte[] b) =>
        b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 ? "image/png"
        : b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF ? "image/jpeg"
        : b.Length > 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P' ? "image/webp"
        : null;

    public class ProfileInput
    {
        [Required(ErrorMessage = "Indica il nome."), StringLength(150)] public string Name { get; set; } = "";
        public string? Sector { get; set; }
        public string? PrimaryColor { get; set; } = "#F6931E";
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
