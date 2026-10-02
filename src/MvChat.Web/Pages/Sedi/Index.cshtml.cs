using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Catalog;
using MvChat.Web.Data;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Sedi;

public class IndexModel : PageModel
{
    private readonly Repos _repos;
    private readonly CatalogRepo _catalog;
    public IndexModel(Repos repos, CatalogRepo catalog) { _repos = repos; _catalog = catalog; }
    public List<Gym> Gyms { get; private set; } = new();
    public Dictionary<int, int> Completeness { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var me = User.Scope();
        Gyms = (await _repos.GymsAsync(me)).Where(g => g.IsActive).ToList();
        // Il responsabile ha una sola attività: si va dritti alla sua scheda.
        if (me.IsManager && Gyms.Count == 1) return Redirect($"/Sedi/Edit/{Gyms[0].Id}");
        Completeness = await _catalog.CompletenessAsync(Gyms.Select(g => g.Id));
        return Page();
    }
}
