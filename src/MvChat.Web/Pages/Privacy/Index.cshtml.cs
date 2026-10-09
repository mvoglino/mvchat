using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Contacts;
using MvChat.Web.Data;
using MvChat.Web.Privacy;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Privacy;

/// <summary>Un cliente chiede quali dati abbiamo su di lui, o di cancellarli: si cerca il suo cellulare e si risponde da qui.</summary>
public class IndexModel : PageModel
{
    private readonly SubjectRequests _req; private readonly ContactsRepo _contacts; private readonly Repos _repos;
    public IndexModel(SubjectRequests req, ContactsRepo contacts, Repos repos) { _req = req; _contacts = contacts; _repos = repos; }

    [BindProperty(SupportsGet = true)] public string? Numero { get; set; }
    [BindProperty] public bool AddStop { get; set; } = true;
    public string? Phone { get; private set; }
    public string? Error { get; private set; }
    public List<FoundContact> Contacts { get; private set; } = new();
    public List<FoundConversation> Conversations { get; private set; } = new();
    public List<FoundOptOut> OptOuts { get; private set; } = new();
    public int Recipients { get; private set; }
    public int Messages { get; private set; }
    public bool Found => Contacts.Count + Conversations.Count + OptOuts.Count + Recipients + Messages > 0;

    private async Task<bool> LoadAsync()
    {
        if (string.IsNullOrWhiteSpace(Numero)) return false;
        var (phone, _) = ImportRules.NormalizePhone(Numero);
        if (phone is null) { Error = "Il numero non è un cellulare valido."; return false; }
        Phone = phone;
        var me = User.Scope();
        Contacts = await _req.ContactsAsync(me, phone);
        Conversations = await _req.ConversationsAsync(me, phone);
        OptOuts = await _req.OptOutsAsync(me, phone);
        Recipients = await _req.RecipientsCountAsync(me, phone);
        Messages = (await _req.MessagesAsync(me, phone)).Count;
        return true;
    }

    public async Task OnGetAsync() => await LoadAsync();

    /// <summary>Diritto di accesso: tutti i dati del numero in un file da consegnare al cliente.</summary>
    public async Task<IActionResult> OnGetExportAsync()
    {
        if (!await LoadAsync()) return Page();
        var me = User.Scope();
        var data = new
        {
            numero = Phone, estratto_il = DateTime.UtcNow,
            contatti = Contacts, conversazioni = Conversations, messaggi = await _req.MessagesAsync(me, Phone!), etichette = await _req.TagsAsync(me, Phone!),
            destinatario_campagne = Recipients, lista_stop = OptOuts
        };
        await _repos.AuditAsync(me, "privacy.export", SubjectRequests.Mask(Phone!), HttpContext.Connection.RemoteIpAddress?.ToString());
        var json = JsonSerializer.SerializeToUtf8Bytes(data, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        return File(json, "application/json", $"dati-{Phone!.TrimStart('+')}.json");
    }

    /// <summary>Diritto alla cancellazione. Di base il numero entra anche nella lista STOP, così non verrà ricontattato da un nuovo file Excel.</summary>
    public async Task<IActionResult> OnPostEraseAsync()
    {
        if (!await LoadAsync()) return Page();
        var me = User.Scope();
        // Gruppi/attività in cui il cliente compariva (più il proprio, se non è MVitalia): lì entra nella lista STOP.
        var orgs = Contacts.Select(c => c.OrganizationId).Concat(Conversations.Select(c => c.OrganizationId)).ToHashSet();
        if (me.OrganizationId is int own) orgs.Add(own);
        var (convs, msgs, contacts, recipients) = await _req.EraseAsync(me, Phone!);
        if (AddStop)
            foreach (var org in orgs)
                if (me.IsAreaManager && org == me.OrganizationId)
                    foreach (var g in me.AreaGymIds) await _contacts.AddOptOutAsync(org, g, Phone!, "Richiesta di cancellazione dei dati", "privacy", me.UserId);
                else
                    await _contacts.AddOptOutAsync(org, me.GymId, Phone!, "Richiesta di cancellazione dei dati", "privacy", me.UserId);
        await _repos.AuditAsync(me, "privacy.erase", $"{SubjectRequests.Mask(Phone!)}: conversazioni {convs}, messaggi {msgs}, contatti {contacts}, destinatari {recipients}",
            HttpContext.Connection.RemoteIpAddress?.ToString(), me.OrganizationId, me.GymId);
        TempData["Ok"] = $"Dati cancellati: {convs} conversazioni, {msgs} messaggi, {contacts} righe di liste, {recipients} invii di campagne." + (AddStop && orgs.Count > 0 ? " Il numero è nella lista STOP." : "");
        return Redirect("/Privacy?numero=" + Uri.EscapeDataString(Phone!));
    }
}
