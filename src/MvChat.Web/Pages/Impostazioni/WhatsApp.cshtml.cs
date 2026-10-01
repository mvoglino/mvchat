using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Data;
using MvChat.Web.Infrastructure;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Impostazioni;

public class WhatsAppModel : PageModel
{
    private readonly AppConfigStore _config;
    private readonly Repos _repos;
    public WhatsAppModel(AppConfigStore config, Repos repos) { _config = config; _repos = repos; }

    [BindProperty] public string? AppId { get; set; }
    [BindProperty] public string? AppSecret { get; set; }
    [BindProperty] public string GraphVersion { get; set; } = "";
    [BindProperty] public string GraphBaseUrl { get; set; } = "";
    public bool HasSecret { get; private set; }
    public string VerifyToken { get; private set; } = "";
    public string WebhookUrl => $"{Request.Scheme}://{Request.Host}/webhooks/whatsapp";

    public void OnGet() => Load();

    private void Load()
    {
        var m = _config.Current.Meta;
        AppId = m.AppId; GraphVersion = m.GraphVersion; GraphBaseUrl = m.GraphBaseUrl;
        HasSecret = !string.IsNullOrEmpty(m.AppSecret); VerifyToken = m.WebhookVerifyToken;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var c = _config.Current;
        c.Meta.AppId = (AppId ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(AppSecret)) c.Meta.AppSecret = AppSecret.Trim(); // vuoto = resta quella di prima
        c.Meta.GraphVersion = System.Text.RegularExpressions.Regex.IsMatch(GraphVersion ?? "", @"^v\d+\.\d+$") ? GraphVersion! : "v23.0";
        c.Meta.GraphBaseUrl = Uri.TryCreate(GraphBaseUrl, UriKind.Absolute, out _) ? GraphBaseUrl.TrimEnd('/') : "https://graph.facebook.com";
        _config.Save(c);
        await _repos.AuditAsync(User.Scope(), "meta.settings", $"app {c.Meta.AppId}", HttpContext.Connection.RemoteIpAddress?.ToString());
        TempData["Ok"] = "Impostazioni Meta salvate.";
        return Redirect("/Impostazioni/WhatsApp");
    }

    public async Task<IActionResult> OnPostNewTokenAsync()
    {
        var c = _config.Current;
        c.Meta.WebhookVerifyToken = AppConfigStore.NewToken();
        _config.Save(c);
        await _repos.AuditAsync(User.Scope(), "meta.verify_token", null, HttpContext.Connection.RemoteIpAddress?.ToString());
        TempData["Ok"] = "Nuova parola d'ordine creata: aggiornala anche su Meta.";
        return Redirect("/Impostazioni/WhatsApp");
    }
}
