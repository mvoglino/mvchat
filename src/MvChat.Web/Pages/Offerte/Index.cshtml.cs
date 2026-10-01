using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Catalog;
using MvChat.Web.Data;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Offerte;

public class IndexModel : PageModel
{
    private readonly CatalogRepo _catalog;
    private readonly Repos _repos;
    public IndexModel(CatalogRepo catalog, Repos repos) { _catalog = catalog; _repos = repos; }
    public List<Offer> Items { get; private set; } = new();
    public List<Gym> Gyms { get; private set; } = new();
    public int? GymFilter { get; private set; }

    public async Task OnGetAsync(int? gym)
    {
        var me = User.Scope();
        Gyms = await _repos.GymsAsync(me);
        GymFilter = gym is int g && Gyms.Any(x => x.Id == g) ? g : null;
        Items = await _catalog.OffersAsync(me, GymFilter);
    }
}
