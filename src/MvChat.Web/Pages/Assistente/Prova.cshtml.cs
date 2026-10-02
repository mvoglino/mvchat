using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Ai;
using MvChat.Web.Catalog;
using MvChat.Web.Contacts;
using MvChat.Web.Data;
using MvChat.Web.Security;
using MvChat.Web.WhatsApp;

namespace MvChat.Web.Pages.Assistente;

/// <summary>Avvia una conversazione di prova: un "cliente" (di solito il proprio cellulare) riceve il primo messaggio e dialoga con l'assistente.</summary>
public class ProvaModel : PageModel
{
    private readonly Repos _repos; private readonly CatalogRepo _catalog; private readonly WaRepo _wa; private readonly WaService _send;
    private readonly ConversationRepo _convs; private readonly ContactsRepo _contacts; private readonly AiClient _ai;
    public ProvaModel(Repos repos, CatalogRepo catalog, WaRepo wa, WaService send, ConversationRepo convs, ContactsRepo contacts, AiClient ai)
    { _repos = repos; _catalog = catalog; _wa = wa; _send = send; _convs = convs; _contacts = contacts; _ai = ai; }

    public List<Gym> Gyms { get; private set; } = new();
    public List<GoalModel> Models { get; private set; } = new();
    public List<Offer> Offers { get; private set; } = new();
    public List<WaTemplate> Templates { get; private set; } = new();
    public WaNumber? Number { get; private set; }
    public bool AiEnabled => _ai.Enabled;
    public string? Error { get; private set; }

    [BindProperty(SupportsGet = true)] public int? Gym { get; set; }
    [BindProperty] public int ModelId { get; set; }
    [BindProperty] public int? OfferId { get; set; }
    [BindProperty] public int? TemplateId { get; set; }
    [BindProperty] public string? Phone { get; set; }
    [BindProperty] public string? Name { get; set; } = "Giulia";
    [BindProperty] public string? Membership { get; set; } = "Annuale";
    [BindProperty] public DateTime? ExpiresOn { get; set; }

    private async Task LoadAsync()
    {
        var me = User.Scope();
        Gyms = (await _repos.GymsAsync(me)).Where(g => g.IsActive).ToList();
        var gym = Gyms.FirstOrDefault(g => g.Id == Gym) ?? Gyms.FirstOrDefault();
        Gym = gym?.Id;
        Models = await _catalog.ModelsAsync(me, onlyActive: true);
        if (gym is null) return;
        Offers = (await _catalog.OffersAsync(me, gym.Id)).Where(o => o.Status == "Attiva").ToList();
        Number = await _wa.NumberForGymAsync(gym.Id);
        if (Number is not null) Templates = (await _wa.TemplatesAsync(gym.Id)).Where(t => t.IsApproved).ToList();
    }

    public async Task OnGetAsync() { await LoadAsync(); ExpiresOn ??= DateTime.UtcNow.Date.AddDays(14); }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        var gym = Gyms.FirstOrDefault(g => g.Id == Gym);
        var model = Models.FirstOrDefault(m => m.Id == ModelId);
        var offer = Offers.FirstOrDefault(o => o.Id == OfferId);
        var (phone, _) = ImportRules.NormalizePhone(Phone);
        if (gym is null || Number is null) Error = "La sede scelta non ha un numero WhatsApp collegato.";
        else if (model is null) Error = "Scegli un modello di obiettivo.";
        else if (model.NeedsOffer && offer is null) Error = "Questo modello ha bisogno di un'offerta attiva della sede.";
        else if (phone is null) Error = "Il cellulare non è valido.";
        else if (await _contacts.IsOptedOutAsync(gym.OrganizationId, phone)) Error = "Questo numero è nella lista STOP.";
        var template = Templates.FirstOrDefault(t => t.Id == TemplateId);
        if (Error is null && template is null && !Number!.IsSimulated) Error = "Con un numero Meta serve un template approvato per il primo messaggio.";
        if (Error is not null) return Page();

        var name = string.IsNullOrWhiteSpace(Name) ? "Giulia" : Name.Trim();
        var conv = new Conversation
        {
            OrganizationId = gym!.OrganizationId, GymId = gym.Id, WaNumberId = Number!.Id, ContactPhone = phone!, ContactName = name,
            Membership = Membership?.Trim(), ExpiresOn = ExpiresOn, GoalModelId = model!.Id, OfferId = offer?.Id, IsTest = true
        };
        conv.Id = await _convs.CreateAsync(conv);
        var values = new Dictionary<string, string?>
        {
            ["nome"] = name, ["cognome"] = "", ["abbonamento"] = Membership, ["scadenza"] = ExpiresOn?.ToString("dd/MM/yyyy"),
            ["palestra"] = gym.Name, ["offerta"] = offer?.Title
        };
        if (template is not null)
        {
            var r = await _send.SendTemplateAsync(Number, template, phone!, values, User.Scope().UserId, conv.Id);
            if (!r.Ok) { await _convs.SetStateAsync(conv.Id, "chiusa", Outcomes.Operatore, "primo messaggio non inviato: " + r.Error); TempData["Err"] = "Primo messaggio non inviato: " + r.Error; }
        }
        else
        {
            // Numero simulato senza template: si usa il testo suggerito dal modello.
            var text = TemplateText.Fill(model.TemplateSuggestion ?? "Ciao {{nome}}!", values);
            await _wa.InsertMessageAsync(Number, phone!, "out", "template", text, "(testo del modello)", "sim-" + Guid.NewGuid().ToString("N"), "delivered", null, User.Scope().UserId, conv.Id);
        }
        await _repos.AuditAsync(User.Scope(), "assistant.test.started", $"{phone} · {model.Name}", HttpContext.Connection.RemoteIpAddress?.ToString(), gym.OrganizationId, gym.Id);
        return Redirect($"/Conversazioni/{conv.Id}");
    }
}
