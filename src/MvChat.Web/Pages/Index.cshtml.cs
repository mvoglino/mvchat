using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Data;
using MvChat.Web.Reports;
using MvChat.Web.Security;

namespace MvChat.Web.Pages;

/// <summary>Il pannello di controllo: per MVitalia tutto il servizio, per gli altri il proprio perimetro.</summary>
public class IndexModel : PageModel
{
    private readonly Repos _repos; private readonly AlertRepo _alerts;
    public IndexModel(Repos repos, AlertRepo alerts) { _repos = repos; _alerts = alerts; }

    public Scope Me { get; private set; } = new();
    public (int Orgs, int Gyms, int Users) Counts { get; private set; }
    public List<Gym> Gyms { get; private set; } = new();
    public Pulse Pulse { get; private set; } = new(0, 0, 0, 0, 0, 0);
    public List<Alert> Alerts { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Me = User.Scope();
        Counts = await _repos.CountsAsync(Me);
        Gyms = await _repos.GymsAsync(Me);
        Pulse = await _alerts.PulseAsync(Me);
        Alerts = await _alerts.AlertsAsync(Me);
    }
}
