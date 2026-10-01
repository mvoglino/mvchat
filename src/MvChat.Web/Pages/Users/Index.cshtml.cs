using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Data;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Users;

public class IndexModel : PageModel
{
    private readonly Repos _repos;
    public IndexModel(Repos repos) => _repos = repos;
    public List<UserRow> Items { get; private set; } = new();
    public Scope Me { get; private set; } = new();
    public async Task OnGetAsync() { Me = User.Scope(); Items = await _repos.UsersAsync(Me); }
}
