using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Catalog;
using MvChat.Web.Data;
using MvChat.Web.Infrastructure;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Modelli;

/// <summary>Mostra cosa riceverà l'assistente AI per una combinazione di modello, attività e offerta.</summary>
public class AnteprimaModel : PageModel
{
    private readonly CatalogRepo _catalog;
    private readonly Repos _repos;
    public AnteprimaModel(CatalogRepo catalog, Repos repos) { _catalog = catalog; _repos = repos; }

    public List<GoalModel> Models { get; private set; } = new();
    public List<Gym> Gyms { get; private set; } = new();
    public List<Offer> Offers { get; private set; } = new();
    [BindProperty(SupportsGet = true)] public int? Model { get; set; }
    [BindProperty(SupportsGet = true)] public int? Gym { get; set; }
    [BindProperty(SupportsGet = true)] public int? Offer { get; set; }
    [BindProperty(SupportsGet = true)] public string Nome { get; set; } = "Giulia";
    [BindProperty(SupportsGet = true)] public string? Abbonamento { get; set; } = "Annuale";
    [BindProperty(SupportsGet = true)] public DateTime? Scadenza { get; set; }

    public string? Prompt { get; private set; }
    public string? FirstMessage { get; private set; }
    public List<string> Warnings { get; } = new();

    public async Task OnGetAsync()
    {
        var me = User.Scope();
        Models = await _catalog.ModelsAsync(me, onlyActive: true);
        Gyms = (await _repos.GymsAsync(me)).Where(g => g.IsActive).ToList();
        var model = Models.FirstOrDefault(m => m.Id == Model) ?? Models.FirstOrDefault();
        var gym = Gyms.FirstOrDefault(g => g.Id == Gym) ?? Gyms.FirstOrDefault();
        if (model is null || gym is null) return;
        Model = model.Id; Gym = gym.Id;
        // Le offerte proposte sono solo quelle dell'attività scelta: niente prezzi di altre attività.
        Offers = await _catalog.OffersAsync(me, gym.Id);
        var offer = Offers.FirstOrDefault(o => o.Id == Offer);
        Offer = offer?.Id;

        var today = DateTime.UtcNow.ToRome().Date;
        Scadenza ??= today.AddDays(14);
        var profile = await _catalog.ProfileAsync(gym.Id);
        var who = new Recipient(string.IsNullOrWhiteSpace(Nome) ? "Giulia" : Nome.Trim(), null, Abbonamento, Scadenza);

        if (model.NeedsOffer && offer is null) Warnings.Add("Questo modello ha bisogno di un'offerta: senza, l'assistente non potrà proporre nulla di concreto.");
        if (offer is not null && offer.Status != "Attiva") Warnings.Add($"L'offerta scelta è «{offer.Status.ToLower()}»: in una campagna vera non verrebbe accettata.");
        if (profile.Completeness < 60) Warnings.Add($"La scheda di {gym.Name} è compilata al {profile.Completeness}%: l'assistente saprà rispondere a poche domande.");

        Prompt = PromptBuilder.Build(model, gym.Name, profile, offer, who, today, await _catalog.ActivityInfoAsync(gym.Id));
        FirstMessage = PromptBuilder.FillTemplate(model.TemplateSuggestion, who, gym.Name, offer);
    }
}
