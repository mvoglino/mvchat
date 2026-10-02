using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Catalog;
using MvChat.Web.Data;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Sedi;

public class EditModel : PageModel
{
    private readonly Repos _repos;
    private readonly CatalogRepo _catalog;
    public EditModel(Repos repos, CatalogRepo catalog) { _repos = repos; _catalog = catalog; }

    public Gym Gym { get; private set; } = null!;
    public MvChat.Web.Infrastructure.Sector Sector { get; private set; } = MvChat.Web.Infrastructure.Sectors.Get("palestra");
    [BindProperty] public ProfileInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var g = await _repos.GymAsync(User.Scope(), id);
        if (g is null) return NotFound();
        Gym = g;
        Sector = (await _catalog.OrgInfoAsync(g.OrganizationId))?.Sector ?? Sector;
        var p = await _catalog.ProfileAsync(id);
        Input = new ProfileInput { OpeningHours = p.OpeningHours, Services = p.Services, Classes = p.Classes, HowToReach = p.HowToReach,
            ExtraInfo = p.ExtraInfo, AssistantName = p.AssistantName, Formality = p.Formality };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var me = User.Scope();
        var g = await _repos.GymAsync(me, id);
        if (g is null) return NotFound();
        Gym = g;
        Sector = (await _catalog.OrgInfoAsync(g.OrganizationId))?.Sector ?? Sector;
        if (Input.Formality is not ("tu" or "lei")) Input.Formality = "tu";
        if (!ModelState.IsValid) return Page();
        await _catalog.SaveProfileAsync(new GymProfile
        {
            GymId = id, OpeningHours = T(Input.OpeningHours), Services = T(Input.Services), Classes = T(Input.Classes),
            HowToReach = T(Input.HowToReach), ExtraInfo = T(Input.ExtraInfo),
            AssistantName = string.IsNullOrWhiteSpace(Input.AssistantName) ? "assistente virtuale" : Input.AssistantName.Trim(),
            Formality = Input.Formality
        }, me.UserId);
        await _repos.AuditAsync(me, "profile.updated", g.Name, HttpContext.Connection.RemoteIpAddress?.ToString(), g.OrganizationId, g.Id);
        TempData["Ok"] = "Scheda salvata.";
        return Redirect(me.IsManager ? $"/Sedi/Edit/{id}" : "/Sedi");
    }

    private static string? T(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public class ProfileInput
    {
        [StringLength(1000)] public string? OpeningHours { get; set; }
        [StringLength(2000)] public string? Services { get; set; }
        [StringLength(2000)] public string? Classes { get; set; }
        [StringLength(1000)] public string? HowToReach { get; set; }
        [StringLength(6000)] public string? ExtraInfo { get; set; }
        [StringLength(60)] public string? AssistantName { get; set; }
        public string Formality { get; set; } = "tu";
    }
}
