using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Data;
using MvChat.Web.Security;

namespace MvChat.Web.Pages;

public class IndexModel : PageModel
{
    private readonly Repos _repos;
    public IndexModel(Repos repos) => _repos = repos;

    public Scope Me { get; private set; } = new();
    public (int Orgs, int Gyms, int Users) Counts { get; private set; }
    public List<Gym> Gyms { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Me = User.Scope();
        Counts = await _repos.CountsAsync(Me);
        Gyms = await _repos.GymsAsync(Me);
    }
}
