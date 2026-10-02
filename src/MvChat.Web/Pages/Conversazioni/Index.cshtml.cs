using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Ai;
using MvChat.Web.Data;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Conversazioni;

/// <summary>La postazione della reception: prima le conversazioni che aspettano una persona, da chi aspetta da più tempo.</summary>
public class IndexModel : PageModel
{
    private readonly ConversationRepo _convs; private readonly Repos _repos;
    public IndexModel(ConversationRepo convs, Repos repos) { _convs = convs; _repos = repos; }

    public static readonly (string Key, string Label)[] Views =
    {
        ("da_gestire", "Da gestire"), ("mie", "Prese in carico da me"), ("ai", "Le segue l'assistente"), ("chiuse", "Chiuse"), ("tutte", "Tutte")
    };

    public List<Conversation> Items { get; private set; } = new();
    public List<Gym> Gyms { get; private set; } = new();
    public Dictionary<string, int> Counts { get; private set; } = new();
    public int Waiting { get; private set; }
    public Scope Me { get; private set; } = new();
    public int? Gym { get; private set; }
    public string View { get; private set; } = "da_gestire";

    public async Task OnGetAsync(int? gym, string? view)
    {
        Me = User.Scope();
        Gyms = await _repos.GymsAsync(Me);
        Gym = Gyms.Any(g => g.Id == gym) ? gym : null;
        View = Views.Any(v => v.Key == view) ? view! : "da_gestire";
        Items = await _convs.InboxAsync(Me, Gym, View, 300);
        Counts = await _convs.CountsAsync(Me);
        Waiting = (await _convs.BadgeAsync(Me)).Count;
    }

    /// <summary>Usato da tutte le pagine ogni 30 secondi per l'avviso nel menu.</summary>
    public async Task<IActionResult> OnGetBadgeAsync()
    {
        var (count, id, name) = await _convs.BadgeAsync(User.Scope());
        return new JsonResult(new { count, id, name });
    }

    public static string Waited(DateTime? since)
    {
        if (since is not DateTime s) return "";
        var m = (int)(DateTime.UtcNow - s).TotalMinutes;
        return m < 1 ? "adesso" : m < 60 ? $"da {m} min" : m < 1440 ? $"da {m / 60} h {m % 60:00} min" : $"da {m / 1440} g";
    }
}
