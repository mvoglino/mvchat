using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Data;
using MvChat.Web.Security;
using MvChat.Web.WhatsApp;

namespace MvChat.Web.Pages.WhatsApp;

public class IndexModel : PageModel
{
    private readonly Repos _repos;
    private readonly WaRepo _wa;
    public IndexModel(Repos repos, WaRepo wa) { _repos = repos; _wa = wa; }
    public List<Gym> Gyms { get; private set; } = new();
    public Dictionary<int, WaNumber> Numbers { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var me = User.Scope();
        Gyms = (await _repos.GymsAsync(me)).Where(g => g.IsActive).ToList();
        Numbers = (await _wa.NumbersAsync(me)).ToDictionary(n => n.GymId);
    }
}
