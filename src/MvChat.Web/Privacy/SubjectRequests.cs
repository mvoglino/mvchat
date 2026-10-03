using MvChat.Web.Infrastructure;
using MvChat.Web.Security;

namespace MvChat.Web.Privacy;

public sealed record FoundContact(long Id, int OrganizationId, string ListName, string GymName, string FirstName, string? LastName, string? Email, string? Membership, DateTime? ExpiresOn, DateTime? ConsentDate, string? ConsentSource, DateTime CreatedAt);
public sealed record FoundConversation(long Id, int OrganizationId, string GymName, string GoalName, string Status, string Outcome, DateTime CreatedAt, int Messages);
public sealed record FoundMessage(long ConversationId, string Direction, string? Body, DateTime CreatedAt);
public sealed record FoundOptOut(string OrgName, string? GymName, string? Reason, string Source, DateTime CreatedAt);

/// <summary>
/// Richieste dei clienti sui loro dati (GDPR: accesso e cancellazione). Cerca un numero di cellulare
/// solo dentro il perimetro di chi chiede: l'attività vede la sua, il gruppo le sue attività, MVitalia tutto.
/// </summary>
public sealed class SubjectRequests
{
    private readonly Db _db;
    public SubjectRequests(Db db) => _db = db;

    private const string G = "(@All=1 OR (@IsOrg=1 AND g.OrganizationId=@Org) OR g.Id=@Gym)";
    private static object A(Scope s, string phone) => new { All = s.IsSuperAdmin ? 1 : 0, IsOrg = s.IsOrgAdmin ? 1 : 0, Org = s.OrganizationId ?? -1, Gym = s.GymId ?? -1, phone };

    /// <summary>Il numero mascherato per il registro: si sa che c'è stata una richiesta, non di chi.</summary>
    public static string Mask(string phone) => phone.Length <= 9 ? (phone.Length <= 4 ? "***" : phone[..3] + "***") : phone[..5] + new string('*', phone.Length - 9) + phone[^4..];

    public Task<List<FoundContact>> ContactsAsync(Scope s, string phone) => _db.QueryAsync(
        $@"SELECT k.Id, g.OrganizationId, l.Name AS ListName, g.Name AS GymName, k.FirstName, k.LastName, k.Email, k.Membership, k.ExpiresOn, k.ConsentDate, k.ConsentSource, k.CreatedAt
           FROM Contacts k JOIN ContactLists l ON l.Id=k.ListId JOIN Gyms g ON g.Id=k.GymId WHERE {G} AND k.Phone=@phone ORDER BY k.CreatedAt",
        A(s, phone), r => new FoundContact(r.GetInt64(0), r.Int("OrganizationId"), r.Str("ListName")!, r.Str("GymName")!, r.Str("FirstName")!, r.Str("LastName"), r.Str("Email"),
            r.Str("Membership"), r.Date("ExpiresOn"), r.Date("ConsentDate"), r.Str("ConsentSource"), r.Date("CreatedAt")!.Value));

    public Task<List<FoundConversation>> ConversationsAsync(Scope s, string phone) => _db.QueryAsync(
        $@"SELECT c.Id, g.OrganizationId, g.Name AS GymName, COALESCE(m.Name, 'Messaggio spontaneo') AS GoalName, c.Status, c.Outcome, c.CreatedAt,
                  (SELECT COUNT(*) FROM WaMessages w WHERE w.ConversationId=c.Id) AS N
           FROM Conversations c JOIN Gyms g ON g.Id=c.GymId LEFT JOIN GoalModels m ON m.Id=c.GoalModelId WHERE {G} AND c.ContactPhone=@phone ORDER BY c.CreatedAt",
        A(s, phone), r => new FoundConversation(r.GetInt64(0), r.Int("OrganizationId"), r.Str("GymName")!, r.Str("GoalName")!, r.Str("Status")!, r.Str("Outcome")!, r.Date("CreatedAt")!.Value,
            Convert.ToInt32(r.GetValue(r.GetOrdinal("N")))));

    public Task<List<FoundMessage>> MessagesAsync(Scope s, string phone) => _db.QueryAsync(
        $@"SELECT COALESCE(w.ConversationId, 0) AS C, w.Direction, w.Body, w.CreatedAt FROM WaMessages w JOIN Gyms g ON g.Id=w.GymId
           WHERE {G} AND w.ContactPhone=@phone ORDER BY w.Id",
        A(s, phone), r => new FoundMessage(Convert.ToInt64(r.GetValue(0)), r.Str("Direction")!, r.Str("Body"), r.Date("CreatedAt")!.Value));

    public Task<int> RecipientsCountAsync(Scope s, string phone) => _db.ScalarAsync<int>(
        $"SELECT COUNT(*) FROM CampaignRecipients r JOIN Campaigns k ON k.Id=r.CampaignId JOIN Gyms g ON g.Id=k.GymId WHERE {G} AND r.Phone=@phone", A(s, phone));

    public Task<List<FoundOptOut>> OptOutsAsync(Scope s, string phone) => _db.QueryAsync(
        @"SELECT o.Name AS OrgName, g.Name AS GymName, x.Reason, x.Source, x.CreatedAt FROM OptOuts x JOIN Organizations o ON o.Id=x.OrganizationId
          LEFT JOIN Gyms g ON g.Id=x.GymId WHERE (@All=1 OR x.OrganizationId=@Org) AND x.Phone=@phone",
        A(s, phone), r => new FoundOptOut(r.Str("OrgName")!, r.Str("GymName"), r.Str("Reason"), r.Str("Source")!, r.Date("CreatedAt")!.Value));

    /// <summary>Cancella tutti i dati del numero nel perimetro (messaggi, conversazioni, destinatari, contatti). La lista STOP resta.</summary>
    public async Task<(int Conversations, int Messages, int Contacts, int Recipients)> EraseAsync(Scope s, string phone)
    {
        var a = A(s, phone);
        var convIds = $"SELECT c.Id FROM Conversations c JOIN Gyms g ON g.Id=c.GymId WHERE {G} AND c.ContactPhone=@phone";
        await _db.ExecuteAsync($"UPDATE AiUsage SET ConversationId=NULL WHERE ConversationId IN (SELECT Id FROM ({convIds}) x)", a);
        var msgs = await _db.ExecuteAsync($"DELETE w FROM WaMessages w JOIN Gyms g ON g.Id=w.GymId WHERE {G} AND w.ContactPhone=@phone", a);
        var recipients = await _db.ExecuteAsync($"DELETE r FROM CampaignRecipients r JOIN Campaigns k ON k.Id=r.CampaignId JOIN Gyms g ON g.Id=k.GymId WHERE {G} AND r.Phone=@phone", a);
        var convs = await _db.ExecuteAsync($"DELETE c FROM Conversations c JOIN Gyms g ON g.Id=c.GymId WHERE {G} AND c.ContactPhone=@phone", a);
        var contacts = await _db.ExecuteAsync($"DELETE k FROM Contacts k JOIN Gyms g ON g.Id=k.GymId WHERE {G} AND k.Phone=@phone", a);

        // Anche le righe scartate all'import (numero scritto in altro modo: si confrontano le ultime 9 cifre).
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        var last9 = digits.Length > 9 ? digits[^9..] : digits;
        if (last9.Length >= 6)
            await _db.ExecuteAsync(
                $@"DELETE x FROM ContactRejects x JOIN ContactLists l ON l.Id=x.ListId JOIN Gyms g ON g.Id=l.GymId
                   WHERE {G} AND x.Phone IS NOT NULL AND RIGHT(REGEXP_REPLACE(x.Phone, '[^0-9]', ''), 9) = @last9",
                new { All = s.IsSuperAdmin ? 1 : 0, IsOrg = s.IsOrgAdmin ? 1 : 0, Org = s.OrganizationId ?? -1, Gym = s.GymId ?? -1, last9 });

        // Nel registro attività il numero resta solo mascherato.
        await _db.ExecuteAsync(
            @"UPDATE AuditLog SET Detail=REPLACE(Detail, @phone, @masked)
              WHERE Detail LIKE CONCAT('%', @phone, '%') AND (@All=1 OR (@IsOrg=1 AND OrganizationId=@Org) OR GymId=@Gym)",
            new { All = s.IsSuperAdmin ? 1 : 0, IsOrg = s.IsOrgAdmin ? 1 : 0, Org = s.OrganizationId ?? -1, Gym = s.GymId ?? -1, phone, masked = Mask(phone) });

        // Copie grezze degli avvisi di Meta (tenute al massimo 30 giorni) che contengono quel numero, sui numeri WhatsApp del perimetro.
        var ids = await _db.QueryAsync(
            "SELECT n.PhoneNumberId FROM WaNumbers n JOIN Gyms g ON g.Id=n.GymId WHERE " + G + " AND n.PhoneNumberId IS NOT NULL",
            a, r => r.GetString(0));
        foreach (var pid in ids)
            await _db.ExecuteAsync(
                "DELETE FROM WaWebhookEvents WHERE Payload LIKE CONCAT('%', @digits, '%') AND Payload LIKE CONCAT('%', @pid, '%')",
                new { digits, pid });
        return (convs, msgs, contacts, recipients);
    }
}
