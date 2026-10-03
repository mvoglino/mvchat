using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Data;
using MvChat.Web.Infrastructure;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Impostazioni;

/// <summary>Regole di fatturazione di MVitalia: canone di base, ricarico sull'AI, cambio dollaro/euro, IVA.</summary>
public class FatturazioneModel : PageModel
{
    private readonly AppConfigStore _config; private readonly Repos _repos;
    public FatturazioneModel(AppConfigStore config, Repos repos) { _config = config; _repos = repos; }

    private static readonly CultureInfo It = CultureInfo.GetCultureInfo("it-IT");
    // Testo e non numero: così "0,92" e "0.92" funzionano con qualsiasi impostazione di lingua del server.
    [BindProperty] public string? DefaultFee { get; set; }
    [BindProperty] public string? Markup { get; set; }
    [BindProperty] public string? UsdToEur { get; set; }
    [BindProperty] public string? Vat { get; set; }
    [BindProperty] public string? IssuerName { get; set; }
    [BindProperty] public string? IssuerDetails { get; set; }
    public string? Error { get; private set; }

    private static decimal? Num(string? s, decimal max) =>
        MvChat.Web.Infrastructure.Money.Parse(s) is decimal v && v >= 0 && v <= max ? v : null;

    public void OnGet()
    {
        var b = _config.Current.Billing;
        DefaultFee = b.DefaultMonthlyFeeEur.ToString("0.00", It); Markup = b.AiMarkupPct.ToString("0.##", It);
        UsdToEur = b.UsdToEur.ToString("0.####", It); Vat = b.VatPct.ToString("0.##", It);
        IssuerName = b.IssuerName; IssuerDetails = b.IssuerDetails;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var fee = Num(DefaultFee, 100000); var mk = Num(Markup, 500); var fx = Num(UsdToEur, 10); var vat = Num(Vat, 100);
        Error = fee is null ? "Canone di base non valido (es. 49,90)." : mk is null ? "Ricarico non valido (es. 20)."
              : fx is null || fx == 0 ? "Cambio non valido (es. 0,92)." : vat is null ? "IVA non valida (es. 22)." : null;
        if (Error is not null) return Page();
        var c = _config.Current;
        c.Billing.DefaultMonthlyFeeEur = Math.Round(fee!.Value, 2); c.Billing.AiMarkupPct = mk!.Value; c.Billing.UsdToEur = fx!.Value; c.Billing.VatPct = vat!.Value;
        c.Billing.IssuerName = string.IsNullOrWhiteSpace(IssuerName) ? "MVitalia" : IssuerName.Trim();
        c.Billing.IssuerDetails = string.IsNullOrWhiteSpace(IssuerDetails) ? null : IssuerDetails.Trim()[..Math.Min(IssuerDetails.Trim().Length, 500)];
        _config.Save(c);
        await _repos.AuditAsync(User.Scope(), "billing.settings", $"canone {fee} · ricarico {mk}% · cambio {fx} · IVA {vat}%", HttpContext.Connection.RemoteIpAddress?.ToString());
        TempData["Ok"] = "Impostazioni di fatturazione salvate. Valgono per i mesi non ancora chiusi.";
        return Redirect("/Impostazioni/Fatturazione");
    }
}
