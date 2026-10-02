using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Contacts;
using MvChat.Web.Data;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.OptOuts;

public class IndexModel : PageModel
{
    private readonly ContactsRepo _contacts;
    private readonly Repos _repos;
    public IndexModel(ContactsRepo contacts, Repos repos) { _contacts = contacts; _repos = repos; }

    public List<OptOut> Items { get; private set; } = new();
    public List<Organization> Orgs { get; private set; } = new();
    public Scope Me { get; private set; } = new();
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty] public string? Phone { get; set; }
    [BindProperty] public string? Reason { get; set; }
    [BindProperty] public int? OrgId { get; set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAddAsync()
    {
        await LoadAsync();
        var org = Me.IsSuperAdmin ? OrgId : Me.OrganizationId;
        if (org is null || !Orgs.Any(o => o.Id == org)) { Error = "Scegli il gruppo."; return Page(); }
        var (phone, _) = ImportRules.NormalizePhone(Phone);
        if (phone is null) { Error = "Il numero non è un cellulare valido."; return Page(); }
        await _contacts.AddOptOutAsync(org.Value, Me.GymId, phone, ImportRules.Clean(Reason, 200), "manuale", Me.UserId);
        await _repos.AuditAsync(Me, "optout.added", phone, HttpContext.Connection.RemoteIpAddress?.ToString(), org);
        TempData["Ok"] = $"{phone} aggiunto alla lista STOP: non riceverà più messaggi da nessuna attività del gruppo.";
        return Redirect("/OptOuts");
    }

    public async Task<IActionResult> OnPostRemoveAsync(long id)
    {
        var me = User.Scope();
        // Togliere qualcuno dalla lista STOP è una decisione delicata: solo direzione gruppo o MVitalia.
        if (!me.IsSuperAdmin && !me.IsOrgAdmin) return Forbid();
        if (await _contacts.RemoveOptOutAsync(me, id) > 0)
        {
            await _repos.AuditAsync(me, "optout.removed", id.ToString(), HttpContext.Connection.RemoteIpAddress?.ToString());
            TempData["Ok"] = "Numero tolto dalla lista STOP.";
        }
        return Redirect("/OptOuts");
    }

    private async Task LoadAsync()
    {
        Me = User.Scope();
        Orgs = await _repos.OrganizationsAsync(Me);
        Items = await _contacts.OptOutsAsync(Me, Q?.Trim());
    }
}
