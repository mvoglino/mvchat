using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Billing;
using MvChat.Web.Data;
using MvChat.Web.Infrastructure;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Fatturazione;

/// <summary>
/// Rendiconti mensili da fatturare. MVitalia vede e chiude tutti i mesi; il gruppo vede i suoi rendiconti,
/// l'attività singola il suo. Non è una fattura elettronica: è la base per farla nel programma di contabilità.
/// </summary>
public class IndexModel : PageModel
{
    private readonly BillingService _billing; private readonly Repos _repos;
    public IndexModel(BillingService billing, Repos repos) { _billing = billing; _repos = repos; }

    public Scope Me { get; private set; } = new();
    public string Month { get; private set; } = "";
    public List<string> Months { get; } = new();
    public List<Statement> Items { get; private set; } = new();
    public bool IsPastMonth { get; private set; }

    private async Task<bool> LoadAsync(string? mese)
    {
        Me = User.Scope();
        if (Me.Role == Roles.Operator) return false;
        var now = DateTime.UtcNow.ToRome();
        var current = new DateTime(now.Year, now.Month, 1);
        for (var i = 0; i < 18; i++) Months.Add(current.AddMonths(-i).ToString("yyyy-MM"));
        // Di base il mese scorso: è quello da fatturare.
        Month = BillingService.TryMonth(mese, out var m) && m <= current ? m.ToString("yyyy-MM") : current.AddMonths(-1).ToString("yyyy-MM");
        IsPastMonth = BillingService.TryMonth(Month, out var mm) && mm < current;
        Items = await _billing.MonthAsync(Me, Month);
        return true;
    }

    public async Task<IActionResult> OnGetAsync(string? mese) => await LoadAsync(mese) ? Page() : Forbid();

    public async Task<IActionResult> OnPostCloseAsync(string? mese)
    {
        if (!await LoadAsync(mese)) return Forbid();
        if (!Me.IsSuperAdmin) return Forbid();
        if (!IsPastMonth) { TempData["Err"] = "Si può chiudere solo un mese già finito."; return Redirect($"/Fatturazione?mese={Month}"); }
        // Senza ragione sociale e partita IVA il rendiconto non si può fatturare: prima si completano i dati.
        var missing = (await _billing.MonthAsync(Me, Month)).Where(x => !x.Closed && x.MissingData).Select(x => x.RecipientName).ToList();
        if (missing.Count > 0)
        {
            TempData["Err"] = "Prima di chiudere il mese completa ragione sociale e partita IVA di: " + string.Join(", ", missing) + ".";
            return Redirect($"/Fatturazione?mese={Month}");
        }
        var n = await _billing.CloseAsync(Me, Month, Me.UserId);
        await _repos.AuditAsync(Me, "billing.closed", $"{Month}: {n} rendiconti", HttpContext.Connection.RemoteIpAddress?.ToString());
        TempData["Ok"] = n == 0 ? "Nessun rendiconto da chiudere." : $"Mese chiuso: {n} rendiconti salvati. Da adesso non cambiano più.";
        return Redirect($"/Fatturazione?mese={Month}");
    }

    public async Task<IActionResult> OnPostReopenAsync(string? mese, int org)
    {
        if (!await LoadAsync(mese)) return Forbid();
        if (!Me.IsSuperAdmin) return Forbid();
        await _billing.ReopenAsync(Month, org);
        await _repos.AuditAsync(Me, "billing.reopened", $"{Month} · cliente {org}", HttpContext.Connection.RemoteIpAddress?.ToString(), org);
        TempData["Ok"] = "Rendiconto riaperto: ora si ricalcola con le regole attuali.";
        return Redirect($"/Fatturazione?mese={Month}");
    }

    /// <summary>Tutte le righe del mese per il programma di contabilità (si apre con Excel).</summary>
    public async Task<IActionResult> OnGetCsvAsync(string? mese)
    {
        if (!await LoadAsync(mese)) return Forbid();
        var it = CultureInfo.GetCultureInfo("it-IT");
        static string C(string? v) => Csv.Cell(v);
        var sb = new StringBuilder();
        sb.AppendLine("Mese;Intestatario;Ragione sociale;Partita IVA;Indirizzo;Email;Attività;Voce;Descrizione;Importo (€);IVA %;Stato");
        foreach (var s in Items)
            foreach (var l in s.Lines)
                sb.AppendLine(string.Join(";", C(s.Month), C(s.RecipientName), C(s.LegalName), C(s.VatNumber), C(s.Address), C(s.Email), C(l.Activity),
                    C(l.Kind == "canone" ? "Abbonamento" : "Consumo AI"), C(l.Description), l.Amount.ToString("0.00", it), s.VatPct.ToString("0.##", it), C(s.Closed ? "chiuso" : "provvisorio")));
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"rendiconti-{Month}.csv");
    }
}
