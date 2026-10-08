using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Ai;
using MvChat.Web.Catalog;
using MvChat.Web.Data;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Assistente;

/// <summary>Banco di prova: l'assistente parla con clienti simulati e mvchat controlla le risposte. Nessun messaggio esce su WhatsApp.</summary>
public class BancoModel : PageModel
{
    private readonly Repos _repos; private readonly CatalogRepo _catalog; private readonly AssistantService _assistant;
    private readonly AiClient _ai; private readonly ConversationRepo _convs;
    public BancoModel(Repos repos, CatalogRepo catalog, AssistantService assistant, AiClient ai, ConversationRepo convs)
    { _repos = repos; _catalog = catalog; _assistant = assistant; _ai = ai; _convs = convs; }

    public List<Gym> Gyms { get; private set; } = new();
    public List<GoalModel> Models { get; private set; } = new();
    public List<Offer> Offers { get; private set; } = new();
    public bool AiEnabled => _ai.Enabled;
    public string AiName => $"{_ai.Provider} · {_ai.Settings.Model}";
    public int? Gym { get; private set; }

    private async Task<Gym?> LoadAsync(int? gym)
    {
        var me = User.Scope();
        Gyms = (await _repos.GymsAsync(me)).Where(g => g.IsActive).ToList();
        var g = Gyms.FirstOrDefault(x => x.Id == gym) ?? Gyms.FirstOrDefault();
        Gym = g?.Id;
        Models = await _catalog.ModelsAsync(me, onlyActive: true);
        if (g is not null) Offers = (await _catalog.OffersAsync(me, g.Id)).Where(o => o.Status == "Attiva").ToList();
        return g;
    }

    public async Task OnGetAsync(int? gym) => await LoadAsync(gym);

    /// <summary>Esegue un solo scenario: la pagina li chiama uno alla volta e mostra i risultati man mano.</summary>
    public async Task<IActionResult> OnPostRunAsync(int gym, int modelId, int? offerId, string scenario)
    {
        var g = await LoadAsync(gym);
        var model = Models.FirstOrDefault(m => m.Id == modelId);
        var offer = Offers.FirstOrDefault(o => o.Id == offerId);
        var s = Bench.Scenarios.FirstOrDefault(x => x.Key == scenario);
        if (g is null || g.Id != gym || model is null || s is null) return BadRequest(new { error = "Scelta non valida." });
        if (!_ai.Enabled) return BadRequest(new { error = "Assistente AI non configurato (Impostazioni AI)." });
        if (model.NeedsOffer && offer is null) return BadRequest(new { error = "Questo obiettivo ha bisogno di un'offerta attiva." });

        var conv = new Conversation
        {
            OrganizationId = g.OrganizationId, GymId = g.Id, GymName = g.Name, ContactName = "Giulia", Membership = "Annuale",
            ExpiresOn = DateTime.UtcNow.Date.AddDays(14), GoalModelId = model.Id, OfferId = offer?.Id
        };
        var built = await _assistant.BuildSystemAsync(conv, null);
        if (built is null) return BadRequest(new { error = "Modello non trovato." });
        var (system, _, _, profile, _) = built.Value;
        var r = await Bench.RunAsync(s, system, offer, profile, _ai, (res, cost) => _convs.LogUsageAsync(g.OrganizationId, g.Id, null, "banco", res, cost));
        return new JsonResult(new
        {
            key = r.Key, passed = r.Passed, outcome = Outcomes.Label(r.Outcome), cost = Math.Round(r.CostUsd, 5),
            transcript = r.Transcript.Select(t => new { role = t.Role, text = t.Text }),
            checks = r.Checks.Select(c => new { level = c.Level, text = c.Text })
        });
    }
}
