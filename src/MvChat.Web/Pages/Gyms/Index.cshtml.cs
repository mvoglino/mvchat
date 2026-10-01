using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Data;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Gyms;

public class IndexModel : PageModel
{
    private readonly Repos _repos;
    public IndexModel(Repos repos) => _repos = repos;
    public List<Gym> Items { get; private set; } = new();
    public Scope Me { get; private set; } = new();
    public async Task OnGetAsync() { Me = User.Scope(); Items = await _repos.GymsAsync(Me); }
}
