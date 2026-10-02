using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Ai;
using MvChat.Web.Contacts;
using MvChat.Web.Data;
using MvChat.Web.Security;
using MvChat.Web.WhatsApp;

namespace MvChat.Web.Pages.Conversazioni;

/// <summary>
/// Una conversazione: la chat, l'esito e i comandi per la reception
/// (prendere in mano, rispondere, restituire all'assistente, cambiare l'esito).
/// </summary>
public class DettaglioModel : PageModel
{
    private readonly ConversationRepo _convs; private readonly WaRepo _wa; private readonly WaService _send;
    private readonly WebhookHandler _hook; private readonly ContactsRepo _contacts; private readonly AiQueue _queue; private readonly Repos _repos;
    private readonly QuickReplyRepo _quick;
    public DettaglioModel(ConversationRepo convs, WaRepo wa, WaService send, WebhookHandler hook, ContactsRepo contacts, AiQueue queue, Repos repos, QuickReplyRepo quick)
    { _convs = convs; _wa = wa; _send = send; _hook = hook; _contacts = contacts; _queue = queue; _repos = repos; _quick = quick; }

    public List<QuickReply> QuickReplies { get; private set; } = new();
    public List<UserRow> Colleagues { get; private set; } = new();
    [BindProperty] public int? AssignTo { get; set; }

    public Conversation Conv { get; private set; } = null!;
    public WaNumber? Number { get; private set; }
    public List<ConvMessage> Messages { get; private set; } = new();
    public Scope Me { get; private set; } = new();
    public string? Error { get; private set; }

    [BindProperty] public string? Text { get; set; }
    [BindProperty] public string? Outcome { get; set; }
    [BindProperty] public string? Note { get; set; }

    /// <summary>Con un numero Meta si può scrivere liberamente solo entro 24 ore dall'ultimo messaggio del cliente.</summary>
    public bool WindowOpen => Number?.IsSimulated == true || (Conv.LastInboundAt is DateTime t && t > DateTime.UtcNow.AddHours(-24));
    public long LastMessageId => Messages.Count == 0 ? 0 : Messages[^1].Id;

    private async Task<bool> LoadAsync(long id)
    {
        Me = User.Scope();
        var c = await _convs.GetAsync(Me, id); // il perimetro (struttura/sede) è nella query
        if (c is null) return false;
        Conv = c;
        Number = await _wa.NumberAsync(c.WaNumberId);
        Messages = await _convs.MessagesAsync(id);
        QuickReplies = await _quick.ForGymAsync(c.OrganizationId, c.GymId);
        if (Me.CanManageUsers)
            Colleagues = (await _repos.UsersAsync(Me)).Where(u => u.IsActive && u.GymId == c.GymId).OrderBy(u => u.FullName).ToList();
        return true;
    }

    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
    private IActionResult Back() => Redirect($"/Conversazioni/{Conv.Id}");

    public async Task<IActionResult> OnGetAsync(long id) => await LoadAsync(id) ? Page() : NotFound();

    /// <summary>Piccolo controllo per la pagina aperta: se arriva un messaggio nuovo, si ricarica da sola.</summary>
    public async Task<IActionResult> OnGetStateAsync(long id)
    {
        var c = await _convs.GetAsync(User.Scope(), id);
        if (c is null) return NotFound();
        var last = (await _convs.MessagesAsync(id, 1)).LastOrDefault()?.Id ?? 0;
        return new JsonResult(new { last, writing = c.AiWriting, status = c.Status, outcome = c.Outcome });
    }

    /// <summary>Una persona prende in mano la conversazione: l'assistente smette di rispondere.</summary>
    public async Task<IActionResult> OnPostTakeAsync(long id)
    {
        if (!await LoadAsync(id)) return NotFound();
        await _convs.SetStateAsync(id, "operatore", Conv.Outcome is Outcomes.InCorso ? Outcomes.Operatore : Conv.Outcome, $"presa in carico da {Me.Name}", Me.UserId);
        await _repos.AuditAsync(Me, "conversation.take", $"#{id} {Conv.ContactPhone}", Ip, Conv.OrganizationId, Conv.GymId);
        TempData["Ok"] = Conv.Status == "ai" ? "Ora la conversazione è tua: l'assistente non risponde più." : "Conversazione presa in carico.";
        return Back();
    }

    /// <summary>La lascio libera: torna tra quelle «da gestire» per tutti i colleghi.</summary>
    public async Task<IActionResult> OnPostReleaseAsync(long id)
    {
        if (!await LoadAsync(id)) return NotFound();
        await _convs.AssignAsync(id, null);
        await _repos.AuditAsync(Me, "conversation.release", $"#{id} {Conv.ContactPhone}", Ip, Conv.OrganizationId, Conv.GymId);
        TempData["Ok"] = "Conversazione lasciata libera per i colleghi.";
        return Back();
    }

    /// <summary>Il responsabile assegna la conversazione a un collega della sede.</summary>
    public async Task<IActionResult> OnPostAssignAsync(long id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!Me.CanManageUsers) return Forbid();
        var who = Colleagues.FirstOrDefault(u => u.Id == AssignTo);
        if (who is null) { Error = "Scegli un collega della sede."; return Page(); }
        await _convs.SetStateAsync(id, "operatore", Conv.Outcome is Outcomes.InCorso ? Outcomes.Operatore : Conv.Outcome, $"assegnata a {who.FullName}", who.Id);
        await _convs.AssignAsync(id, who.Id);
        await _repos.AuditAsync(Me, "conversation.assign", $"#{id} → {who.FullName}", Ip, Conv.OrganizationId, Conv.GymId);
        TempData["Ok"] = $"Conversazione assegnata a {who.FullName}.";
        return Back();
    }

    public async Task<IActionResult> OnPostGiveBackAsync(long id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (Conv.Outcome == Outcomes.OptOut) { TempData["Err"] = "Il cliente ha chiesto di non essere più contattato: la conversazione resta chiusa."; return Back(); }
        await _convs.GiveBackToAiAsync(id);
        _queue.Enqueue(id); // se c'è un messaggio del cliente in attesa, l'assistente risponde subito
        await _repos.AuditAsync(Me, "conversation.giveback", $"#{id} {Conv.ContactPhone}", Ip, Conv.OrganizationId, Conv.GymId);
        TempData["Ok"] = "Conversazione restituita all'assistente.";
        return Back();
    }

    /// <summary>Risposta scritta da una persona della sede.</summary>
    public async Task<IActionResult> OnPostReplyAsync(long id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (string.IsNullOrWhiteSpace(Text)) { Error = "Scrivi il messaggio."; return Page(); }
        if (Text.Length > 1000) { Error = "Il messaggio è troppo lungo (massimo 1000 caratteri)."; return Page(); }
        if (Number is null) { Error = "Il numero WhatsApp della sede non è più collegato."; return Page(); }
        if (await _contacts.IsOptedOutAsync(Conv.OrganizationId, Conv.ContactPhone)) { Error = "Il cliente è nella lista STOP: non gli si può scrivere."; return Page(); }
        if (!WindowOpen) { Error = "Sono passate più di 24 ore dall'ultimo messaggio del cliente: WhatsApp permette solo un template approvato."; return Page(); }
        var r = await _send.SendTextAsync(Number, Conv.ContactPhone, Text.Trim(), Me.UserId, id);
        if (!r.Ok) { Error = "Invio non riuscito: " + r.Error; return Page(); }
        // Chi risponde a mano prende in carico la conversazione, così l'assistente non si sovrappone.
        if (Conv.Status != "operatore") await _convs.SetStateAsync(id, "operatore", Conv.Outcome is Outcomes.InCorso ? Outcomes.Operatore : Conv.Outcome, null, Me.UserId);
        else if (Conv.AssignedUserId is null) await _convs.AssignAsync(id, Me.UserId);
        await _convs.TouchAsync(id);
        return Back();
    }

    /// <summary>Esito deciso dalla reception (es. il cliente ha rinnovato al banco).</summary>
    public async Task<IActionResult> OnPostOutcomeAsync(long id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (Outcome is null || !Outcomes.All.Contains(Outcome)) { Error = "Scegli un esito."; return Page(); }
        var status = Outcome switch { Outcomes.InCorso => Conv.Status == "chiusa" ? "operatore" : Conv.Status, Outcomes.Operatore => "operatore", _ => "chiusa" };
        var note = string.IsNullOrWhiteSpace(Note) ? $"esito impostato da {Me.Name}" : Note.Trim()[..Math.Min(Note.Trim().Length, 480)];
        await _convs.SetStateAsync(id, status, Outcome, note, Me.UserId);
        if (Outcome == Outcomes.OptOut)
            await _contacts.AddOptOutAsync(Conv.OrganizationId, Conv.GymId, Conv.ContactPhone, "Dalla conversazione: " + note, "operatore", Me.UserId);
        await _repos.AuditAsync(Me, "conversation.outcome", $"#{id} {Outcome}", Ip, Conv.OrganizationId, Conv.GymId);
        TempData["Ok"] = "Esito aggiornato.";
        return Back();
    }

    /// <summary>Solo numeri simulati: scrivi tu al posto del cliente. Passa dallo stesso percorso dei messaggi veri.</summary>
    public async Task<IActionResult> OnPostSimulateAsync(long id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (Number is null || !Number.IsSimulated) return BadRequest();
        if (string.IsNullOrWhiteSpace(Text)) { Error = "Scrivi il messaggio del cliente."; return Page(); }
        await _hook.ProcessAsync(WebhookHandler.SimulatedInbound(Number.PhoneNumberId!, Conv.ContactPhone, Text));
        return Back();
    }
}
