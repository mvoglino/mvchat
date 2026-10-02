using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Ai;
using MvChat.Web.Data;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Conversazioni;

/// <summary>Elenco delle conversazioni: in alto quelle che aspettano una persona.</summary>
public class IndexModel : PageModel
{
    private readonly ConversationRepo _convs; private readonly Repos _repos;
    public IndexModel(ConversationRepo convs, Repos repos) { _convs = convs; _repos = repos; }

    public List<Conversation> Items { get; private set; } = new();
    public List<Gym> Gyms { get; private set; } = new();
    public Dictionary<string, int> Counts { get; private set; } = new();
    public Scope Me { get; private set; } = new();
    public int? Gym { get; private set; }
    public string Status { get; private set; } = "";

    public async Task OnGetAsync(int? gym, string? status)
    {
        Me = User.Scope();
        Gyms = await _repos.GymsAsync(Me);
        Gym = Gyms.Any(g => g.Id == gym) ? gym : null;
        Status = status is "ai" or "operatore" or "chiusa" ? status : "";
        Items = await _convs.ListAsync(Me, Gym, Status == "" ? null : Status, 300);
        Counts = await _convs.CountsAsync(Me);
    }

    public static string StatusLabel(string s) => s switch { "ai" => "Risponde l'assistente", "operatore" => "Serve una persona", "chiusa" => "Chiusa", _ => s };
}
