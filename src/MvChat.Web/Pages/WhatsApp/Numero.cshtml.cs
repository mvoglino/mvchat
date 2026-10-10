using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Contacts;
using MvChat.Web.Data;
using MvChat.Web.Security;
using MvChat.Web.WhatsApp;

namespace MvChat.Web.Pages.WhatsApp;

public class NumeroModel : PageModel
{
    private readonly Repos _repos;
    private readonly WaRepo _wa;
    private readonly WaService _service;
    private readonly WebhookHandler _hook;
    private readonly ContactsRepo _contacts;
    public NumeroModel(Repos repos, WaRepo wa, WaService service, WebhookHandler hook, ContactsRepo contacts)
    { _repos = repos; _wa = wa; _service = service; _hook = hook; _contacts = contacts; }

    public Gym Gym { get; private set; } = null!;
    public WaNumber? Number { get; private set; }
    public List<WaTemplate> Approved { get; private set; } = new();
    public List<WaMessage> Messages { get; private set; } = new();
    public Scope Me { get; private set; } = new();
    public bool CanConfigure => Me.CanManageGyms;
    public string? Error { get; private set; }

    // Collegamento
    [BindProperty] public string Mode { get; set; } = "simulato";
    [BindProperty] public string? DisplayPhone { get; set; }
    [BindProperty] public string? DisplayName { get; set; }
    [BindProperty] public string? PhoneNumberId { get; set; }
    [BindProperty] public string? WabaId { get; set; }
    [BindProperty] public string? AccessToken { get; set; }
    // Prova e simulatore
    [BindProperty] public int TemplateId { get; set; }
    [BindProperty] public string? To { get; set; }
    [BindProperty] public string? Nome { get; set; }
    [BindProperty] public string? Text { get; set; }

    private async Task<bool> LoadAsync(int id)
    {
        Me = User.Scope();
        var g = await _repos.GymAsync(Me, id);
        if (g is null) return false;
        Gym = g;
        Number = await _wa.NumberForGymAsync(id);
        if (Number is not null)
        {
            Approved = (await _wa.TemplatesAsync(id)).Where(t => t.IsApproved).ToList();
            Messages = await _wa.MessagesAsync(id, 50);
        }
        return true;
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (Number is not null)
        {
            Mode = Number.IsSimulated ? "simulato" : "meta";
            DisplayPhone = Number.DisplayPhone; DisplayName = Number.DisplayName; PhoneNumberId = Number.IsSimulated ? null : Number.PhoneNumberId; WabaId = Number.WabaId;
        }
        DisplayName ??= Gym.Name;
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!CanConfigure) return Forbid();
        var (phone, _) = ImportRules.NormalizePhone(DisplayPhone);
        // Il numero di un'attività può essere anche un fisso: qui basta che sia un numero plausibile.
        var display = phone ?? (DisplayPhone ?? "").Trim();
        if (display.Count(char.IsDigit) < 6) { Error = "Indica il numero di telefono."; return Page(); }

        var n = Number ?? new WaNumber { OrganizationId = Gym.OrganizationId, GymId = Gym.Id };
        n.DisplayPhone = display;
        n.DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? Gym.Name : DisplayName.Trim();
        n.IsSimulated = Mode != "meta";
        if (n.IsSimulated)
        {
            n.PhoneNumberId = $"sim-{Gym.Id}"; n.WabaId = null; n.AccessTokenEnc = null; n.Status = "attivo";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(PhoneNumberId) || !PhoneNumberId.Trim().All(char.IsDigit)) { Error = "L'identificativo del numero (Phone number ID) è fatto solo di cifre: copialo dalla pagina Configurazione API di Meta."; return Page(); }
            if (string.IsNullOrWhiteSpace(WabaId) || !WabaId.Trim().All(char.IsDigit)) { Error = "L'identificativo dell'account WhatsApp (WABA ID) è fatto solo di cifre."; return Page(); }
            if (string.IsNullOrWhiteSpace(AccessToken) && (n.IsSimulated || !n.HasToken)) { Error = "Inserisci la chiave di accesso."; return Page(); }
            if (!string.IsNullOrWhiteSpace(AccessToken) && (AccessToken.Trim().Contains('@') || AccessToken.Trim().Any(char.IsWhiteSpace) || AccessToken.Trim().Length < 10))
            { Error = "La chiave di accesso non sembra quella di Meta (di solito inizia con «EAA» ed è molto lunga): ricopiala dall'utente di sistema. Attenzione al browser che a volte inserisce da solo la password."; return Page(); }
            n.PhoneNumberId = PhoneNumberId.Trim(); n.WabaId = WabaId.Trim();
            if (!string.IsNullOrWhiteSpace(AccessToken)) n.AccessTokenEnc = _service.Protect(AccessToken);
            n.Status = "da controllare";
        }
        try { n.Id = await _wa.SaveNumberAsync(n); }
        catch (Exception ex) when (ex.Message.Contains("Duplicate", StringComparison.OrdinalIgnoreCase))
        {
            Error = "Questo identificativo è già collegato a un'altra attività."; return Page();
        }
        await _repos.AuditAsync(Me, "wa.number.saved", $"{n.DisplayPhone} ({(n.IsSimulated ? "simulato" : "Meta")})", HttpContext.Connection.RemoteIpAddress?.ToString(), Gym.OrganizationId, Gym.Id);
        var saved = await _wa.NumberAsync(n.Id);
        var err = await _service.CheckNumberAsync(saved!);
        TempData[err is null ? "Ok" : "Err"] = err is null ? "Numero salvato e controllato." : "Numero salvato, ma Meta segnala un problema: " + err;
        return Redirect($"/WhatsApp/Numero/{id}");
    }

    public async Task<IActionResult> OnPostCheckAsync(int id)
    {
        if (!await LoadAsync(id) || Number is null) return NotFound();
        var err = await _service.CheckNumberAsync(Number);
        TempData[err is null ? "Ok" : "Err"] = err ?? "Collegamento controllato: tutto a posto.";
        return Redirect($"/WhatsApp/Numero/{id}");
    }

    public async Task<IActionResult> OnPostTestAsync(int id)
    {
        if (!await LoadAsync(id) || Number is null) return NotFound();
        var t = Approved.FirstOrDefault(x => x.Id == TemplateId);
        if (t is null) { Error = "Scegli un template approvato."; return Page(); }
        var (phone, _) = ImportRules.NormalizePhone(To);
        if (phone is null) { Error = "Il numero a cui scrivere non è un cellulare valido."; return Page(); }
        if (await _contacts.IsOptedOutAsync(Gym.OrganizationId, phone)) { Error = "Questo numero è nella lista STOP: non gli si può scrivere."; return Page(); }
        var values = new Dictionary<string, string?>
        {
            ["nome"] = string.IsNullOrWhiteSpace(Nome) ? "Giulia" : Nome.Trim(), ["cognome"] = "",
            ["abbonamento"] = "Annuale", ["scadenza"] = DateTime.UtcNow.AddDays(14).ToString("dd/MM/yyyy"),
            ["palestra"] = Gym.Name, ["offerta"] = "offerta di prova",
            ["servizio"] = TemplateText.Placeholders["servizio"], ["note"] = TemplateText.Placeholders["note"]
        };
        var r = await _service.SendTemplateAsync(Number, t, phone, values, Me.UserId);
        TempData[r.Ok ? "Ok" : "Err"] = r.Ok ? $"Messaggio di prova inviato a {phone}." : "Invio non riuscito: " + r.Error;
        return Redirect($"/WhatsApp/Numero/{id}");
    }

    /// <summary>Solo numeri simulati: finge che un cliente abbia risposto, passando dallo stesso percorso dei messaggi veri.</summary>
    public async Task<IActionResult> OnPostSimulateAsync(int id)
    {
        if (!await LoadAsync(id) || Number is null) return NotFound();
        if (!Number.IsSimulated) return BadRequest();
        var (phone, _) = ImportRules.NormalizePhone(To);
        if (phone is null || string.IsNullOrWhiteSpace(Text)) { Error = "Indica un cellulare valido e il testo del messaggio."; return Page(); }
        await _hook.ProcessAsync(WebhookHandler.SimulatedInbound(Number.PhoneNumberId!, phone, Text));
        TempData["Ok"] = "Messaggio simulato ricevuto.";
        return Redirect($"/WhatsApp/Numero/{id}");
    }
}
