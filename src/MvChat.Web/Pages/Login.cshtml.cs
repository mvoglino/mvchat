using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Data;
using MvChat.Web.Security;

namespace MvChat.Web.Pages;

public class LoginModel : PageModel
{
    private readonly LoginService _login;
    private readonly Repos _repos;
    public LoginModel(LoginService login, Repos repos) { _login = login; _repos = repos; }

    [BindProperty, Required(ErrorMessage = "Inserisci l'email.")] public string Email { get; set; } = "";
    [BindProperty, Required(ErrorMessage = "Inserisci la password.")] public string Password { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    public string? Error { get; private set; }

    public IActionResult OnGet() => User.Identity?.IsAuthenticated == true ? Redirect("/") : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var (ok, error, user) = await _login.CheckAsync(Email, Password, ip);
        if (!ok || user is null)
        {
            Error = error;
            await _repos.AuditAsync(null, "login.failed", Email.Trim() is { Length: > 120 } e ? e[..120] : Email.Trim(), ip);
            return Page();
        }
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, LoginService.Principal(user),
            new AuthenticationProperties { IsPersistent = true, IssuedUtc = DateTimeOffset.UtcNow });
        await _repos.AuditAsync(Scope.From(LoginService.Principal(user)), "login.ok", null, ip);
        if (user.MustChangePassword) return Redirect("/Account/Password");
        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/");
    }
}
