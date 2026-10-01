using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using MvChat.Web.Data;
using MvChat.Web.Infrastructure;

namespace MvChat.Web.Security;

/// <summary>Hash delle password con l'algoritmo standard di ASP.NET Core (PBKDF2, 100.000 iterazioni).</summary>
public sealed class PasswordService
{
    private readonly PasswordHasher<object> _hasher = new();
    private static readonly object Subject = new();

    public string Hash(string password) => _hasher.HashPassword(Subject, password);
    public bool Verify(string hash, string password) =>
        _hasher.VerifyHashedPassword(Subject, hash, password) != PasswordVerificationResult.Failed;

    /// <summary>Regola minima: 10 caratteri, almeno una lettera e un numero.</summary>
    public static string? Weakness(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 10) return "La password deve avere almeno 10 caratteri.";
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit)) return "La password deve contenere lettere e numeri.";
        return null;
    }

    public static string Generate()
    {
        const string chars = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(12);
        var pwd = new string(bytes.Select(b => chars[b % chars.Length]).ToArray());
        return pwd[..10] + "7a";
    }
}

public sealed class LoginService
{
    public const int MaxAttempts = 5;
    public static readonly TimeSpan LockTime = TimeSpan.FromMinutes(15);

    private readonly Repos _repos;
    private readonly PasswordService _pwd;
    public LoginService(Repos repos, PasswordService pwd) { _repos = repos; _pwd = pwd; }

    public async Task<(bool Ok, string? Error, UserAuth? User)> CheckAsync(string email, string password)
    {
        const string generic = "Email o password non corrette.";
        var u = await _repos.UserForLoginAsync(email.Trim());
        if (u is null) return (false, generic, null);
        if (u.LockedUntil is { } until && until > DateTime.UtcNow)
            return (false, $"Troppi tentativi sbagliati. Riprova dopo le {until.ToRome():HH:mm}.", null);
        if (!_pwd.Verify(u.PasswordHash, password))
        {
            var failed = u.FailedLogins + 1;
            await _repos.LoginFailedAsync(u.Id, failed, failed >= MaxAttempts ? DateTime.UtcNow.Add(LockTime) : null);
            return (false, generic, null);
        }
        if (!u.IsActive || !u.OrgActive) return (false, "Questo accesso è disattivato. Contatta il tuo amministratore.", null);
        await _repos.LoginOkAsync(u.Id);
        return (true, null, u);
    }

    public static ClaimsPrincipal Principal(UserAuth u)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, u.Id.ToString()),
            new(ClaimTypes.Name, u.FullName),
            new(ClaimTypes.Email, u.Email),
            new(ClaimTypes.Role, u.Role),
        };
        if (u.OrganizationId is { } o) claims.Add(new("org", o.ToString()));
        if (u.GymId is { } g) claims.Add(new("gym", g.ToString()));
        if (u.MustChangePassword) claims.Add(new("pwd", "change"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    /// <summary>
    /// A ogni richiesta (al massimo ogni 5 minuti) ricontrolla l'utente nel database:
    /// se è stato disattivato o gli è cambiato ruolo o palestra, l'accesso si aggiorna o si chiude.
    /// </summary>
    public static async Task ValidateAsync(CookieValidatePrincipalContext ctx)
    {
        var issued = ctx.Properties.IssuedUtc ?? DateTimeOffset.MinValue;
        if (DateTimeOffset.UtcNow - issued < TimeSpan.FromMinutes(5)) return;
        var scope = ctx.Principal!.Scope();
        var repos = ctx.HttpContext.RequestServices.GetRequiredService<Repos>();
        var u = await repos.UserForLoginAsync(scope.UserId);
        if (u is null || !u.IsActive || !u.OrgActive)
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }
        ctx.ReplacePrincipal(Principal(u));
        ctx.ShouldRenew = true;
    }
}
