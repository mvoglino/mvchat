using System.Data.Common;
using System.Text;
using System.Text.Json;
using MvChat.Web.Data;
using MvChat.Web.Infrastructure;
using MvChat.Web.Security;

namespace MvChat.Web.Contacts;

public sealed record ContactList(int Id, int OrganizationId, int GymId, string GymName, string Name, string? FileName,
    int RowsRead, int ValidCount, int NoConsent, int BadPhone, int Duplicates, int OptedOut, DateTime CreatedAt, string? CreatedByName);
public sealed record Contact(long Id, string FirstName, string? LastName, string Phone, string? Email, string? Membership, DateTime? ExpiresOn, DateTime? ConsentDate, string? ConsentSource, bool OptedOut);
public sealed record Reject(int RowNumber, string? Name, string? Phone, string Reason);
public sealed record OptOut(long Id, int OrganizationId, string OrganizationName, string? GymName, string Phone, string? Reason, string Source, DateTime CreatedAt);

/// <summary>Quali colonne dell'Excel corrispondono a quali dati. -1 = colonna non presente.</summary>
public sealed class ColumnMap
{
    public int FirstName { get; set; } = -1;
    public int LastName { get; set; } = -1;
    public int Phone { get; set; } = -1;
    public int Email { get; set; } = -1;
    public int Membership { get; set; } = -1;
    public int ExpiresOn { get; set; } = -1;
    public int Consent { get; set; } = -1;
    public int ConsentDate { get; set; } = -1;
    public int ConsentSource { get; set; } = -1;
    /// <summary>Intestazioni scelte, per riconoscere lo stesso file la volta dopo.</summary>
    public Dictionary<string, string> Headers { get; set; } = new();
}

public sealed class ImportRequest
{
    public int GymId { get; set; }
    public string Name { get; set; } = "";
    public string? FileName { get; set; }
    public ColumnMap Map { get; set; } = new();
    public List<List<string?>> Rows { get; set; } = new();
}

public sealed record ImportResult(int ListId, int RowsRead, int Valid, int NoConsent, int BadPhone, int Duplicates, int OptedOut, int NoName);

public sealed class ContactsRepo
{
    public const int MaxRows = 20000;
    private readonly Db _db;
    public ContactsRepo(Db db) => _db = db;

    private const string ScopeWhere = "(@All=1 OR (@IsOrg=1 AND l.OrganizationId=@Org) OR l.GymId=@Gym)";

    // ---------- Liste ----------
    public Task<List<ContactList>> ListsAsync(Scope s, int? listId = null) => _db.QueryAsync(
        $@"SELECT l.*, g.Name AS GymName, u.FullName AS CreatedByName
           FROM ContactLists l JOIN Gyms g ON g.Id=l.GymId LEFT JOIN Users u ON u.Id=l.CreatedBy
           WHERE {ScopeWhere} {(listId is null ? "" : "AND l.Id=@ListId")}
           ORDER BY l.CreatedAt DESC, l.Id DESC",
        new { All = s.IsSuperAdmin ? 1 : 0, IsOrg = s.IsOrgAdmin ? 1 : 0, Org = s.OrganizationId ?? -1, Gym = s.GymId ?? -1, ListId = listId ?? -1 },
        MapList);

    public async Task<ContactList?> ListAsync(Scope s, int id) => (await ListsAsync(s, id)).FirstOrDefault();

    private static ContactList MapList(DbDataReader r) => new(
        r.Int("Id"), r.Int("OrganizationId"), r.Int("GymId"), r.Str("GymName")!, r.Str("Name")!, r.Str("FileName"),
        r.Int("RowsRead"), r.Int("ValidCount"), r.Int("NoConsent"), r.Int("BadPhone"), r.Int("Duplicates"), r.Int("OptedOut"),
        r.Date("CreatedAt")!.Value, r.Str("CreatedByName"));

    public Task<List<Contact>> ContactsAsync(int listId, int orgId, string? search, int limit) => _db.QueryAsync(
        @"SELECT c.*, (o.Id IS NOT NULL) AS IsOptedOut FROM Contacts c
          LEFT JOIN OptOuts o ON o.OrganizationId=@orgId AND o.Phone=c.Phone
          WHERE c.ListId=@listId AND (@q='' OR c.FirstName LIKE @like OR c.LastName LIKE @like OR c.Phone LIKE @like OR c.Email LIKE @like)
          ORDER BY c.LastName, c.FirstName LIMIT @limit",
        new { listId, orgId, q = search ?? "", like = "%" + (search ?? "") + "%", limit },
        r => new Contact(r.GetInt64(r.GetOrdinal("Id")), r.Str("FirstName")!, r.Str("LastName"), r.Str("Phone")!, r.Str("Email"),
            r.Str("Membership"), r.Date("ExpiresOn"), r.Date("ConsentDate"), r.Str("ConsentSource"), Convert.ToInt32(r["IsOptedOut"]) == 1));

    public Task<List<Reject>> RejectsAsync(int listId) => _db.QueryAsync(
        "SELECT RowNumber, Name, Phone, Reason FROM ContactRejects WHERE ListId=@listId ORDER BY RowNumber LIMIT 2000",
        new { listId }, r => new Reject(r.Int("RowNumber"), r.Str("Name"), r.Str("Phone"), r.Str("Reason")!));

    public Task DeleteListAsync(int id) => _db.ExecuteAsync("DELETE FROM ContactLists WHERE Id=@id", new { id });

    // ---------- Abbinamento colonne ricordato ----------
    public async Task<ColumnMap?> MappingAsync(int gymId)
    {
        var json = await _db.ScalarAsync<string>("SELECT MappingJson FROM ColumnMappings WHERE GymId=@gymId", new { gymId });
        return json is null ? null : JsonSerializer.Deserialize<ColumnMap>(json);
    }

    // ---------- Import ----------
    public async Task<ImportResult> ImportAsync(Scope s, int orgId, ImportRequest req)
    {
        var optedOut = new HashSet<string>(await _db.QueryAsync(
            "SELECT Phone FROM OptOuts WHERE OrganizationId=@orgId", new { orgId }, r => r.Str("Phone")!));

        var m = req.Map;
        string? Cell(List<string?> row, int i) => i >= 0 && i < row.Count ? row[i] : null;

        var valid = new List<object[]>();
        var rejects = new List<(int Row, string? Name, string? Phone, string Reason)>();
        var seen = new HashSet<string>();
        int noConsent = 0, badPhone = 0, dup = 0, opt = 0, noName = 0, rowsRead = 0;

        for (var i = 0; i < req.Rows.Count; i++)
        {
            var row = req.Rows[i];
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            rowsRead++;
            var excelRow = i + 2; // riga 1 = intestazioni
            var first = ImportRules.NiceName(Cell(row, m.FirstName), 100);
            var last = ImportRules.NiceName(Cell(row, m.LastName), 100);
            var rawPhone = ImportRules.Clean(Cell(row, m.Phone), 60);
            var display = string.Join(" ", new[] { first, last }.Where(x => x is not null));

            if (!ImportRules.IsYes(Cell(row, m.Consent))) { noConsent++; rejects.Add((excelRow, display, rawPhone, ImportRules.NoConsent)); continue; }
            var (phone, reason) = ImportRules.NormalizePhone(rawPhone);
            if (phone is null) { badPhone++; rejects.Add((excelRow, display, rawPhone, reason!)); continue; }
            if (first is null) { noName++; rejects.Add((excelRow, display, rawPhone, ImportRules.NoName)); continue; }
            if (!seen.Add(phone)) { dup++; rejects.Add((excelRow, display, phone, ImportRules.Duplicate)); continue; }
            if (optedOut.Contains(phone)) { opt++; rejects.Add((excelRow, display, phone, ImportRules.OptedOut)); continue; }

            valid.Add(new object[]
            {
                first, (object?)last ?? DBNull.Value, phone,
                (object?)ImportRules.Clean(Cell(row, m.Email), 200) ?? DBNull.Value,
                (object?)ImportRules.Clean(Cell(row, m.Membership), 100) ?? DBNull.Value,
                (object?)ImportRules.ParseDate(Cell(row, m.ExpiresOn)) ?? DBNull.Value,
                (object?)ImportRules.ParseDate(Cell(row, m.ConsentDate)) ?? DBNull.Value,
                (object?)ImportRules.Clean(Cell(row, m.ConsentSource), 100) ?? DBNull.Value,
            });
        }

        await using var cn = await _db.OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();

        int listId;
        await using (var ins = Db.Command(cn,
            @"INSERT INTO ContactLists (OrganizationId, GymId, Name, FileName, RowsRead, ValidCount, NoConsent, BadPhone, Duplicates, OptedOut, CreatedBy)
              VALUES (@orgId, @gymId, @name, @file, @rows, @valid, @noConsent, @bad, @dup, @opt, @uid); SELECT LAST_INSERT_ID();",
            new { orgId, gymId = req.GymId, name = req.Name.Trim(), file = ImportRules.Clean(req.FileName, 255), rows = rowsRead, valid = valid.Count,
                  noConsent, bad = badPhone + noName, dup, opt, uid = s.UserId }, tx))
            listId = Convert.ToInt32(await ins.ExecuteScalarAsync());

        // Inserimento a blocchi: una sola richiesta al database ogni 500 contatti.
        foreach (var chunk in valid.Chunk(500))
        {
            var sb = new StringBuilder("INSERT INTO Contacts (OrganizationId, GymId, ListId, FirstName, LastName, Phone, Email, Membership, ExpiresOn, ConsentDate, ConsentSource) VALUES ");
            await using var cmd = cn.CreateCommand();
            cmd.Transaction = tx;
            for (var k = 0; k < chunk.Length; k++)
            {
                sb.Append(k == 0 ? "" : ",").Append($"(@o,@g,@l,@a{k},@b{k},@c{k},@d{k},@e{k},@f{k},@h{k},@i{k})");
                var v = chunk[k];
                string[] names = { "a", "b", "c", "d", "e", "f", "h", "i" };
                for (var j = 0; j < names.Length; j++) AddParam(cmd, $"@{names[j]}{k}", v[j]);
            }
            AddParam(cmd, "@o", orgId); AddParam(cmd, "@g", req.GymId); AddParam(cmd, "@l", listId);
            cmd.CommandText = sb.ToString();
            await cmd.ExecuteNonQueryAsync();
        }

        foreach (var chunk in rejects.Take(5000).Chunk(500))
        {
            var sb = new StringBuilder("INSERT INTO ContactRejects (ListId, RowNumber, Name, Phone, Reason) VALUES ");
            await using var cmd = cn.CreateCommand();
            cmd.Transaction = tx;
            for (var k = 0; k < chunk.Length; k++)
            {
                sb.Append(k == 0 ? "" : ",").Append($"(@l,@r{k},@n{k},@p{k},@x{k})");
                AddParam(cmd, $"@r{k}", chunk[k].Row);
                AddParam(cmd, $"@n{k}", (object?)ImportRules.Clean(chunk[k].Name, 200) ?? DBNull.Value);
                AddParam(cmd, $"@p{k}", (object?)ImportRules.Clean(chunk[k].Phone, 60) ?? DBNull.Value);
                AddParam(cmd, $"@x{k}", chunk[k].Reason);
            }
            AddParam(cmd, "@l", listId);
            cmd.CommandText = sb.ToString();
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var map = Db.Command(cn,
            "INSERT INTO ColumnMappings (GymId, MappingJson) VALUES (@gymId, @json) ON DUPLICATE KEY UPDATE MappingJson=@json",
            new { gymId = req.GymId, json = JsonSerializer.Serialize(req.Map) }, tx))
            await map.ExecuteNonQueryAsync();

        await tx.CommitAsync();
        return new ImportResult(listId, rowsRead, valid.Count, noConsent, badPhone, dup, opt, noName);
    }

    private static void AddParam(DbCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }

    // ---------- Lista STOP ----------
    public Task<List<OptOut>> OptOutsAsync(Scope s, string? search) => _db.QueryAsync(
        @"SELECT o.*, org.Name AS OrgName, g.Name AS GymName FROM OptOuts o
          JOIN Organizations org ON org.Id=o.OrganizationId LEFT JOIN Gyms g ON g.Id=o.GymId
          WHERE (@All=1 OR o.OrganizationId=@Org) AND (@q='' OR o.Phone LIKE @like)
          ORDER BY o.CreatedAt DESC LIMIT 1000",
        new { All = s.IsSuperAdmin ? 1 : 0, Org = s.OrganizationId ?? -1, q = search ?? "", like = "%" + (search ?? "") + "%" },
        r => new OptOut(r.GetInt64(r.GetOrdinal("Id")), r.Int("OrganizationId"), r.Str("OrgName")!, r.Str("GymName"), r.Str("Phone")!,
            r.Str("Reason"), r.Str("Source")!, r.Date("CreatedAt")!.Value));

    public Task<int> AddOptOutAsync(int orgId, int? gymId, string phone, string? reason, string source, int? userId) => _db.ExecuteAsync(
        @"INSERT INTO OptOuts (OrganizationId, GymId, Phone, Reason, Source, CreatedBy) VALUES (@orgId, @gymId, @phone, @reason, @source, @userId)
          ON DUPLICATE KEY UPDATE Reason=COALESCE(@reason, Reason)",
        new { orgId, gymId, phone, reason, source, userId });

    public async Task<bool> IsOptedOutAsync(int orgId, string phone) =>
        await _db.ScalarAsync<long>("SELECT COUNT(*) FROM OptOuts WHERE OrganizationId=@orgId AND Phone=@phone", new { orgId, phone }) > 0;

    public Task<int> RemoveOptOutAsync(Scope s, long id) => _db.ExecuteAsync(
        "DELETE FROM OptOuts WHERE Id=@id AND (@All=1 OR OrganizationId=@Org)",
        new { id, All = s.IsSuperAdmin ? 1 : 0, Org = s.OrganizationId ?? -1 });
}
