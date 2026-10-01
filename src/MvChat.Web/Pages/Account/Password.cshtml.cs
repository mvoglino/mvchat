using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Data;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Account;

public class PasswordModel : PageModel
{
    private readonly Repos _repos;
    private readonly PasswordService _pwd;
    public PasswordModel(Repos repos, PasswordService pwd) { _repos = repos; _pwd = pwd; }

    [BindProperty] public string Current { get; set; } = "";
    [BindProperty] public string New { get; set; } = "";
    [BindProperty] public string New2 { get; set; } = "";
    public bool Forced { get; private set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync() => Forced = (await _repos.UserForLoginAsync(User.Scope().UserId))?.MustChangePassword == true;

    public async Task<IActionResult> OnPostAsync()
    {
        var me = User.Scope();
        var u = await _repos.UserForLoginAsync(me.UserId);
        if (u is null) return Redirect("/Login");
        Forced = u.MustChangePassword;
        if (!_pwd.Verify(u.PasswordHash, Current)) Error = "La password attuale non è corretta.";
        else if (New != New2) Error = "Le due nuove password non coincidono.";
        else if (PasswordService.Weakness(New) is { } weak) Error = weak;
        else if (New == Current) Error = "La nuova password deve essere diversa da quella attuale.";
        if (Error is not null) return Page();
        await _repos.SetPasswordAsync(me.UserId, _pwd.Hash(New));
        var fresh = await _repos.UserForLoginAsync(me.UserId);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, LoginService.Principal(fresh!),
            new AuthenticationProperties { IsPersistent = true, IssuedUtc = DateTimeOffset.UtcNow });
        await _repos.AuditAsync(me, "password.changed", null, HttpContext.Connection.RemoteIpAddress?.ToString());
        TempData["Ok"] = "Password aggiornata.";
        return Redirect("/");
    }
}
