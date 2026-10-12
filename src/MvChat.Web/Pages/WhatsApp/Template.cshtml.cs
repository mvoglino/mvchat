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
    /// <summary>Le altre attività dello stesso gruppo con un numero collegato: lì si può copiare un template.</summary>
    public List<(Gym Gym, WaNumber Number)> CopyTargets { get; private set; } = new();
    [BindProperty] public List<int> Targets { get; set; } = new();

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
        if (g.InGroup)
            foreach (var other in (await _repos.GymsAsync(me)).Where(x => x.Id != g.Id && x.OrganizationId == g.OrganizationId).OrderBy(x => x.Name))
                if (await _wa.NumberForGymAsync(other.Id) is { } on) CopyTargets.Add((other, on));
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
        if (Problems.Count == 0 && await _wa.TemplateNameInAccountAsync(Number, name, "it") is string owner)
            Problems.Add($"Questo nome è già usato da {owner}, che ha lo stesso account WhatsApp: apri i template di {owner} e usa «Copia sulle altre attività», oppure scegli un altro nome.");
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

    [BindProperty] public int TemplateId { get; set; }

    /// <summary>Il template non era arrivato a Meta (errore di collegamento): si rimanda senza cambiare nome.</summary>
    public async Task<IActionResult> OnPostResubmitAsync(int id)
    {
        if (!await LoadAsync(id) || Number is null) return NotFound();
        var t = Items.FirstOrDefault(x => x.Id == TemplateId);
        if (t is null || t.Status != "errore") return Redirect($"/WhatsApp/Template/{id}");
        var (ok, error) = await _service.SubmitTemplateAsync(Number, t);
        TempData[ok ? "Ok" : "Err"] = ok ? "Template inviato di nuovo a Meta." : "Meta ha rifiutato l'invio: " + error;
        return Redirect($"/WhatsApp/Template/{id}");
    }

    /// <summary>Elimina un template rifiutato o non inviato, così il nome si può riusare con un testo corretto.</summary>
    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        var t = Items.FirstOrDefault(x => x.Id == TemplateId);
        if (t is null) return Redirect($"/WhatsApp/Template/{id}");
        var n = await _wa.DeleteTemplateAsync(t.Id, Gym.Id);
        if (n > 0) await _repos.AuditAsync(User.Scope(), "wa.template.deleted", t.Name, HttpContext.Connection.RemoteIpAddress?.ToString(), Gym.OrganizationId, Gym.Id);
        TempData[n > 0 ? "Ok" : "Err"] = n > 0 ? "Template eliminato." : "Si possono eliminare solo template non approvati e non usati da una campagna.";
        return Redirect($"/WhatsApp/Template/{id}");
    }

    /// <summary>
    /// Copia un template sulle altre attività del gruppo. Se l'attività usa lo stesso account WhatsApp il template è già lì
    /// (stesso stato, nessun nuovo invio a Meta); altrimenti viene mandato a Meta per l'approvazione sul suo account.
    /// </summary>
    public async Task<IActionResult> OnPostCopyAsync(int id)
    {
        if (!await LoadAsync(id) || Number is null) return NotFound();
        var src = Items.FirstOrDefault(x => x.Id == TemplateId);
        if (src is null || src.Status is "bozza" or "errore" or "rifiutato") { TempData["Err"] = "Si copiano solo template approvati o in revisione."; return Redirect($"/WhatsApp/Template/{id}"); }
        var chosen = CopyTargets.Where(t => Targets.Contains(t.Gym.Id)).ToList();
        if (chosen.Count == 0) { TempData["Err"] = "Scegli almeno un'attività."; return Redirect($"/WhatsApp/Template/{id}"); }
        var me = User.Scope();
        var done = new List<string>(); var skipped = new List<string>(); var failed = new List<string>();
        foreach (var (gym, num) in chosen)
        {
            if (await _wa.TemplateNameTakenAsync(num.Id, src.Name, src.Language)) { skipped.Add(gym.Name); continue; }
            var sameAccount = !num.IsSimulated && !Number.IsSimulated && num.WabaId == Number.WabaId && src.MetaTemplateId is not null;
            var t = new WaTemplate
            {
                OrganizationId = gym.OrganizationId, GymId = gym.Id, WaNumberId = num.Id, GoalModelId = src.GoalModelId,
                Name = src.Name, Language = src.Language, Category = src.Category, Body = src.Body, Variables = src.Variables,
                Status = sameAccount ? src.Status : "bozza", MetaTemplateId = sameAccount ? src.MetaTemplateId : null
            };
            t.Id = await _wa.InsertTemplateAsync(t, me.UserId);
            if (sameAccount) { done.Add(gym.Name); continue; }
            var (ok, error) = await _service.SubmitTemplateAsync(num, t);
            if (ok) done.Add(gym.Name); else failed.Add($"{gym.Name} ({error})");
        }
        await _repos.AuditAsync(me, "wa.template.copied", $"{src.Name} → {string.Join(", ", done)}", HttpContext.Connection.RemoteIpAddress?.ToString(), Gym.OrganizationId, Gym.Id);
        var msg = done.Count > 0 ? $"Template «{src.Name}» copiato su: {string.Join(", ", done)}. Sulle attività con un proprio account WhatsApp va approvato da Meta (di solito pochi minuti)." : "";
        if (skipped.Count > 0) msg += $" Già presente su: {string.Join(", ", skipped)}.";
        if (failed.Count > 0) msg += $" Non riuscito su: {string.Join("; ", failed)}.";
        TempData[failed.Count == 0 && done.Count > 0 ? "Ok" : "Err"] = msg.Trim();
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
