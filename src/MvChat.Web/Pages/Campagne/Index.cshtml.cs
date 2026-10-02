using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Campaigns;
using MvChat.Web.Data;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Campagne;

public class IndexModel : PageModel
{
    private readonly CampaignRepo _repo; private readonly Repos _repos;
    public IndexModel(CampaignRepo repo, Repos repos) { _repo = repo; _repos = repos; }

    public List<Campaign> Items { get; private set; } = new();
    public List<Gym> Gyms { get; private set; } = new();
    public int? Gym { get; private set; }

    public async Task OnGetAsync(int? gym)
    {
        var me = User.Scope();
        Gyms = await _repos.GymsAsync(me);
        Gym = Gyms.Any(g => g.Id == gym) ? gym : null;
        Items = await _repo.ListAsync(me, Gym);
    }
}
