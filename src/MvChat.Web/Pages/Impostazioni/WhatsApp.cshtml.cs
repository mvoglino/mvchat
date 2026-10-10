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
    private readonly MvChat.Web.WhatsApp.WaRepo _wa;
    public WhatsAppModel(AppConfigStore config, Repos repos, MvChat.Web.WhatsApp.WaRepo wa) { _config = config; _repos = repos; _wa = wa; }

    /// <summary>Gli ultimi avvisi di Meta in parole semplici (per capire se il collegamento funziona).</summary>
    public List<(DateTime At, string What, string Outcome, string Css)> Events { get; private set; } = new();

    public static string Describe(string payload)
    {
        try
        {
            var j = System.Text.Json.Nodes.JsonNode.Parse(payload);
            var ch = j?["entry"]?[0]?["changes"]?[0];
            var field = ch?["field"]?.ToString() ?? "?";
            var v = ch?["value"];
            var pid = v?["metadata"]?["phone_number_id"]?.ToString();
            var msgs = v?["messages"]?.AsArray().Count ?? 0;
            var sts = v?["statuses"]?.AsArray().Count ?? 0;
            var parts = new List<string> { field };
            if (msgs > 0) parts.Add($"{msgs} messaggio/i del cliente ({v!["messages"]![0]?["type"]})");
            if (sts > 0) parts.Add($"{sts} stato/i ({v!["statuses"]![0]?["status"]})");
            if (field == "message_template_status_update") parts.Add($"template {v?["message_template_name"]}: {v?["event"]}");
            if (pid is not null) parts.Add("numero " + pid);
            return string.Join(" · ", parts);
        }
        catch { return "avviso non leggibile"; }
    }

    [BindProperty] public string? AppId { get; set; }
    [BindProperty] public string? AppSecret { get; set; }
    [BindProperty] public string GraphVersion { get; set; } = "";
    [BindProperty] public string GraphBaseUrl { get; set; } = "";
    // Testo e non numero: così "0,0658" e "0.0658" funzionano con qualsiasi impostazione di lingua del server.
    [BindProperty] public string? MarketingPrice { get; set; }
    [BindProperty] public string? UtilityPrice { get; set; }
    [BindProperty] public string? MaxCampaigns { get; set; }
    private static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
    private static decimal? Num(string? s) =>
        decimal.TryParse((s ?? "").Trim().Replace(',', '.'), System.Globalization.NumberStyles.Number, Inv, out var v) && v >= 0 && v < 10 ? v : null;
    public bool HasSecret { get; private set; }
    public string VerifyToken { get; private set; } = "";
    public string WebhookUrl => $"{Request.Scheme}://{Request.Host}/webhooks/whatsapp";

    public async Task OnGetAsync()
    {
        Load();
        foreach (var e in await _wa.LastEventsAsync(15))
            Events.Add((e.At, Describe(e.Payload),
                e.Error is not null ? "errore: " + e.Error : e.Note is not null ? "non usato: " + e.Note : "lavorato",
                e.Error is not null ? "bad" : e.Note is not null ? "warn" : "good"));
    }

    private void Load()
    {
        var m = _config.Current.Meta;
        AppId = m.AppId; GraphVersion = m.GraphVersion; GraphBaseUrl = m.GraphBaseUrl;
        MarketingPrice = m.MarketingPriceEur.ToString(Inv); UtilityPrice = m.UtilityPriceEur.ToString(Inv);
        MaxCampaigns = m.MaxCampaignsPerCustomer.ToString(Inv);
        HasSecret = !string.IsNullOrEmpty(m.AppSecret); VerifyToken = m.WebhookVerifyToken;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var c = _config.Current;
        // La chiave segreta di Meta è fatta di 32 caratteri (cifre e lettere a-f): così si blocca, per esempio,
        // la password di mvchat inserita per sbaglio dal browser.
        if (!string.IsNullOrWhiteSpace(AppSecret) && !System.Text.RegularExpressions.Regex.IsMatch(AppSecret.Trim(), "^[0-9a-fA-F]{32}$"))
        {
            TempData["Err"] = "La chiave segreta dell'app non sembra quella di Meta (sono 32 caratteri, cifre e lettere a-f): ricopiala da Impostazioni dell'app → Di base → Chiave segreta. Niente è stato salvato.";
            return Redirect("/Impostazioni/WhatsApp");
        }
        if (!string.IsNullOrWhiteSpace(AppId) && !AppId.Trim().All(char.IsDigit))
        {
            TempData["Err"] = "L'ID app di Meta è fatto solo di cifre. Niente è stato salvato.";
            return Redirect("/Impostazioni/WhatsApp");
        }
        c.Meta.AppId = (AppId ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(AppSecret)) c.Meta.AppSecret = AppSecret.Trim(); // vuoto = resta quella di prima
        c.Meta.GraphVersion = System.Text.RegularExpressions.Regex.IsMatch(GraphVersion ?? "", @"^v\d+\.\d+$") ? GraphVersion! : "v23.0";
        c.Meta.GraphBaseUrl = Uri.TryCreate(GraphBaseUrl, UriKind.Absolute, out _) ? GraphBaseUrl.TrimEnd('/') : "https://graph.facebook.com";
        if (Num(MarketingPrice) is decimal mp) c.Meta.MarketingPriceEur = mp;
        if (Num(UtilityPrice) is decimal up) c.Meta.UtilityPriceEur = up;
        if (int.TryParse((MaxCampaigns ?? "").Trim(), out var mc) && mc >= 0 && mc <= 31) c.Meta.MaxCampaignsPerCustomer = mc; // vuoto = resta com'era
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
