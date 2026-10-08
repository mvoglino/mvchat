using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Billing;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Fatturazione;

/// <summary>Il rendiconto di un cliente per un mese, pronto da stampare o salvare in PDF.</summary>
public class DettaglioModel : PageModel
{
    private readonly BillingService _billing; private readonly MvChat.Web.Infrastructure.AppConfigStore _config;
    public DettaglioModel(BillingService billing, MvChat.Web.Infrastructure.AppConfigStore config) { _billing = billing; _config = config; }
    public Statement S { get; private set; } = new();
    public MvChat.Web.Infrastructure.BillingSettings B => _config.Current.Billing;

    public async Task<IActionResult> OnGetAsync(string? mese, int org)
    {
        var me = User.Scope();
        if (me.Role is Roles.Operator or Roles.AreaManager || mese is null) return NotFound();
        var s = (await _billing.MonthAsync(me, mese)).FirstOrDefault(x => x.OrganizationId == org); // perimetro nella query
        if (s is null) return NotFound();
        S = s;
        return Page();
    }
}
