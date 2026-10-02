using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Ai;
using MvChat.Web.Data;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.RisposteRapide;

/// <summary>Testi pronti per la reception. Il responsabile li crea per la sua sede, la direzione anche per tutta la struttura.</summary>
public class IndexModel : PageModel
{
    private readonly QuickReplyRepo _repo; private readonly Repos _repos;
    public IndexModel(QuickReplyRepo repo, Repos repos) { _repo = repo; _repos = repos; }

    public List<QuickReply> Items { get; private set; } = new();
    public List<(string Value, string Label)> Targets { get; private set; } = new();
    public string? Error { get; private set; }

    [BindProperty] public string? Target { get; set; }
    [BindProperty] public string? Title { get; set; }
    [BindProperty] public string? Body { get; set; }

    private async Task LoadAsync()
    {
        var me = User.Scope();
        Items = await _repo.ManageableAsync(me);
        var gyms = (await _repos.GymsAsync(me)).Where(g => g.IsActive).ToList();
        if (me.CanManageGyms)
            foreach (var org in gyms.GroupBy(g => (g.OrganizationId, g.OrganizationName)))
                Targets.Add(($"o:{org.Key.OrganizationId}", $"Tutte le sedi di {org.Key.OrganizationName}"));
        Targets.AddRange(gyms.Select(g => ($"g:{g.Id}", g.Name)));
    }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAddAsync()
    {
        await LoadAsync();
        var me = User.Scope();
        if (string.IsNullOrWhiteSpace(Title) || string.IsNullOrWhiteSpace(Body)) { Error = "Scrivi un titolo e il testo della risposta."; return Page(); }
        if (Title.Trim().Length > 80 || Body.Trim().Length > 1000) { Error = "Titolo massimo 80 caratteri, testo massimo 1000."; return Page(); }
        // Sede e struttura si ricavano dalle scelte consentite all'utente, mai dal valore inviato così com'è.
        if (!Targets.Any(t => t.Value == Target)) { Error = "Scegli dove usare la risposta."; return Page(); }
        var gyms = await _repos.GymsAsync(me);
        int orgId; int? gymId = null;
        if (Target!.StartsWith("o:")) orgId = int.Parse(Target[2..]);
        else { var g = gyms.First(x => x.Id == int.Parse(Target[2..])); orgId = g.OrganizationId; gymId = g.Id; }
        await _repo.AddAsync(orgId, gymId, Title.Trim(), Body.Trim(), me.UserId);
        await _repos.AuditAsync(me, "quickreply.added", Title.Trim(), HttpContext.Connection.RemoteIpAddress?.ToString(), orgId, gymId);
        TempData["Ok"] = "Risposta rapida aggiunta.";
        return Redirect("/RisposteRapide");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await LoadAsync();
        var me = User.Scope();
        var q = Items.FirstOrDefault(x => x.Id == id);
        // Il responsabile non cancella quelle comuni della struttura.
        if (q is null || (q.GymId is null && !me.CanManageGyms)) return NotFound();
        await _repo.DeleteAsync(id);
        await _repos.AuditAsync(me, "quickreply.deleted", q.Title, HttpContext.Connection.RemoteIpAddress?.ToString(), q.OrganizationId, q.GymId);
        TempData["Ok"] = "Risposta rapida eliminata.";
        return Redirect("/RisposteRapide");
    }
}
