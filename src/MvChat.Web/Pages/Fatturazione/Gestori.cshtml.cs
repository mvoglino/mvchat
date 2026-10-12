using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Billing;
using MvChat.Web.Data;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Fatturazione;

/// <summary>
/// Gestori: società che detengono più attività di un gruppo e ricevono un solo rendiconto per tutte.
/// Li crea MVitalia; le attività si collegano dalla loro scheda (Attività → Modifica → Rendiconto intestato a).
/// </summary>
public class GestoriModel : PageModel
{
    private readonly BillingService _billing; private readonly Repos _repos;
    public GestoriModel(BillingService billing, Repos repos) { _billing = billing; _repos = repos; }

    public List<BillingEntity> Items { get; private set; } = new();
    public List<Organization> Groups { get; private set; } = new();
    public string? Error { get; private set; }
    [BindProperty] public BillingEntity Input { get; set; } = new();

    private async Task<bool> LoadAsync()
    {
        var me = User.Scope();
        if (!me.IsSuperAdmin) return false;
        Items = await _billing.EntitiesAsync();
        Groups = (await _repos.OrganizationsAsync(me)).Where(o => o.IsGroup).OrderBy(o => o.Name).ToList();
        return true;
    }

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (!await LoadAsync()) return Forbid();
        if (id is int i) Input = Items.FirstOrDefault(x => x.Id == i) ?? new();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadAsync()) return Forbid();
        static string? T(string? v, int max) => string.IsNullOrWhiteSpace(v) ? null : (v.Trim().Length > max ? v.Trim()[..max] : v.Trim());
        var existing = Input.Id > 0 ? Items.FirstOrDefault(x => x.Id == Input.Id) : null;
        if (Input.Id > 0 && existing is null) return NotFound();
        var e = new BillingEntity
        {
            Id = existing?.Id ?? 0,
            OrganizationId = existing?.OrganizationId ?? Input.OrganizationId, // il gruppo di un gestore esistente non cambia
            Name = T(Input.Name, 150) ?? "", LegalName = T(Input.LegalName, 200), VatNumber = T(Input.VatNumber, 20),
            Address = T(Input.Address, 250), City = T(Input.City, 100), Email = T(Input.Email, 200), IsActive = Input.IsActive
        };
        if (!Groups.Any(g => g.Id == e.OrganizationId)) Error = "Scegli il gruppo.";
        else if (e.Name.Length < 2) Error = "Scrivi il nome del gestore.";
        else if (e.VatNumber is not null && !System.Text.RegularExpressions.Regex.IsMatch(e.VatNumber, @"^[A-Za-z]{0,2}\d{11}$")) Error = "La partita IVA è fatta di 11 cifre.";
        if (Error is not null) { Input = e; return Page(); }
        var id = await _billing.SaveEntityAsync(e);
        await _repos.AuditAsync(User.Scope(), existing is null ? "billing.entity.created" : "billing.entity.updated", e.Name, HttpContext.Connection.RemoteIpAddress?.ToString(), e.OrganizationId);
        TempData["Ok"] = existing is null ? "Gestore creato: ora collega le sue attività da Attività → Modifica → «Rendiconto intestato a»." : "Gestore aggiornato.";
        return Redirect("/Fatturazione/Gestori");
    }
}
