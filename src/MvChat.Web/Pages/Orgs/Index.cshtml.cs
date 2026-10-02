using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Data;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Orgs;

public class IndexModel : PageModel
{
    private readonly Repos _repos;
    public IndexModel(Repos repos) => _repos = repos;
    public List<Organization> Items { get; private set; } = new();
    public async Task OnGetAsync() => Items = (await _repos.OrganizationsAsync(User.Scope())).Where(o => o.IsGroup).ToList();
}
