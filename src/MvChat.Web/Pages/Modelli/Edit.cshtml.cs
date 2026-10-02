using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Catalog;
using MvChat.Web.Data;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Modelli;

public class EditModel : PageModel
{
    private readonly CatalogRepo _catalog;
    private readonly Repos _repos;
    public EditModel(CatalogRepo catalog, Repos repos) { _catalog = catalog; _repos = repos; }

    [BindProperty] public ModelInput Input { get; set; } = new();
    public List<Organization> Orgs { get; private set; } = new();
    public Scope Me { get; private set; } = new();
    public bool IsNew => Input.Id == 0;

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        Me = User.Scope();
        if (!Me.IsSuperAdmin && !Me.IsOrgAdmin) return Forbid();
        Orgs = await _repos.OrganizationsAsync(Me);
        if (id is null) { Input.OrganizationId = Me.OrganizationId; return Page(); }
        var m = await _catalog.ModelAsync(Me, id.Value);
        if (m is null || !CatalogRepo.CanEdit(Me, m)) return NotFound();
        Input = new ModelInput { Id = m.Id, OrganizationId = m.OrganizationId, Name = m.Name, Success = m.Success, Instructions = m.Instructions,
            TemplateSuggestion = m.TemplateSuggestion, NeedsOffer = m.NeedsOffer, MaxAiMessages = m.MaxAiMessages, IsActive = m.IsActive, SortOrder = m.SortOrder };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Me = User.Scope();
        if (!Me.IsSuperAdmin && !Me.IsOrgAdmin) return Forbid();
        Orgs = await _repos.OrganizationsAsync(Me);
        GoalModel? existing = null;
        if (Input.Id != 0)
        {
            existing = await _catalog.ModelAsync(Me, Input.Id);
            if (existing is null || !CatalogRepo.CanEdit(Me, existing)) return NotFound();
            Input.OrganizationId = existing.OrganizationId; // standard resta standard, struttura resta struttura
        }
        else if (!Me.IsSuperAdmin) Input.OrganizationId = Me.OrganizationId;
        else if (Input.OrganizationId is int o && !Orgs.Any(x => x.Id == o)) ModelState.AddModelError("Input.OrganizationId", "Struttura non valida.");
        if (!ModelState.IsValid) return Page();

        var code = existing?.Code ?? Regex.Replace(Input.Name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        var id = await _catalog.SaveModelAsync(new GoalModel
        {
            Id = Input.Id, OrganizationId = Input.OrganizationId, Code = code.Length > 40 ? code[..40] : code, Name = Input.Name.Trim(),
            Success = Input.Success.Trim(), Instructions = Input.Instructions.Trim(), TemplateSuggestion = string.IsNullOrWhiteSpace(Input.TemplateSuggestion) ? null : Input.TemplateSuggestion.Trim(),
            NeedsOffer = Input.NeedsOffer, MaxAiMessages = Input.MaxAiMessages, IsActive = Input.IsActive, SortOrder = Input.SortOrder
        });
        await _repos.AuditAsync(Me, IsNew ? "model.created" : "model.updated", Input.Name, HttpContext.Connection.RemoteIpAddress?.ToString(), Input.OrganizationId);
        TempData["Ok"] = IsNew ? "Modello creato." : "Modello aggiornato.";
        return Redirect("/Modelli");
    }

    public class ModelInput
    {
        public int Id { get; set; }
        public int? OrganizationId { get; set; }
        [Required(ErrorMessage = "Dai un nome al modello."), StringLength(100)] public string Name { get; set; } = "";
        [Required(ErrorMessage = "Scrivi quando la conversazione è riuscita."), StringLength(500)] public string Success { get; set; } = "";
        [Required(ErrorMessage = "Scrivi le indicazioni per l'assistente."), StringLength(4000)] public string Instructions { get; set; } = "";
        [StringLength(1000)] public string? TemplateSuggestion { get; set; }
        public bool NeedsOffer { get; set; } = true;
        [Range(2, 15, ErrorMessage = "Da 2 a 15 messaggi.")] public int MaxAiMessages { get; set; } = 8;
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; } = 100;
    }
}
