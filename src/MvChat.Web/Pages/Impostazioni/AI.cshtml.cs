using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Ai;
using MvChat.Web.Data;
using MvChat.Web.Infrastructure;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Impostazioni;

public class AIModel : PageModel
{
    private readonly AppConfigStore _config;
    private readonly AiClient _ai;
    private readonly Repos _repos;
    private readonly ConversationRepo _convs;
    public AIModel(AppConfigStore config, AiClient ai, Repos repos, ConversationRepo convs) { _config = config; _ai = ai; _repos = repos; _convs = convs; }

    [BindProperty] public string Provider { get; set; } = "";
    [BindProperty] public ProviderInput Anthropic { get; set; } = new();
    [BindProperty] public ProviderInput OpenAi { get; set; } = new();
    [BindProperty] public bool Transcribe { get; set; }
    [BindProperty] public string TranscribeModel { get; set; } = "";
    [BindProperty] public string TranscribePrice { get; set; } = "";
    [BindProperty] public int TranscribeMaxSeconds { get; set; }
    public bool AnthropicHasKey { get; private set; }
    public bool OpenAiHasKey { get; private set; }
    public List<UsageRow> Usage { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Usage = await _convs.UsageSummaryAsync(30);
        var a = _config.Current.Ai;
        Provider = a.Provider;
        Anthropic = ProviderInput.From(a.Anthropic); OpenAi = ProviderInput.From(a.OpenAi);
        AnthropicHasKey = !string.IsNullOrEmpty(a.Anthropic.KeyEnc); OpenAiHasKey = !string.IsNullOrEmpty(a.OpenAi.KeyEnc);
        Transcribe = a.Transcribe; TranscribeModel = a.TranscribeModel; TranscribeMaxSeconds = a.TranscribeMaxSeconds;
        TranscribePrice = a.TranscribePricePerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var c = _config.Current;
        c.Ai.Provider = Provider is "anthropic" or "openai" ? Provider : "";
        Anthropic.ApplyTo(c.Ai.Anthropic, _ai, "https://api.anthropic.com");
        OpenAi.ApplyTo(c.Ai.OpenAi, _ai, "https://api.openai.com");
        c.Ai.Transcribe = Transcribe;
        if (!string.IsNullOrWhiteSpace(TranscribeModel)) c.Ai.TranscribeModel = TranscribeModel.Trim();
        if (decimal.TryParse((TranscribePrice ?? "").Trim().Replace(',', '.'), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var tp) && tp >= 0)
            c.Ai.TranscribePricePerMinute = tp;
        if (TranscribeMaxSeconds > 0) c.Ai.TranscribeMaxSeconds = Math.Clamp(TranscribeMaxSeconds, 30, 900);
        _config.Save(c);
        await _repos.AuditAsync(User.Scope(), "ai.settings", $"{c.Ai.Provider} {c.Ai.Current.Model}", HttpContext.Connection.RemoteIpAddress?.ToString());
        TempData["Ok"] = Transcribe && string.IsNullOrEmpty(c.Ai.OpenAi.KeyEnc)
            ? "Impostazioni AI salvate. Attenzione: per trascrivere i vocali serve la chiave OpenAI (sezione OpenAI qui sopra)."
            : "Impostazioni AI salvate.";
        return Redirect("/Impostazioni/AI");
    }

    /// <summary>Una domanda brevissima al fornitore scelto, per controllare chiave e modello.</summary>
    public async Task<IActionResult> OnPostTestAsync()
    {
        var r = await _ai.ChatAsync("Rispondi solo con un oggetto JSON: {\"risposta\": \"ok\", \"esito\": \"in_corso\"}", new[] { new AiTurn("user", "Prova di collegamento") }, 50);
        await _convs.LogUsageAsync(null, null, null, "test", r, r.CostUsd(_ai.Settings));
        TempData[r.Ok ? "Ok" : "Err"] = r.Ok ? $"Collegamento riuscito con {r.Provider} ({r.Model})." : r.Error;
        return Redirect("/Impostazioni/AI");
    }

    public class ProviderInput
    {
        public string? Key { get; set; }
        public string Model { get; set; } = "";
        public string BaseUrl { get; set; } = "";
        // Testo e non numero: così "0,10" e "0.10" funzionano con qualsiasi impostazione di lingua del server.
        public string InputPrice { get; set; } = "0";
        public string OutputPrice { get; set; } = "0";
        public string CacheReadPrice { get; set; } = "0";

        private static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
        private static decimal Num(string? s) =>
            decimal.TryParse((s ?? "").Trim().Replace(',', '.'), System.Globalization.NumberStyles.Number, Inv, out var v) && v >= 0 ? v : 0;

        public static ProviderInput From(AiProviderSettings p) => new()
        {
            Model = p.Model, BaseUrl = p.BaseUrl, InputPrice = p.InputPrice.ToString(Inv),
            OutputPrice = p.OutputPrice.ToString(Inv), CacheReadPrice = p.CacheReadPrice.ToString(Inv)
        };

        public void ApplyTo(AiProviderSettings p, AiClient ai, string defaultUrl)
        {
            if (!string.IsNullOrWhiteSpace(Key)) p.KeyEnc = ai.Protect(Key); // vuoto = resta la chiave di prima
            if (!string.IsNullOrWhiteSpace(Model)) p.Model = Model.Trim();
            p.BaseUrl = Uri.TryCreate(BaseUrl, UriKind.Absolute, out _) ? BaseUrl.Trim().TrimEnd('/') : defaultUrl;
            p.InputPrice = Num(InputPrice); p.OutputPrice = Num(OutputPrice); p.CacheReadPrice = Num(CacheReadPrice);
        }
    }
}
