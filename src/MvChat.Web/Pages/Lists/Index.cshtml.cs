using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Contacts;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Lists;

public class IndexModel : PageModel
{
    private readonly ContactsRepo _repo;
    public IndexModel(ContactsRepo repo) => _repo = repo;
    public List<ContactList> Items { get; private set; } = new();
    public Scope Me { get; private set; } = new();
    public async Task OnGetAsync() { Me = User.Scope(); Items = await _repo.ListsAsync(Me); }
}
