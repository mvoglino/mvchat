using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Data;
using MvChat.Web.Infrastructure;
using MvChat.Web.Security;

namespace MvChat.Web.Pages;

[IgnoreAntiforgeryToken(Order = 1001)] // prima installazione: non esistono ancora chiavi persistenti
public class InstallModel : PageModel
{
    private readonly AppConfigStore _config;
    private readonly PasswordService _pwd;
    public InstallModel(AppConfigStore config, PasswordService pwd) { _config = config; _pwd = pwd; }

    [BindProperty] public InstallInput Input { get; set; } = new();
    public string? WriteError { get; private set; }
    public string? DbError { get; private set; }
    public bool Done { get; private set; }
    public string TickUrl { get; private set; } = "";
    public List<string> Applied { get; private set; } = new();

    public IActionResult OnGet()
    {
        if (_config.Current.Installed) return Redirect("/Login");
        _config.CanWrite(out var err);
        WriteError = string.IsNullOrEmpty(err) ? null : err;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (_config.Current.Installed) return Redirect("/Login");
        if (!_config.CanWrite(out var werr)) { WriteError = werr; return Page(); }

        if (Input.AdminPassword != Input.AdminPassword2)
            ModelState.AddModelError("Input.AdminPassword2", "Le due password non coincidono.");
        if (PasswordService.Weakness(Input.AdminPassword) is { } weak)
            ModelState.AddModelError("Input.AdminPassword", weak);
        if (!ModelState.IsValid) return Page();

        var csb = new DbConnectionStringBuilder
        {
            ["Server"] = Input.DbHost.Trim(),
            ["Port"] = Input.DbPort,
            ["Database"] = Input.DbName.Trim(),
            ["User ID"] = Input.DbUser.Trim(),
            ["Password"] = Input.DbPassword,
            ["SslMode"] = "Preferred",
            ["CharacterSet"] = "utf8mb4",
            ["ConnectionTimeout"] = 15,
        };
        var cs = csb.ConnectionString;

        try
        {
            Applied = await Migrator.ApplyAsync(cs);
            await using var cn = await Db.OpenAsync(cs);
            long admins;
            await using (var q = Db.Command(cn, "SELECT COUNT(*) FROM Users WHERE Role='superadmin'", null))
                admins = Convert.ToInt64(await q.ExecuteScalarAsync());
            if (admins == 0)
            {
                await using var ins = Db.Command(cn,
                    "INSERT INTO Users (Email, FullName, PasswordHash, Role, IsActive) VALUES (@Email, @Name, @Hash, 'superadmin', 1)",
                    new { Email = Input.AdminEmail.Trim().ToLowerInvariant(), Name = Input.AdminName.Trim(), Hash = _pwd.Hash(Input.AdminPassword) });
                await ins.ExecuteNonQueryAsync();
            }
        }
        catch (Exception ex)
        {
            DbError = Friendly(ex);
            return Page();
        }

        var token = AppConfigStore.NewToken();
        _config.Save(new AppConfig
        {
            Installed = true,
            ProductName = string.IsNullOrWhiteSpace(Input.ProductName) ? "mvchat" : Input.ProductName.Trim(),
            ConnectionString = cs,
            JobToken = token,
            InstalledAt = DateTime.UtcNow
        });
        TickUrl = $"{Request.Scheme}://{Request.Host}/jobs/tick?token={token}";
        Done = true;
        return Page();
    }

    private static string Friendly(Exception ex)
    {
        var m = ex.Message;
        if (m.Contains("Access denied", StringComparison.OrdinalIgnoreCase)) return "Il database ha rifiutato utente o password. Controlla i dati nel pannello Aruba.";
        if (m.Contains("Unable to connect", StringComparison.OrdinalIgnoreCase) || m.Contains("host", StringComparison.OrdinalIgnoreCase))
            return "Non riesco a raggiungere il server del database. Controlla l'indirizzo (host) e la porta.";
        if (m.Contains("Unknown database", StringComparison.OrdinalIgnoreCase)) return "Il database indicato non esiste. Crealo prima dal pannello Aruba.";
        return "Errore del database: " + m;
    }

    public class InstallInput
    {
        [Required(ErrorMessage = "Indica il server del database.")] public string DbHost { get; set; } = "";
        [Range(1, 65535)] public int DbPort { get; set; } = 3306;
        [Required(ErrorMessage = "Indica il nome del database.")] public string DbName { get; set; } = "";
        [Required(ErrorMessage = "Indica l'utente del database.")] public string DbUser { get; set; } = "";
        public string DbPassword { get; set; } = "";
        public string ProductName { get; set; } = "mvchat";
        [Required(ErrorMessage = "Indica il tuo nome.")] public string AdminName { get; set; } = "";
        [Required(ErrorMessage = "Indica l'email."), EmailAddress(ErrorMessage = "Email non valida.")] public string AdminEmail { get; set; } = "";
        [Required(ErrorMessage = "Scegli una password.")] public string AdminPassword { get; set; } = "";
        [Required(ErrorMessage = "Ripeti la password.")] public string AdminPassword2 { get; set; } = "";
    }
}
