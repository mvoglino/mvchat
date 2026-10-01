using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Contacts;
using MvChat.Web.Data;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Lists;

public class DetailModel : PageModel
{
    public const int PageSize = 500;
    private readonly ContactsRepo _contacts;
    private readonly Repos _repos;
    public DetailModel(ContactsRepo contacts, Repos repos) { _contacts = contacts; _repos = repos; }

    public ContactList List { get; private set; } = null!;
    public List<Contact> Contacts { get; private set; } = new();
    public List<Reject> Rejects { get; private set; } = new();
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public bool Nuova { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var l = await _contacts.ListAsync(User.Scope(), id);
        if (l is null) return NotFound();
        List = l;
        Contacts = await _contacts.ContactsAsync(id, l.OrganizationId, Q?.Trim(), PageSize);
        Rejects = await _contacts.RejectsAsync(id);
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var me = User.Scope();
        var l = await _contacts.ListAsync(me, id);
        if (l is null) return NotFound();
        await _contacts.DeleteListAsync(id);
        await _repos.AuditAsync(me, "list.deleted", l.Name, HttpContext.Connection.RemoteIpAddress?.ToString(), l.OrganizationId, l.GymId);
        TempData["Ok"] = $"Lista «{l.Name}» eliminata con tutti i suoi contatti.";
        return Redirect("/Lists");
    }
}
