using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Campaigns;
using MvChat.Web.Catalog;
using MvChat.Web.Contacts;
using MvChat.Web.Data;
using MvChat.Web.Infrastructure;
using MvChat.Web.Security;
using MvChat.Web.WhatsApp;

namespace MvChat.Web.Pages.Campagne;

public class NuovaModel : PageModel
{
    private readonly Repos _repos; private readonly ContactsRepo _contacts; private readonly CatalogRepo _catalog; private readonly WaRepo _wa; private readonly CampaignRepo _repo;
    public NuovaModel(Repos repos, ContactsRepo contacts, CatalogRepo catalog, WaRepo wa, CampaignRepo repo)
    { _repos = repos; _contacts = contacts; _catalog = catalog; _wa = wa; _repo = repo; }

    public List<Gym> Gyms { get; private set; } = new();
    public List<ContactList> Lists { get; private set; } = new();
    public List<GoalModel> Models { get; private set; } = new();
    public List<Offer> Offers { get; private set; } = new();
    public List<WaTemplate> Templates { get; private set; } = new();
    public WaNumber? Number { get; private set; }
    public string Windows { get; private set; } = "";
    public string? Error { get; private set; }

    [BindProperty(SupportsGet = true)] public int? Gym { get; set; }
    [BindProperty] public string? Name { get; set; }
    [BindProperty] public int ListId { get; set; }
    [BindProperty] public int ModelId { get; set; }
    [BindProperty] public int? OfferId { get; set; }
    [BindProperty] public int TemplateId { get; set; }
    [BindProperty] public string When { get; set; } = "subito";
    [BindProperty] public DateTime? StartAt { get; set; }
    [BindProperty] public int? DailyLimit { get; set; }
    [BindProperty] public string? ExtraInstructions { get; set; }
    /// <summary>Di base sì: a chi rifiuta senza dire perché, l'assistente chiede il motivo una volta.</summary>
    [BindProperty] public bool AskRefusalReason { get; set; } = true;
    /// <summary>Promemoria a chi non risponde: un secondo template approvato (vuoto = nessun promemoria) dopo 1–7 giorni.</summary>
    [BindProperty] public int? FollowUpTemplateId { get; set; }
    [BindProperty] public int FollowUpDays { get; set; } = 3;
    public const int ExtraMax = 1500;

    private async Task<Gym?> LoadAsync()
    {
        var me = User.Scope();
        Gyms = (await _repos.GymsAsync(me)).Where(g => g.IsActive).ToList();
        var g = Gyms.FirstOrDefault(x => x.Id == Gym) ?? Gyms.FirstOrDefault();
        Gym = g?.Id;
        Models = await _catalog.ModelsAsync(me, onlyActive: true);
        if (g is null) return null;
        Lists = (await _contacts.ListsAsync(me)).Where(l => l.GymId == g.Id && l.ValidCount > 0).ToList();
        Offers = (await _catalog.OffersAsync(me, g.Id)).Where(o => o.Status == "Attiva").ToList();
        Number = await _wa.NumberForGymAsync(g.Id);
        if (Number is not null) Templates = (await _wa.TemplatesAsync(g.Id)).Where(t => t.IsApproved).ToList();
        Windows = SendWindows.Describe(await _repo.WindowsAsync(g.Id));
        return g;
    }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        var g = await LoadAsync();
        var list = Lists.FirstOrDefault(l => l.Id == ListId);
        var model = Models.FirstOrDefault(m => m.Id == ModelId);
        var offer = Offers.FirstOrDefault(o => o.Id == OfferId);
        var template = Templates.FirstOrDefault(t => t.Id == TemplateId);
        var followUp = FollowUpTemplateId is int fid ? Templates.FirstOrDefault(t => t.Id == fid) : null;
        DateTime? startUtc = null;
        if (When == "data" && StartAt is DateTime local)
        {
            startUtc = local.FromRome(); // l'orario scritto in pagina è quello italiano
        }
        Error =
            g is null || Number is null ? "L'attività scelta non ha un numero WhatsApp collegato."
            : string.IsNullOrWhiteSpace(Name) ? "Dai un nome alla campagna (es. «Rinnovi ottobre»)."
            : list is null ? "Scegli una lista contatti dell'attività."
            : model is null ? "Scegli l'obiettivo."
            : model.NeedsOffer && offer is null ? "Questo obiettivo ha bisogno di un'offerta attiva dell'attività."
            : template is null ? "Scegli il template del primo messaggio (deve essere approvato da Meta)."
            : template.Variables.Contains("offerta") && offer is null ? "Il template cita l'offerta: collega un'offerta alla campagna."
            : When == "data" && (startUtc is null || startUtc < DateTime.UtcNow.AddMinutes(-5)) ? "Indica una data e ora di partenza futura."
            : DailyLimit is < 1 or > 100000 ? "Il limite giornaliero deve essere un numero tra 1 e 100.000 (oppure vuoto)."
            : FollowUpTemplateId is not null && followUp is null ? "Il template del promemoria deve essere approvato da Meta."
            : followUp is not null && followUp.Id == template.Id ? "Per il promemoria scegli un template diverso dal primo messaggio."
            : followUp is not null && followUp.Variables.Contains("offerta") && offer is null ? "Il promemoria cita l'offerta: collega un'offerta alla campagna."
            : FollowUpDays is < 1 or > 7 ? "Il promemoria parte dopo 1–7 giorni."
            : null;
        if (Error is not null) return Page();

        var me = User.Scope();
        var c = new Campaign
        {
            OrganizationId = g!.OrganizationId, GymId = g.Id, Name = Name!.Trim()[..Math.Min(150, Name.Trim().Length)], ListId = list!.Id, ListName = list.Name,
            GoalModelId = model!.Id, OfferId = offer?.Id, WaNumberId = Number!.Id, TemplateId = template!.Id, StartAt = startUtc, DailyLimit = DailyLimit,
            ExtraInstructions = string.IsNullOrWhiteSpace(ExtraInstructions) ? null : ExtraInstructions.Trim()[..Math.Min(ExtraInstructions.Trim().Length, ExtraMax)],
            AskRefusalReason = AskRefusalReason, FollowUpTemplateId = followUp?.Id, FollowUpDays = FollowUpDays
        };
        var (id, recipients, excluded) = await _repo.CreateAsync(c, me.UserId);
        await _repos.AuditAsync(me, "campaign.created", $"#{id} {c.Name} · {recipients} destinatari", HttpContext.Connection.RemoteIpAddress?.ToString(), g.OrganizationId, g.Id);
        TempData["Ok"] = $"Campagna creata con {recipients} destinatari" + (excluded > 0 ? $" ({excluded} esclusi perché nella lista STOP)" : "") + ". Controlla l'anteprima e avviala.";
        return Redirect($"/Campagne/{id}");
    }
}
