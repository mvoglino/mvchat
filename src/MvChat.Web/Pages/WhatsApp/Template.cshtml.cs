using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Catalog;
using MvChat.Web.Data;
using MvChat.Web.Security;
using MvChat.Web.WhatsApp;

namespace MvChat.Web.Pages.WhatsApp;

public class TemplateModel : PageModel
{
    private readonly Repos _repos;
    private readonly WaRepo _wa;
    private readonly WaService _service;
    private readonly CatalogRepo _catalog;
    public TemplateModel(Repos repos, WaRepo wa, WaService service, CatalogRepo catalog) { _repos = repos; _wa = wa; _service = service; _catalog = catalog; }

    public Gym Gym { get; private set; } = null!;
    public WaNumber? Number { get; private set; }
    public List<WaTemplate> Items { get; private set; } = new();
    public List<GoalModel> Models { get; private set; } = new();
    public List<string> Problems { get; private set; } = new();

    [BindProperty(SupportsGet = true)] public int? Model { get; set; }
    [BindProperty] public string? Name { get; set; }
    [BindProperty] public string Category { get; set; } = "MARKETING";
    [BindProperty] public string? Body { get; set; }

    private async Task<bool> LoadAsync(int id)
    {
        var me = User.Scope();
        var g = await _repos.GymAsync(me, id);
        if (g is null) return false;
        Gym = g;
        Number = await _wa.NumberForGymAsync(id);
        Items = await _wa.TemplatesAsync(id);
        Models = await _catalog.ModelsAsync(me, onlyActive: true);
        return true;
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (Models.FirstOrDefault(m => m.Id == Model) is { } gm)
        {
            Name = TemplateText.MetaName($"{gm.Code}_{Gym.Name}");
            Body = gm.TemplateSuggestion;
        }
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (Number is null) return Redirect($"/WhatsApp/Numero/{id}");
        var name = TemplateText.MetaName(Name ?? "");
        Body = (Body ?? "").Replace("\r\n", "\n").Trim();
        if (name.Length < 3) Problems.Add("Dai un nome al template (almeno 3 lettere).");
        Problems.AddRange(TemplateText.Problems(Body));
        if (Category is not ("MARKETING" or "UTILITY")) Category = "MARKETING";
        if (Problems.Count == 0 && await _wa.TemplateNameTakenAsync(Number.Id, name, "it")) Problems.Add("Esiste già un template con questo nome per questo numero.");
        if (Problems.Count > 0) return Page();

        var t = new WaTemplate
        {
            OrganizationId = Gym.OrganizationId, GymId = Gym.Id, WaNumberId = Number.Id, GoalModelId = Models.Any(m => m.Id == Model) ? Model : null,
            Name = name, Language = "it", Category = Category, Body = Body, Variables = TemplateText.Variables(Body), Status = "bozza"
        };
        t.Id = await _wa.InsertTemplateAsync(t, User.Scope().UserId);
        var (ok, error) = await _service.SubmitTemplateAsync(Number, t);
        await _repos.AuditAsync(User.Scope(), "wa.template.submitted", name, HttpContext.Connection.RemoteIpAddress?.ToString(), Gym.OrganizationId, Gym.Id);
        TempData[ok ? "Ok" : "Err"] = ok
            ? (Number.IsSimulated ? "Template approvato (numero simulato)." : "Template inviato a Meta: di solito l'approvazione arriva in pochi minuti, a volte fino a 24 ore.")
            : "Meta ha rifiutato l'invio: " + error;
        return Redirect($"/WhatsApp/Template/{id}");
    }

    public async Task<IActionResult> OnPostRefreshAsync(int id)
    {
        if (!await LoadAsync(id) || Number is null) return NotFound();
        var (updated, error) = await _service.RefreshTemplatesAsync(Number);
        TempData[error is null ? "Ok" : "Err"] = error ?? $"Stati aggiornati da Meta ({updated}).";
        return Redirect($"/WhatsApp/Template/{id}");
    }
}
