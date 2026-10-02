using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Data;
using MvChat.Web.Infrastructure;
using MvChat.Web.Privacy;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Impostazioni;

/// <summary>Per quanto tempo si tengono i dati e quando è passata l'ultima pulizia.</summary>
public class PrivacyModel : PageModel
{
    private readonly AppConfigStore _config; private readonly RetentionService _retention; private readonly Repos _repos; private readonly Db _db;
    public PrivacyModel(AppConfigStore config, RetentionService retention, Repos repos, Db db) { _config = config; _retention = retention; _repos = repos; _db = db; }

    [BindProperty] public int RetentionMonths { get; set; }
    [BindProperty] public int WebhookDays { get; set; }
    public DateTime? LastRun { get; private set; }
    public string? LastDetail { get; private set; }
    public string? Error { get; private set; }

    private async Task LoadAsync()
    {
        LastRun = await _retention.LastRunAsync();
        LastDetail = await _db.ScalarAsync<string>("SELECT Detail FROM AuditLog WHERE Action='privacy.cleanup' ORDER BY Id DESC LIMIT 1");
    }

    public async Task OnGetAsync()
    {
        var p = _config.Current.Privacy;
        RetentionMonths = p.RetentionMonths; WebhookDays = p.WebhookDays;
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (RetentionMonths is < 1 or > 120 || WebhookDays is < 1 or > 365)
        {
            Error = "Conservazione tra 1 e 120 mesi, avvisi di Meta tra 1 e 365 giorni.";
            await LoadAsync(); return Page();
        }
        var c = _config.Current;
        c.Privacy.RetentionMonths = RetentionMonths; c.Privacy.WebhookDays = WebhookDays;
        _config.Save(c);
        await _repos.AuditAsync(User.Scope(), "privacy.settings", $"conservazione {RetentionMonths} mesi, avvisi Meta {WebhookDays} giorni", HttpContext.Connection.RemoteIpAddress?.ToString());
        TempData["Ok"] = "Periodo di conservazione salvato.";
        return Redirect("/Impostazioni/Privacy");
    }

    public async Task<IActionResult> OnPostRunAsync()
    {
        var r = await _retention.RunAsync(force: true);
        TempData["Ok"] = $"Pulizia fatta: {r.Conversations} conversazioni, {r.Messages} messaggi, {r.Lists} liste, {r.Recipients} destinatari, {r.WebhookEvents} avvisi Meta, {r.AuditRows} righe di registro cancellate.";
        return Redirect("/Impostazioni/Privacy");
    }
}
