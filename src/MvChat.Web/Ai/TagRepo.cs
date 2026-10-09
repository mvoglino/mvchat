using MvChat.Web.Infrastructure;

namespace MvChat.Web.Ai;

/// <summary>Un'etichetta mostrata in pagina: del cliente (resta da una campagna all'altra) o della singola conversazione.</summary>
public sealed record TagItem(string Tag, bool OnContact);

/// <summary>
/// Etichette libere scritte dalla reception: sul cliente (per attività: «VIP», «richiamare a gennaio») o sulla
/// singola conversazione («reclamo», «da richiamare»). Non le legge l'assistente: sono note interne dello staff.
/// Il perimetro lo controlla la pagina della conversazione prima di chiamare questi metodi.
/// </summary>
public sealed class TagRepo
{
    public const int MaxLength = 30;
    private readonly Db _db;
    public TagRepo(Db db) => _db = db;

    /// <summary>Etichetta pulita: spazi tolti, niente caratteri strani, al massimo 30 caratteri. Null se vuota.</summary>
    public static string? Clean(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var t = new string(tag.Where(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '_' or '/' or '.' or '\'').ToArray());
        t = string.Join(' ', t.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (t.Length == 0) return null;
        return t.Length > MaxLength ? t[..MaxLength].Trim() : t;
    }

    public async Task<List<TagItem>> ForConversationAsync(long conversationId, int gymId, string phone)
    {
        var contact = await _db.QueryAsync("SELECT Tag FROM ContactTags WHERE GymId=@gymId AND Phone=@phone ORDER BY Tag", new { gymId, phone }, r => new TagItem(r.GetString(0), true));
        var conv = await _db.QueryAsync("SELECT Tag FROM ConversationTags WHERE ConversationId=@conversationId ORDER BY Tag", new { conversationId }, r => new TagItem(r.GetString(0), false));
        return contact.Concat(conv).ToList();
    }

    /// <summary>Etichette di più conversazioni insieme (per gli elenchi): cliente + conversazione.</summary>
    public async Task<Dictionary<long, List<TagItem>>> ForConversationsAsync(IEnumerable<long> ids)
    {
        var list = ids.Distinct().ToList();
        var result = list.ToDictionary(i => i, _ => new List<TagItem>());
        if (list.Count == 0) return result;
        var inList = string.Join(",", list); // numeri presi dal database, nessun testo libero
        var rows = await _db.QueryAsync(
            $@"SELECT c.Id, k.Tag, 1 AS OnContact FROM Conversations c JOIN ContactTags k ON k.GymId=c.GymId AND k.Phone=c.ContactPhone WHERE c.Id IN ({inList})
               UNION ALL
               SELECT t.ConversationId, t.Tag, 0 FROM ConversationTags t WHERE t.ConversationId IN ({inList})
               ORDER BY 3 DESC, 2", null,
            r => (Id: r.GetInt64(0), Tag: r.GetString(1), OnContact: Convert.ToInt32(r.GetValue(2)) == 1));
        foreach (var (id, tag, onContact) in rows) result[id].Add(new TagItem(tag, onContact));
        return result;
    }

    /// <summary>Etichette già usate nell'attività: si propongono mentre si scrive, così non nascono doppioni.</summary>
    public Task<List<string>> SuggestionsAsync(int gymId) => _db.QueryAsync(
        @"SELECT Tag FROM (SELECT Tag FROM ContactTags WHERE GymId=@gymId
                           UNION SELECT t.Tag FROM ConversationTags t JOIN Conversations c ON c.Id=t.ConversationId WHERE c.GymId=@gymId) x
          ORDER BY Tag LIMIT 100", new { gymId }, r => r.GetString(0));

    /// <summary>Etichette usate nelle attività visibili (per i filtri).</summary>
    public Task<List<string>> UsedAsync(IEnumerable<int> gymIds)
    {
        var ids = gymIds.Distinct().ToList();
        if (ids.Count == 0) return Task.FromResult(new List<string>());
        var inList = string.Join(",", ids);
        return _db.QueryAsync(
            $@"SELECT Tag FROM (SELECT Tag FROM ContactTags WHERE GymId IN ({inList})
                               UNION SELECT t.Tag FROM ConversationTags t JOIN Conversations c ON c.Id=t.ConversationId WHERE c.GymId IN ({inList})) x
               ORDER BY Tag LIMIT 200", null, r => r.GetString(0));
    }

    public Task AddToContactAsync(int orgId, int gymId, string phone, string tag, int userId) => _db.ExecuteAsync(
        "INSERT IGNORE INTO ContactTags (OrganizationId, GymId, Phone, Tag, CreatedBy) VALUES (@orgId, @gymId, @phone, @tag, @userId)",
        new { orgId, gymId, phone, tag, userId });

    public Task RemoveFromContactAsync(int gymId, string phone, string tag) =>
        _db.ExecuteAsync("DELETE FROM ContactTags WHERE GymId=@gymId AND Phone=@phone AND Tag=@tag", new { gymId, phone, tag });

    public Task AddToConversationAsync(long conversationId, string tag, int userId) => _db.ExecuteAsync(
        "INSERT IGNORE INTO ConversationTags (ConversationId, Tag, CreatedBy) VALUES (@conversationId, @tag, @userId)", new { conversationId, tag, userId });

    public Task RemoveFromConversationAsync(long conversationId, string tag) =>
        _db.ExecuteAsync("DELETE FROM ConversationTags WHERE ConversationId=@conversationId AND Tag=@tag", new { conversationId, tag });
}
