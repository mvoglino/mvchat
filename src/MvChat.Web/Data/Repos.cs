using System.Data.Common;
using MvChat.Web.Infrastructure;
using MvChat.Web.Security;

namespace MvChat.Web.Data;

public sealed record Organization(int Id, string Name, string Slug, string? LogoUrl, string PrimaryColor, string? VatNumber, string? BillingEmail, bool IsActive, int Gyms, int Users, string Sector, bool IsGroup);

/// <summary>Dati dell'attività di un gruppo: personalizzabili da MVitalia e dalla direzione del gruppo.</summary>
public sealed class OrgProfile
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Sector { get; set; } = "palestra";
    public string PrimaryColor { get; set; } = "#F6931E";
    public string? LegalName { get; set; }
    public string? VatNumber { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Phone { get; set; }
    public string? ContactEmail { get; set; }
    public string? Website { get; set; }
    public string? Description { get; set; }
    public string? PrivacyUrl { get; set; }
    public bool HasLogo { get; set; }
    public string? LogoUrl { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
/// <summary>Un'attività (nel codice «Gym»). InGroup = fa parte di un gruppo; altrimenti è un'attività singola.</summary>
public sealed record Gym(int Id, int OrganizationId, string OrganizationName, string Name, string? City, string? Address, string? Phone, bool IsActive, int Users,
    bool InGroup = true, string Sector = "palestra", string? OwnSector = null);

/// <summary>Dati propri di un'attività: tipo (se diverso dal gruppo), nome legale, logo, presentazione.</summary>
public sealed class ActivityProfile
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public string GroupName { get; set; } = "";
    public bool InGroup { get; set; }
    public string GroupSector { get; set; } = "palestra";
    public string Name { get; set; } = "";
    public string? Sector { get; set; }
    public string? PrimaryColor { get; set; }
    public string? LegalName { get; set; }
    public string? VatNumber { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Phone { get; set; }
    public string? ContactEmail { get; set; }
    public string? Website { get; set; }
    public string? Description { get; set; }
    public string? PrivacyUrl { get; set; }
    public string? GroupPrivacyUrl { get; set; }
    public bool HasLogo { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string EffectiveSector => Sector ?? GroupSector;
}
public sealed record UserRow(int Id, int? OrganizationId, string? OrganizationName, int? GymId, string? GymName, string Email, string FullName, string Role, bool IsActive, DateTime? LastLoginAt);
public sealed record UserAuth(int Id, int? OrganizationId, int? GymId, string Email, string FullName, string PasswordHash, string Role, bool IsActive, int FailedLogins, DateTime? LockedUntil, bool MustChangePassword, bool OrgActive);
public sealed record Branding(string Name, string? LogoUrl, string PrimaryColor, string Sector);

/// <summary>
/// Query su gruppi, attività e utenti. Ogni lettura riceve lo Scope di chi chiede
/// e lo traduce in un filtro SQL: nessuna pagina può dimenticarsi di filtrare.
/// </summary>
public sealed class Repos
{
    private readonly Db _db;
    public Repos(Db db) => _db = db;

    // ---------- Gruppi ----------
    public Task<List<Organization>> OrganizationsAsync(Scope s) => _db.QueryAsync(
        @"SELECT o.Id, o.Name, o.Slug, o.LogoUrl, o.PrimaryColor, o.VatNumber, o.BillingEmail, o.IsActive, o.Sector, o.IsGroup,
                 (SELECT COUNT(*) FROM Gyms g WHERE g.OrganizationId=o.Id) AS GymCount,
                 (SELECT COUNT(*) FROM Users u WHERE u.OrganizationId=o.Id) AS UserCount
          FROM Organizations o WHERE (@All=1 OR o.Id=@Org) ORDER BY o.Name",
        new { All = s.IsSuperAdmin ? 1 : 0, Org = s.OrganizationId ?? -1 }, MapOrg);

    public async Task<Organization?> OrganizationAsync(Scope s, int id) =>
        (await OrganizationsAsync(s)).FirstOrDefault(o => o.Id == id);

    /// <summary>Dati amministrativi (solo MVitalia): nome, codice, tipo di attività, email fatture, attiva/sospesa.</summary>
    public async Task<int> SaveOrganizationAsync(int? id, string name, string slug, string sector, string? email, bool active)
    {
        if (id is null)
            return await _db.ScalarAsync<int>(
                @"INSERT INTO Organizations (Name, Slug, Sector, BillingEmail, IsActive)
                  VALUES (@name, @slug, @sector, @email, @active); SELECT LAST_INSERT_ID();",
                new { name, slug, sector, email, active });
        await _db.ExecuteAsync(
            "UPDATE Organizations SET Name=@name, Slug=@slug, Sector=@sector, BillingEmail=@email, IsActive=@active WHERE Id=@id",
            new { id, name, slug, sector, email, active });
        return id.Value;
    }

    public async Task<bool> IsGroupAsync(int orgId) => await _db.ScalarAsync<int>("SELECT IsGroup FROM Organizations WHERE Id=@orgId", new { orgId }) == 1;

    public Task<OrgProfile?> OrgProfileAsync(int orgId) => _db.FirstAsync(
        @"SELECT Id, Name, Sector, PrimaryColor, LegalName, VatNumber, Address, City, Phone, ContactEmail, Website, Description, PrivacyUrl, LogoUrl, UpdatedAt,
                 LogoData IS NOT NULL AS HasLogo FROM Organizations WHERE Id=@orgId", new { orgId },
        r => new OrgProfile
        {
            Id = r.Int("Id"), Name = r.Str("Name")!, Sector = r.Str("Sector") ?? "palestra", PrimaryColor = r.Str("PrimaryColor")!,
            LegalName = r.Str("LegalName"), VatNumber = r.Str("VatNumber"), Address = r.Str("Address"), City = r.Str("City"), Phone = r.Str("Phone"),
            ContactEmail = r.Str("ContactEmail"), Website = r.Str("Website"), Description = r.Str("Description"), PrivacyUrl = r.Str("PrivacyUrl"), LogoUrl = r.Str("LogoUrl"),
            UpdatedAt = r.Date("UpdatedAt"), HasLogo = Convert.ToInt32(r.GetValue(r.GetOrdinal("HasLogo"))) == 1
        });

    /// <summary>Dati dell'attività e aspetto: li cura MVitalia o la direzione del gruppo. Il tipo di attività lo cambia solo MVitalia.</summary>
    public Task SaveOrgProfileAsync(OrgProfile p, bool canChangeSector) => _db.ExecuteAsync(
        @"UPDATE Organizations SET Name=@Name, PrimaryColor=@PrimaryColor, LegalName=@LegalName, VatNumber=@VatNumber, Address=@Address, City=@City,
            Phone=@Phone, ContactEmail=@ContactEmail, Website=@Website, Description=@Description, PrivacyUrl=@PrivacyUrl,
            Sector=CASE WHEN @canChangeSector=1 THEN @Sector ELSE Sector END, UpdatedAt=UTC_TIMESTAMP() WHERE Id=@Id",
        new { p.Id, p.Name, p.PrimaryColor, p.LegalName, p.VatNumber, p.Address, p.City, p.Phone, p.ContactEmail, p.Website, p.Description, p.PrivacyUrl, p.Sector,
              canChangeSector = canChangeSector ? 1 : 0 });

    public Task SaveLogoAsync(int orgId, byte[]? data, string? type) => _db.ExecuteAsync(
        "UPDATE Organizations SET LogoData=@data, LogoType=@type, LogoUrl=NULL, UpdatedAt=UTC_TIMESTAMP() WHERE Id=@orgId", new { orgId, data, type });

    public async Task<(byte[] Data, string Type, DateTime? At)?> LogoAsync(int orgId)
    {
        var rows = await _db.QueryAsync("SELECT LogoData, LogoType, UpdatedAt FROM Organizations WHERE Id=@orgId AND LogoData IS NOT NULL", new { orgId },
            r => ((byte[])r.GetValue(0), r.Str("LogoType") ?? "image/png", r.Date("UpdatedAt")));
        return rows.Count == 0 ? null : rows[0];
    }

    public Task<Branding?> BrandingAsync(int orgId) => _db.FirstAsync(
        @"SELECT Id, Name, PrimaryColor, Sector, LogoUrl, LogoData IS NOT NULL AS HasLogo, UpdatedAt FROM Organizations WHERE Id=@orgId", new { orgId },
        r => new Branding(r.Str("Name")!,
            Convert.ToInt32(r.GetValue(r.GetOrdinal("HasLogo"))) == 1 ? $"/logo/{r.Int("Id")}?v={(r.Date("UpdatedAt") ?? DateTime.MinValue).Ticks}" : r.Str("LogoUrl"),
            r.Str("PrimaryColor")!, r.Str("Sector") ?? "palestra"));

    private static Organization MapOrg(DbDataReader r) => new(
        r.Int("Id"), r.Str("Name")!, r.Str("Slug")!, r.Str("LogoUrl"), r.Str("PrimaryColor")!, r.Str("VatNumber"),
        r.Str("BillingEmail"), r.Bool("IsActive"), Convert.ToInt32(r["GymCount"]), Convert.ToInt32(r["UserCount"]), r.Str("Sector") ?? "palestra", r.Bool("IsGroup"));

    // ---------- Attività ----------
    private const string GymSelect =
        @"SELECT g.Id, g.OrganizationId, g.Name, g.City, g.Address, g.Phone, g.IsActive, g.Sector AS OwnSector, o.Name AS OrgName, o.IsGroup,
                 COALESCE(g.Sector, o.Sector) AS Sector, (SELECT COUNT(*) FROM Users u WHERE u.GymId=g.Id) AS UserCount
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
        // Attività singola: il suo contenitore segue nome e stato dell'attività (se è sospesa, i suoi utenti non entrano).
        await _db.ExecuteAsync("UPDATE Organizations SET Name=@name, IsActive=@active WHERE Id=@orgId AND IsGroup=0", new { orgId, name, active });
        return id.Value;
    }

    /// <summary>Un'attività che non fa parte di un gruppo: si crea insieme al suo contenitore (non visibile come gruppo).</summary>
    public async Task<(int OrgId, int GymId)> CreateSingleActivityAsync(string name, string slug, string sector, string? city, string? address, string? phone, bool active)
    {
        await using var cn = await _db.OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();
        int orgId, gymId;
        await using (var c1 = Db.Command(cn, @"INSERT INTO Organizations (Name, Slug, Sector, IsActive, IsGroup) VALUES (@name, @slug, @sector, @active, 0); SELECT LAST_INSERT_ID();",
            new { name, slug, sector, active }, tx)) orgId = Convert.ToInt32(await c1.ExecuteScalarAsync());
        await using (var c2 = Db.Command(cn, @"INSERT INTO Gyms (OrganizationId, Name, City, Address, Phone, IsActive) VALUES (@orgId, @name, @city, @address, @phone, @active); SELECT LAST_INSERT_ID();",
            new { orgId, name, city, address, phone, active }, tx)) gymId = Convert.ToInt32(await c2.ExecuteScalarAsync());
        await tx.CommitAsync();
        return (orgId, gymId);
    }

    public Task<ActivityProfile?> ActivityProfileAsync(int gymId) => _db.FirstAsync(
        @"SELECT g.Id, g.OrganizationId, o.Name AS GroupName, o.IsGroup, o.Sector AS GroupSector, g.Name, g.Sector, g.PrimaryColor, g.LegalName, g.VatNumber,
                 g.Address, g.City, g.Phone, g.ContactEmail, g.Website, g.Description, g.PrivacyUrl, o.PrivacyUrl AS GroupPrivacyUrl, g.UpdatedAt, g.LogoData IS NOT NULL AS HasLogo
          FROM Gyms g JOIN Organizations o ON o.Id=g.OrganizationId WHERE g.Id=@gymId", new { gymId },
        r => new ActivityProfile
        {
            Id = r.Int("Id"), OrganizationId = r.Int("OrganizationId"), GroupName = r.Str("GroupName")!, InGroup = r.Bool("IsGroup"),
            GroupSector = r.Str("GroupSector") ?? "palestra", Name = r.Str("Name")!, Sector = r.Str("Sector"), PrimaryColor = r.Str("PrimaryColor"),
            LegalName = r.Str("LegalName"), VatNumber = r.Str("VatNumber"), Address = r.Str("Address"), City = r.Str("City"), Phone = r.Str("Phone"),
            ContactEmail = r.Str("ContactEmail"), Website = r.Str("Website"), Description = r.Str("Description"), PrivacyUrl = r.Str("PrivacyUrl"),
            GroupPrivacyUrl = r.Str("GroupPrivacyUrl"), UpdatedAt = r.Date("UpdatedAt"),
            HasLogo = Convert.ToInt32(r.GetValue(r.GetOrdinal("HasLogo"))) == 1
        });

    /// <summary>Salva i dati propri dell'attività. Per un'attività singola il tipo di attività sta nel suo contenitore.</summary>
    public async Task SaveActivityProfileAsync(ActivityProfile p, bool canChangeSector)
    {
        await _db.ExecuteAsync(
            @"UPDATE Gyms SET Name=@Name, PrimaryColor=@PrimaryColor, LegalName=@LegalName, VatNumber=@VatNumber, Address=@Address, City=@City, Phone=@Phone,
                ContactEmail=@ContactEmail, Website=@Website, Description=@Description, PrivacyUrl=@PrivacyUrl,
                Sector=CASE WHEN @canChangeSector=1 AND @InGroup=1 THEN @Sector ELSE Sector END, UpdatedAt=UTC_TIMESTAMP() WHERE Id=@Id",
            new { p.Id, p.Name, p.PrimaryColor, p.LegalName, p.VatNumber, p.Address, p.City, p.Phone, p.ContactEmail, p.Website, p.Description, p.PrivacyUrl, p.Sector,
                  InGroup = p.InGroup ? 1 : 0, canChangeSector = canChangeSector ? 1 : 0 });
        if (!p.InGroup)
            await _db.ExecuteAsync("UPDATE Organizations SET Name=@Name, Sector=CASE WHEN @can=1 AND @Sector IS NOT NULL THEN @Sector ELSE Sector END WHERE Id=@OrganizationId AND IsGroup=0",
                new { p.Name, p.Sector, p.OrganizationId, can = canChangeSector ? 1 : 0 });
    }

    /// <summary>Abbonamento dell'attività: canone (vuoto = canone di base) e periodo in cui è attivo.</summary>
    public async Task<(decimal? Fee, DateTime? From, DateTime? To)> FeeAsync(int gymId) =>
        (await _db.QueryAsync("SELECT MonthlyFeeEur, FeeStartsOn, FeeEndsOn FROM Gyms WHERE Id=@gymId", new { gymId },
            r => (r.IsDBNull(0) ? (decimal?)null : r.GetDecimal(0), r.Date("FeeStartsOn"), r.Date("FeeEndsOn")))).FirstOrDefault();

    public Task SaveFeeAsync(int gymId, decimal? fee, DateTime? from, DateTime? to) => _db.ExecuteAsync(
        "UPDATE Gyms SET MonthlyFeeEur=@fee, FeeStartsOn=@from, FeeEndsOn=@to WHERE Id=@gymId", new { gymId, fee, from, to });

    public Task SaveActivityLogoAsync(int gymId, byte[]? data, string? type) => _db.ExecuteAsync(
        "UPDATE Gyms SET LogoData=@data, LogoType=@type, UpdatedAt=UTC_TIMESTAMP() WHERE Id=@gymId", new { gymId, data, type });

    public async Task<(byte[] Data, string Type, DateTime? At)?> ActivityLogoAsync(int gymId)
    {
        var rows = await _db.QueryAsync("SELECT LogoData, LogoType, UpdatedAt FROM Gyms WHERE Id=@gymId AND LogoData IS NOT NULL", new { gymId },
            r => ((byte[])r.GetValue(0), r.Str("LogoType") ?? "image/png", r.Date("UpdatedAt")));
        return rows.Count == 0 ? null : rows[0];
    }

    /// <summary>Come appare mvchat a chi lavora in un'attività: nome, logo e colore dell'attività, altrimenti quelli del gruppo.</summary>
    public Task<Branding?> ActivityBrandingAsync(int gymId) => _db.FirstAsync(
        @"SELECT g.Id, g.Name, o.Id AS OrgId, o.Name AS OrgName, COALESCE(g.PrimaryColor, o.PrimaryColor) AS Color, COALESCE(g.Sector, o.Sector) AS Sector,
                 g.LogoData IS NOT NULL AS GymLogo, o.LogoData IS NOT NULL AS OrgLogo, o.LogoUrl, g.UpdatedAt AS GymAt, o.UpdatedAt AS OrgAt
          FROM Gyms g JOIN Organizations o ON o.Id=g.OrganizationId WHERE g.Id=@gymId", new { gymId },
        r => new Branding(r.Str("Name")!,
            Convert.ToInt32(r.GetValue(r.GetOrdinal("GymLogo"))) == 1 ? $"/logo/a/{r.Int("Id")}?v={(r.Date("GymAt") ?? DateTime.MinValue).Ticks}"
            : Convert.ToInt32(r.GetValue(r.GetOrdinal("OrgLogo"))) == 1 ? $"/logo/{r.Int("OrgId")}?v={(r.Date("OrgAt") ?? DateTime.MinValue).Ticks}"
            : r.Str("LogoUrl"),
            r.Str("Color")!, r.Str("Sector") ?? "palestra"));

    private static Gym MapGym(DbDataReader r) => new(
        r.Int("Id"), r.Int("OrganizationId"), r.Str("OrgName")!, r.Str("Name")!, r.Str("City"), r.Str("Address"),
        r.Str("Phone"), r.Bool("IsActive"), Convert.ToInt32(r["UserCount"]), r.Bool("IsGroup"), r.Str("Sector") ?? "palestra", r.Str("OwnSector"));

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

    /// <summary>Conta il tentativo sbagliato nel database stesso: anche con molti tentativi in parallelo il blocco scatta.</summary>
    public Task LoginFailedAsync(int id, int maxAttempts, DateTime lockUntil) => _db.ExecuteAsync(
        @"UPDATE Users SET FailedLogins=FailedLogins+1,
            LockedUntil=CASE WHEN FailedLogins >= @maxAttempts THEN @lockUntil ELSE LockedUntil END WHERE Id=@id",
        new { id, maxAttempts, lockUntil });

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
