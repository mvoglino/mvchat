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
    // Testo e non numero: così "0,0658" e "0.0658" funzionano con qualsiasi impostazione di lingua del server.
    [BindProperty] public string? MarketingPrice { get; set; }
    [BindProperty] public string? UtilityPrice { get; set; }
    private static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
    private static decimal? Num(string? s) =>
        decimal.TryParse((s ?? "").Trim().Replace(',', '.'), System.Globalization.NumberStyles.Number, Inv, out var v) && v >= 0 && v < 10 ? v : null;
    public bool HasSecret { get; private set; }
    public string VerifyToken { get; private set; } = "";
    public string WebhookUrl => $"{Request.Scheme}://{Request.Host}/webhooks/whatsapp";

    public void OnGet() => Load();

    private void Load()
    {
        var m = _config.Current.Meta;
        AppId = m.AppId; GraphVersion = m.GraphVersion; GraphBaseUrl = m.GraphBaseUrl;
        MarketingPrice = m.MarketingPriceEur.ToString(Inv); UtilityPrice = m.UtilityPriceEur.ToString(Inv);
        HasSecret = !string.IsNullOrEmpty(m.AppSecret); VerifyToken = m.WebhookVerifyToken;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var c = _config.Current;
        c.Meta.AppId = (AppId ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(AppSecret)) c.Meta.AppSecret = AppSecret.Trim(); // vuoto = resta quella di prima
        c.Meta.GraphVersion = System.Text.RegularExpressions.Regex.IsMatch(GraphVersion ?? "", @"^v\d+\.\d+$") ? GraphVersion! : "v23.0";
        c.Meta.GraphBaseUrl = Uri.TryCreate(GraphBaseUrl, UriKind.Absolute, out _) ? GraphBaseUrl.TrimEnd('/') : "https://graph.facebook.com";
        if (Num(MarketingPrice) is decimal mp) c.Meta.MarketingPriceEur = mp;
        if (Num(UtilityPrice) is decimal up) c.Meta.UtilityPriceEur = up;
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
