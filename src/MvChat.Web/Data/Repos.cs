using System.Data.Common;
using MvChat.Web.Infrastructure;
using MvChat.Web.Security;

namespace MvChat.Web.Data;

public sealed record Organization(int Id, string Name, string Slug, string? LogoUrl, string PrimaryColor, string? VatNumber, string? BillingEmail, bool IsActive, int Gyms, int Users);
public sealed record Gym(int Id, int OrganizationId, string OrganizationName, string Name, string? City, string? Address, string? Phone, bool IsActive, int Users);
public sealed record UserRow(int Id, int? OrganizationId, string? OrganizationName, int? GymId, string? GymName, string Email, string FullName, string Role, bool IsActive, DateTime? LastLoginAt);
public sealed record UserAuth(int Id, int? OrganizationId, int? GymId, string Email, string FullName, string PasswordHash, string Role, bool IsActive, int FailedLogins, DateTime? LockedUntil, bool MustChangePassword, bool OrgActive);
public sealed record Branding(string Name, string? LogoUrl, string PrimaryColor);

/// <summary>
/// Query su catene, palestre e utenti. Ogni lettura riceve lo Scope di chi chiede
/// e lo traduce in un filtro SQL: nessuna pagina può dimenticarsi di filtrare.
/// </summary>
public sealed class Repos
{
    private readonly Db _db;
    public Repos(Db db) => _db = db;

    // ---------- Catene ----------
    public Task<List<Organization>> OrganizationsAsync(Scope s) => _db.QueryAsync(
        @"SELECT o.*, (SELECT COUNT(*) FROM Gyms g WHERE g.OrganizationId=o.Id) AS GymCount,
                 (SELECT COUNT(*) FROM Users u WHERE u.OrganizationId=o.Id) AS UserCount
          FROM Organizations o WHERE (@All=1 OR o.Id=@Org) ORDER BY o.Name",
        new { All = s.IsSuperAdmin ? 1 : 0, Org = s.OrganizationId ?? -1 }, MapOrg);

    public async Task<Organization?> OrganizationAsync(Scope s, int id) =>
        (await OrganizationsAsync(s)).FirstOrDefault(o => o.Id == id);

    public async Task<int> SaveOrganizationAsync(int? id, string name, string slug, string? logo, string color, string? vat, string? email, bool active)
    {
        if (id is null)
            return await _db.ScalarAsync<int>(
                @"INSERT INTO Organizations (Name, Slug, LogoUrl, PrimaryColor, VatNumber, BillingEmail, IsActive)
                  VALUES (@name, @slug, @logo, @color, @vat, @email, @active); SELECT LAST_INSERT_ID();",
                new { name, slug, logo, color, vat, email, active });
        await _db.ExecuteAsync(
            @"UPDATE Organizations SET Name=@name, Slug=@slug, LogoUrl=@logo, PrimaryColor=@color, VatNumber=@vat,
              BillingEmail=@email, IsActive=@active WHERE Id=@id",
            new { id, name, slug, logo, color, vat, email, active });
        return id.Value;
    }

    public Task<Branding?> BrandingAsync(int orgId) => _db.FirstAsync(
        "SELECT Name, LogoUrl, PrimaryColor FROM Organizations WHERE Id=@orgId", new { orgId },
        r => new Branding(r.Str("Name")!, r.Str("LogoUrl"), r.Str("PrimaryColor")!));

    private static Organization MapOrg(DbDataReader r) => new(
        r.Int("Id"), r.Str("Name")!, r.Str("Slug")!, r.Str("LogoUrl"), r.Str("PrimaryColor")!, r.Str("VatNumber"),
        r.Str("BillingEmail"), r.Bool("IsActive"), Convert.ToInt32(r["GymCount"]), Convert.ToInt32(r["UserCount"]));

    // ---------- Palestre ----------
    private const string GymSelect =
        @"SELECT g.*, o.Name AS OrgName, (SELECT COUNT(*) FROM Users u WHERE u.GymId=g.Id) AS UserCount
          FROM Gyms g JOIN Organizations o ON o.Id=g.OrganizationId
          WHERE (@All=1 OR (@IsOrg=1 AND g.OrganizationId=@Org) OR g.Id=@Gym)";

    private static object GymArgs(Scope s) => new
    {
        All = s.IsSuperAdmin ? 1 : 0,
        IsOrg = s.IsOrgAdmin ? 1 : 0,
        Org = s.OrganizationId ?? -1,
        Gym = s.GymId ?? -1
    };

    public Task<List<Gym>> GymsAsync(Scope s) => _db.QueryAsync(GymSelect + " ORDER BY o.Name, g.Name", GymArgs(s), MapGym);

    public async Task<Gym?> GymAsync(Scope s, int id) => (await GymsAsync(s)).FirstOrDefault(g => g.Id == id);

    public async Task<int> SaveGymAsync(int? id, int orgId, string name, string? city, string? address, string? phone, bool active)
    {
        if (id is null)
            return await _db.ScalarAsync<int>(
                @"INSERT INTO Gyms (OrganizationId, Name, City, Address, Phone, IsActive)
                  VALUES (@orgId, @name, @city, @address, @phone, @active); SELECT LAST_INSERT_ID();",
                new { orgId, name, city, address, phone, active });
        await _db.ExecuteAsync(
            "UPDATE Gyms SET Name=@name, City=@city, Address=@address, Phone=@phone, IsActive=@active WHERE Id=@id AND OrganizationId=@orgId",
            new { id, orgId, name, city, address, phone, active });
        return id.Value;
    }

    private static Gym MapGym(DbDataReader r) => new(
        r.Int("Id"), r.Int("OrganizationId"), r.Str("OrgName")!, r.Str("Name")!, r.Str("City"), r.Str("Address"),
        r.Str("Phone"), r.Bool("IsActive"), Convert.ToInt32(r["UserCount"]));

    // ---------- Utenti ----------
    public Task<List<UserRow>> UsersAsync(Scope s) => _db.QueryAsync(
        @"SELECT u.Id, u.OrganizationId, o.Name AS OrgName, u.GymId, g.Name AS GymName, u.Email, u.FullName, u.Role, u.IsActive, u.LastLoginAt
          FROM Users u LEFT JOIN Organizations o ON o.Id=u.OrganizationId LEFT JOIN Gyms g ON g.Id=u.GymId
          WHERE (@All=1 OR (@IsOrg=1 AND u.OrganizationId=@Org) OR (@IsMgr=1 AND u.GymId=@Gym))
          ORDER BY o.Name, g.Name, u.FullName",
        new { All = s.IsSuperAdmin ? 1 : 0, IsOrg = s.IsOrgAdmin ? 1 : 0, IsMgr = s.IsManager ? 1 : 0, Org = s.OrganizationId ?? -1, Gym = s.GymId ?? -1 },
        r => new UserRow(r.Int("Id"), r.IntN("OrganizationId"), r.Str("OrgName"), r.IntN("GymId"), r.Str("GymName"),
            r.Str("Email")!, r.Str("FullName")!, r.Str("Role")!, r.Bool("IsActive"), r.Date("LastLoginAt")));

    public async Task<UserRow?> UserAsync(Scope s, int id) => (await UsersAsync(s)).FirstOrDefault(u => u.Id == id);

    public async Task<bool> EmailTakenAsync(string email, int? exceptId) => await _db.ScalarAsync<long>(
        "SELECT COUNT(*) FROM Users WHERE Email=@email AND Id<>@id", new { email, id = exceptId ?? -1 }) > 0;

    public async Task<int> SaveUserAsync(int? id, int? orgId, int? gymId, string email, string fullName, string role, bool active, string? passwordHash)
    {
        if (id is null)
            return await _db.ScalarAsync<int>(
                @"INSERT INTO Users (OrganizationId, GymId, Email, FullName, PasswordHash, Role, IsActive, MustChangePassword)
                  VALUES (@orgId, @gymId, @email, @fullName, @passwordHash, @role, @active, 1); SELECT LAST_INSERT_ID();",
                new { orgId, gymId, email, fullName, passwordHash, role, active });
        await _db.ExecuteAsync(
            @"UPDATE Users SET OrganizationId=@orgId, GymId=@gymId, Email=@email, FullName=@fullName, Role=@role, IsActive=@active
              WHERE Id=@id", new { id, orgId, gymId, email, fullName, role, active });
        if (passwordHash is not null)
            await _db.ExecuteAsync("UPDATE Users SET PasswordHash=@passwordHash, MustChangePassword=1, FailedLogins=0, LockedUntil=NULL WHERE Id=@id", new { id, passwordHash });
        return id.Value;
    }

    public Task<UserAuth?> UserForLoginAsync(string email) => _db.FirstAsync(
        @"SELECT u.*, COALESCE(o.IsActive, 1) AS OrgActive FROM Users u LEFT JOIN Organizations o ON o.Id=u.OrganizationId WHERE u.Email=@email",
        new { email }, MapAuth);

    public Task<UserAuth?> UserForLoginAsync(int id) => _db.FirstAsync(
        @"SELECT u.*, COALESCE(o.IsActive, 1) AS OrgActive FROM Users u LEFT JOIN Organizations o ON o.Id=u.OrganizationId WHERE u.Id=@id",
        new { id }, MapAuth);

    private static UserAuth MapAuth(DbDataReader r) => new(
        r.Int("Id"), r.IntN("OrganizationId"), r.IntN("GymId"), r.Str("Email")!, r.Str("FullName")!, r.Str("PasswordHash")!,
        r.Str("Role")!, r.Bool("IsActive"), r.Int("FailedLogins"), r.Date("LockedUntil"), r.Bool("MustChangePassword"),
        Convert.ToInt32(r["OrgActive"]) == 1);

    public Task LoginFailedAsync(int id, int failed, DateTime? lockUntil) => _db.ExecuteAsync(
        "UPDATE Users SET FailedLogins=@failed, LockedUntil=@lockUntil WHERE Id=@id", new { id, failed, lockUntil });

    public Task LoginOkAsync(int id) => _db.ExecuteAsync(
        "UPDATE Users SET FailedLogins=0, LockedUntil=NULL, LastLoginAt=UTC_TIMESTAMP() WHERE Id=@id", new { id });

    public Task SetPasswordAsync(int id, string hash) => _db.ExecuteAsync(
        "UPDATE Users SET PasswordHash=@hash, MustChangePassword=0 WHERE Id=@id", new { id, hash });

    // ---------- Registro attività ----------
    public Task AuditAsync(Scope? s, string action, string? detail, string? ip, int? orgId = null, int? gymId = null) => _db.ExecuteAsync(
        "INSERT INTO AuditLog (UserId, OrganizationId, GymId, Action, Detail, Ip) VALUES (@uid, @org, @gym, @action, @detail, @ip)",
        new { uid = s?.UserId, org = orgId ?? s?.OrganizationId, gym = gymId ?? s?.GymId, action, detail, ip });

    // ---------- Riepilogo per la pagina iniziale ----------
    public async Task<(int Orgs, int Gyms, int Users)> CountsAsync(Scope s)
    {
        var gyms = await GymsAsync(s);
        var users = s.CanManageUsers ? (await UsersAsync(s)).Count : 0;
        var orgs = s.IsSuperAdmin ? (await OrganizationsAsync(s)).Count : 1;
        return (orgs, gyms.Count, users);
    }
}
