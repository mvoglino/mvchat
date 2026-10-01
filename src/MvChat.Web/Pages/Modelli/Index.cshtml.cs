using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Catalog;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Modelli;

public class IndexModel : PageModel
{
    private readonly CatalogRepo _catalog;
    public IndexModel(CatalogRepo catalog) => _catalog = catalog;
    public List<GoalModel> Items { get; private set; } = new();
    public Scope Me { get; private set; } = new();
    public async Task OnGetAsync() { Me = User.Scope(); Items = await _catalog.ModelsAsync(Me); }
}
