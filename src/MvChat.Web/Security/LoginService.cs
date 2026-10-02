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

    // Limite per indirizzo di rete: chi prova tante password su tanti account viene fermato.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int Count, DateTime Since)> ByIp = new();
    public const int MaxPerIp = 30;
    private static readonly string DummyHash = new PasswordService().Hash("password-che-non-esiste-" + Guid.NewGuid());

    public static bool IpBlocked(string? ip) =>
        ip is not null && ByIp.TryGetValue(ip, out var v) && v.Since > DateTime.UtcNow.AddMinutes(-15) && v.Count >= MaxPerIp;

    private static void IpFailed(string? ip)
    {
        if (ip is null) return;
        ByIp.AddOrUpdate(ip, _ => (1, DateTime.UtcNow), (_, v) => v.Since < DateTime.UtcNow.AddMinutes(-15) ? (1, DateTime.UtcNow) : (v.Count + 1, v.Since));
        if (ByIp.Count > 10000) ByIp.Clear(); // non deve crescere all'infinito
    }

    public async Task<(bool Ok, string? Error, UserAuth? User)> CheckAsync(string email, string password, string? ip = null)
    {
        const string generic = "Email o password non corrette.";
        if (IpBlocked(ip)) return (false, "Troppi tentativi da questa rete. Riprova tra 15 minuti.", null);
        var u = await _repos.UserForLoginAsync(email.Trim());
        if (u is null)
        {
            _pwd.Verify(DummyHash, password); // stesso tempo di risposta di un'email esistente
            IpFailed(ip);
            return (false, generic, null);
        }
        if (u.LockedUntil is { } until && until > DateTime.UtcNow)
            return (false, $"Troppi tentativi sbagliati. Riprova dopo le {until.ToRome():HH:mm}.", null);
        if (!_pwd.Verify(u.PasswordHash, password))
        {
            await _repos.LoginFailedAsync(u.Id, MaxAttempts, DateTime.UtcNow.Add(LockTime));
            IpFailed(ip);
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
        claims.Add(new("stamp", Stamp(u.PasswordHash)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    /// <summary>
    /// A ogni richiesta (al massimo ogni 5 minuti) ricontrolla l'utente nel database:
    /// se è stato disattivato o gli è cambiato ruolo o attività, l'accesso si aggiorna o si chiude.
    /// </summary>
    /// <summary>Impronta della password: se la password cambia, le sessioni aperte con quella vecchia non valgono più.</summary>
    public static string Stamp(string passwordHash) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(passwordHash)))[..16];

    public static async Task ValidateAsync(CookieValidatePrincipalContext ctx)
    {
        var issued = ctx.Properties.IssuedUtc ?? DateTimeOffset.MinValue;
        if (DateTimeOffset.UtcNow - issued < TimeSpan.FromMinutes(5)) return;
        var scope = ctx.Principal!.Scope();
        var repos = ctx.HttpContext.RequestServices.GetRequiredService<Repos>();
        var u = await repos.UserForLoginAsync(scope.UserId);
        if (u is null || !u.IsActive || !u.OrgActive || ctx.Principal!.FindFirstValue("stamp") != Stamp(u.PasswordHash))
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }
        ctx.ReplacePrincipal(Principal(u));
        ctx.ShouldRenew = true;
    }
}
