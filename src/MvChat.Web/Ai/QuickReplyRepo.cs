using MvChat.Web.Infrastructure;
using MvChat.Web.Security;

namespace MvChat.Web.Ai;

public sealed record QuickReply(int Id, int OrganizationId, int? GymId, string? GymName, string Title, string Body);

/// <summary>Risposte rapide della reception: della singola palestra o di tutta la catena.</summary>
public sealed class QuickReplyRepo
{
    private readonly Db _db;
    public QuickReplyRepo(Db db) => _db = db;

    /// <summary>Quelle utilizzabili in una palestra: le sue e quelle comuni della catena.</summary>
    public Task<List<QuickReply>> ForGymAsync(int orgId, int gymId) => _db.QueryAsync(
        @"SELECT q.*, g.Name AS GymName FROM QuickReplies q LEFT JOIN Gyms g ON g.Id=q.GymId
          WHERE q.OrganizationId=@orgId AND (q.GymId IS NULL OR q.GymId=@gymId) ORDER BY q.Title",
        new { orgId, gymId }, Map);

    /// <summary>Quelle che l'utente può gestire: il responsabile solo le sue, la direzione tutte quelle della catena.</summary>
    public Task<List<QuickReply>> ManageableAsync(Scope s) => _db.QueryAsync(
        @"SELECT q.*, g.Name AS GymName FROM QuickReplies q LEFT JOIN Gyms g ON g.Id=q.GymId
          WHERE @All=1 OR (@IsOrg=1 AND q.OrganizationId=@Org) OR q.GymId=@Gym ORDER BY q.OrganizationId, g.Name, q.Title",
        new { All = s.IsSuperAdmin ? 1 : 0, IsOrg = s.IsOrgAdmin ? 1 : 0, Org = s.OrganizationId ?? -1, Gym = s.GymId ?? -1 }, Map);

    private static QuickReply Map(System.Data.Common.DbDataReader r) =>
        new(r.Int("Id"), r.Int("OrganizationId"), r.IntN("GymId"), r.Str("GymName"), r.Str("Title")!, r.Str("Body")!);

    public Task<int> AddAsync(int orgId, int? gymId, string title, string body, int userId) => _db.ExecuteAsync(
        "INSERT INTO QuickReplies (OrganizationId, GymId, Title, Body, CreatedBy) VALUES (@orgId, @gymId, @title, @body, @userId)",
        new { orgId, gymId, title, body, userId });

    public Task<int> DeleteAsync(int id) => _db.ExecuteAsync("DELETE FROM QuickReplies WHERE Id=@id", new { id });
}
