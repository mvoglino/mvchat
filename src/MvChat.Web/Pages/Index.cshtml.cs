using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Data;
using MvChat.Web.Reports;
using MvChat.Web.Security;

namespace MvChat.Web.Pages;

/// <summary>Il pannello di controllo: per MVitalia tutto il servizio, per gli altri il proprio perimetro.</summary>
public class IndexModel : PageModel
{
    private readonly Repos _repos; private readonly AlertRepo _alerts; private readonly MvChat.Web.Billing.SubscriptionRepo _subs;
    public IndexModel(Repos repos, AlertRepo alerts, MvChat.Web.Billing.SubscriptionRepo subs) { _repos = repos; _alerts = alerts; _subs = subs; }

    public Scope Me { get; private set; } = new();
    public (int Orgs, int Gyms, int Users) Counts { get; private set; }
    public List<Gym> Gyms { get; private set; } = new();
    public Pulse Pulse { get; private set; } = new(0, 0, 0, 0, 0, 0);
    public List<Alert> Alerts { get; private set; } = new();
    /// <summary>Abbonamento mvchat e spesa Meta stimata del mese in corso (non per MVitalia e operatori).</summary>
    public (decimal Fee, decimal Meta, DateTime Since)? Costs { get; private set; }

    public async Task OnGetAsync()
    {
        Me = User.Scope();
        Counts = await _repos.CountsAsync(Me);
        Gyms = await _repos.GymsAsync(Me);
        Pulse = await _alerts.PulseAsync(Me);
        Alerts = await _alerts.AlertsAsync(Me);
        if (Me.CanManageUsers && !Me.IsSuperAdmin) Costs = await _subs.CurrentAsync(Me);
    }
}
