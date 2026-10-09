using System.Data.Common;
using MvChat.Web.Infrastructure;
using MvChat.Web.Security;

namespace MvChat.Web.Catalog;

public sealed class GymProfile
{
    public int GymId { get; set; }
    public string? OpeningHours { get; set; }
    public string? Services { get; set; }
    public string? Classes { get; set; }
    public string? HowToReach { get; set; }
    public string? ExtraInfo { get; set; }
    public string AssistantName { get; set; } = "assistente virtuale";
    public string Formality { get; set; } = "tu";
    /// <summary>Link per prenotare una visita, una prova o un appuntamento (vale anche senza offerta).</summary>
    public string? BookingUrl { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Quanto è completa la scheda: serve a ricordare all'attività cosa manca.</summary>
    public int Completeness => new[] { OpeningHours, Services, Classes, HowToReach, ExtraInfo }.Count(s => !string.IsNullOrWhiteSpace(s)) * 20;
}

public sealed class Offer
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public int GymId { get; set; }
    public string GymName { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public decimal? Price { get; set; }
    public decimal? FullPrice { get; set; }
    public string? PriceNote { get; set; }
    public string? Conditions { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public int MaxExtraDiscountPct { get; set; }
    /// <summary>Link per pagare / acquistare / aderire all'offerta.</summary>
    public string? ActionUrl { get; set; }
    /// <summary>Link per prenotare (es. la prima lezione o la visita) legato all'offerta.</summary>
    public string? BookUrl { get; set; }
    public bool IsActive { get; set; } = true;

    public bool IsExpired => ValidTo is { } to && to.Date < DateTime.UtcNow.ToRome().Date;
    public bool NotStarted => ValidFrom is { } from && from.Date > DateTime.UtcNow.ToRome().Date;
    public string Status => !IsActive ? "Disattivata" : IsExpired ? "Scaduta" : NotStarted ? "Non ancora valida" : "Attiva";
}

public sealed class GoalModel
{
    public int Id { get; set; }
    public int? OrganizationId { get; set; }
    public string? OrganizationName { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Success { get; set; } = "";
    public string Instructions { get; set; } = "";
    public string? TemplateSuggestion { get; set; }
    public bool NeedsOffer { get; set; } = true;
    public int MaxAiMessages { get; set; } = 8;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; } = 100;
    public bool IsStandard => OrganizationId is null;
}

/// <summary>Scheda attività, offerte e modelli di obiettivo, sempre filtrati in base a chi chiede.</summary>
public sealed class CatalogRepo
{
    private readonly Db _db;
    public CatalogRepo(Db db) => _db = db;

    // ---------- Scheda attività ----------
    /// <summary>Per le istruzioni dell'assistente: tipo e presentazione dell'attività, più quella del gruppo se ne fa parte.</summary>
    public Task<OrgInfo?> ActivityInfoAsync(int gymId) => _db.FirstAsync(
        @"SELECT g.Id, g.Name, COALESCE(g.Sector, o.Sector) AS Sector, g.Description, COALESCE(g.Website, o.Website) AS Website,
                 COALESCE(g.Phone, o.Phone) AS Phone, o.IsGroup, o.Name AS GroupName, o.Description AS GroupDescription,
                 COALESCE(g.PrivacyUrl, o.PrivacyUrl) AS PrivacyUrl
          FROM Gyms g JOIN Organizations o ON o.Id=g.OrganizationId WHERE g.Id=@gymId", new { gymId },
        r => new OrgInfo(r.Int("Id"), r.Str("Name")!, r.Str("Sector") ?? "palestra", r.Str("Description"), r.Str("Website"), r.Str("Phone"),
            r.Bool("IsGroup") ? r.Str("GroupName") : null, r.Bool("IsGroup") ? r.Str("GroupDescription") : null, r.Str("PrivacyUrl")));

    /// <summary>Tipo di attività e presentazione del gruppo.</summary>
    public Task<OrgInfo?> OrgInfoAsync(int orgId) => _db.FirstAsync(
        "SELECT Id, Name, Sector, Description, Website, Phone FROM Organizations WHERE Id=@orgId", new { orgId },
        r => new OrgInfo(r.Int("Id"), r.Str("Name")!, r.Str("Sector") ?? "palestra", r.Str("Description"), r.Str("Website"), r.Str("Phone")));

    public async Task<GymProfile> ProfileAsync(int gymId) =>
        await _db.FirstAsync("SELECT * FROM GymProfiles WHERE GymId=@gymId", new { gymId }, r => new GymProfile
        {
            GymId = r.Int("GymId"), OpeningHours = r.Str("OpeningHours"), Services = r.Str("Services"), Classes = r.Str("Classes"),
            HowToReach = r.Str("HowToReach"), ExtraInfo = r.Str("ExtraInfo"), AssistantName = r.Str("AssistantName")!,
            Formality = r.Str("Formality")!, BookingUrl = r.Str("BookingUrl"), UpdatedAt = r.Date("UpdatedAt")
        }) ?? new GymProfile { GymId = gymId };

    public Task SaveProfileAsync(GymProfile p, int userId) => _db.ExecuteAsync(
        @"INSERT INTO GymProfiles (GymId, OpeningHours, Services, Classes, HowToReach, ExtraInfo, AssistantName, Formality, BookingUrl, UpdatedBy)
          VALUES (@GymId, @OpeningHours, @Services, @Classes, @HowToReach, @ExtraInfo, @AssistantName, @Formality, @BookingUrl, @userId)
          ON DUPLICATE KEY UPDATE OpeningHours=@OpeningHours, Services=@Services, Classes=@Classes, HowToReach=@HowToReach,
            ExtraInfo=@ExtraInfo, AssistantName=@AssistantName, Formality=@Formality, BookingUrl=@BookingUrl, UpdatedBy=@userId",
        new { p.GymId, p.OpeningHours, p.Services, p.Classes, p.HowToReach, p.ExtraInfo, p.AssistantName, p.Formality, p.BookingUrl, userId });

    public async Task<Dictionary<int, int>> CompletenessAsync(IEnumerable<int> gymIds)
    {
        var ids = gymIds.ToList();
        var result = ids.ToDictionary(i => i, _ => 0);
        if (ids.Count == 0) return result;
        var rows = await _db.QueryAsync($"SELECT * FROM GymProfiles WHERE GymId IN ({string.Join(",", ids)})", null, r => new GymProfile
        {
            GymId = r.Int("GymId"), OpeningHours = r.Str("OpeningHours"), Services = r.Str("Services"), Classes = r.Str("Classes"),
            HowToReach = r.Str("HowToReach"), ExtraInfo = r.Str("ExtraInfo")
        });
        foreach (var p in rows) result[p.GymId] = p.Completeness;
        return result;
    }

    // ---------- Offerte ----------
    public Task<List<Offer>> OffersAsync(Scope s, int? gymId = null, int? offerId = null) => _db.QueryAsync(
        @"SELECT o.*, g.Name AS GymName FROM Offers o JOIN Gyms g ON g.Id=o.GymId
          WHERE (@All=1 OR (@IsOrg=1 AND o.OrganizationId=@Org) OR (o.GymId=@Gym OR FIND_IN_SET(o.GymId, @Gyms)))
            AND (@FilterGym=-1 OR o.GymId=@FilterGym) AND (@FilterId=-1 OR o.Id=@FilterId)
          ORDER BY o.IsActive DESC, COALESCE(o.ValidTo, '2999-12-31') DESC, o.Id DESC",
        new { All = s.IsSuperAdmin ? 1 : 0, IsOrg = s.IsOrgAdmin ? 1 : 0, Org = s.OrganizationId ?? -1, Gym = s.GymId ?? -1, Gyms = s.AreaGymsCsv,
              FilterGym = gymId ?? -1, FilterId = offerId ?? -1 }, MapOffer);

    public async Task<Offer?> OfferAsync(Scope s, int id) => (await OffersAsync(s, offerId: id)).FirstOrDefault();

    private static Offer MapOffer(DbDataReader r) => new()
    {
        Id = r.Int("Id"), OrganizationId = r.Int("OrganizationId"), GymId = r.Int("GymId"), GymName = r.Str("GymName")!,
        Title = r.Str("Title")!, Description = r.Str("Description"), Price = Dec(r, "Price"), FullPrice = Dec(r, "FullPrice"),
        PriceNote = r.Str("PriceNote"), Conditions = r.Str("Conditions"), ValidFrom = r.Date("ValidFrom"), ValidTo = r.Date("ValidTo"),
        MaxExtraDiscountPct = Convert.ToInt32(r["MaxExtraDiscountPct"]), ActionUrl = r.Str("ActionUrl"), BookUrl = r.Str("BookUrl"), IsActive = r.Bool("IsActive")
    };

    private static decimal? Dec(DbDataReader r, string col) { var i = r.GetOrdinal(col); return r.IsDBNull(i) ? null : r.GetDecimal(i); }

    public async Task<int> SaveOfferAsync(Offer o, int userId)
    {
        var args = new { o.Id, o.OrganizationId, o.GymId, o.Title, o.Description, o.Price, o.FullPrice, o.PriceNote, o.Conditions,
                         o.ValidFrom, o.ValidTo, o.MaxExtraDiscountPct, o.ActionUrl, o.BookUrl, o.IsActive, userId };
        if (o.Id == 0)
            return await _db.ScalarAsync<int>(
                @"INSERT INTO Offers (OrganizationId, GymId, Title, Description, Price, FullPrice, PriceNote, Conditions, ValidFrom, ValidTo, MaxExtraDiscountPct, ActionUrl, BookUrl, IsActive, CreatedBy)
                  VALUES (@OrganizationId, @GymId, @Title, @Description, @Price, @FullPrice, @PriceNote, @Conditions, @ValidFrom, @ValidTo, @MaxExtraDiscountPct, @ActionUrl, @BookUrl, @IsActive, @userId);
                  SELECT LAST_INSERT_ID();", args);
        await _db.ExecuteAsync(
            @"UPDATE Offers SET Title=@Title, Description=@Description, Price=@Price, FullPrice=@FullPrice, PriceNote=@PriceNote,
              Conditions=@Conditions, ValidFrom=@ValidFrom, ValidTo=@ValidTo, MaxExtraDiscountPct=@MaxExtraDiscountPct,
              ActionUrl=@ActionUrl, BookUrl=@BookUrl, IsActive=@IsActive WHERE Id=@Id AND GymId=@GymId", args);
        return o.Id;
    }

    // ---------- Modelli di obiettivo ----------
    /// <summary>Modelli standard MVitalia più quelli del gruppo di chi chiede (MVitalia li vede tutti).</summary>
    public Task<List<GoalModel>> ModelsAsync(Scope s, int? modelId = null, bool onlyActive = false) => _db.QueryAsync(
        @"SELECT m.*, o.Name AS OrgName FROM GoalModels m LEFT JOIN Organizations o ON o.Id=m.OrganizationId
          WHERE (m.OrganizationId IS NULL OR @All=1 OR m.OrganizationId=@Org)
            AND (@FilterId=-1 OR m.Id=@FilterId) AND (@OnlyActive=0 OR m.IsActive=1)
          ORDER BY (m.OrganizationId IS NULL), m.SortOrder, m.Name",
        new { All = s.IsSuperAdmin ? 1 : 0, Org = s.OrganizationId ?? -1, FilterId = modelId ?? -1, OnlyActive = onlyActive ? 1 : 0 },
        r => new GoalModel
        {
            Id = r.Int("Id"), OrganizationId = r.IntN("OrganizationId"), OrganizationName = r.Str("OrgName"), Code = r.Str("Code")!,
            Name = r.Str("Name")!, Success = r.Str("Success")!, Instructions = r.Str("Instructions")!, TemplateSuggestion = r.Str("TemplateSuggestion"),
            NeedsOffer = r.Bool("NeedsOffer"), MaxAiMessages = Convert.ToInt32(r["MaxAiMessages"]), IsActive = r.Bool("IsActive"),
            SortOrder = r.Int("SortOrder")
        });

    public async Task<GoalModel?> ModelAsync(Scope s, int id) => (await ModelsAsync(s, id)).FirstOrDefault();

    public async Task<int> SaveModelAsync(GoalModel m)
    {
        var args = new { m.Id, m.OrganizationId, m.Code, m.Name, m.Success, m.Instructions, m.TemplateSuggestion, m.NeedsOffer, m.MaxAiMessages, m.IsActive, m.SortOrder };
        if (m.Id == 0)
            return await _db.ScalarAsync<int>(
                @"INSERT INTO GoalModels (OrganizationId, Code, Name, Success, Instructions, TemplateSuggestion, NeedsOffer, MaxAiMessages, IsActive, SortOrder)
                  VALUES (@OrganizationId, @Code, @Name, @Success, @Instructions, @TemplateSuggestion, @NeedsOffer, @MaxAiMessages, @IsActive, @SortOrder);
                  SELECT LAST_INSERT_ID();", args);
        await _db.ExecuteAsync(
            @"UPDATE GoalModels SET Name=@Name, Success=@Success, Instructions=@Instructions, TemplateSuggestion=@TemplateSuggestion,
              NeedsOffer=@NeedsOffer, MaxAiMessages=@MaxAiMessages, IsActive=@IsActive, SortOrder=@SortOrder WHERE Id=@Id", args);
        return m.Id;
    }

    /// <summary>Chi può modificare un modello: gli standard solo MVitalia, quelli di gruppo la direzione del gruppo.</summary>
    public static bool CanEdit(Scope s, GoalModel m) => s.IsSuperAdmin || (s.IsOrgAdmin && m.OrganizationId == s.OrganizationId);
}
