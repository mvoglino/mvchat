using System.Reflection;
using System.Text.RegularExpressions;
using MvChat.Web.Infrastructure;

namespace MvChat.Web.Data;

/// <summary>
/// Applica in ordine gli script SQL incorporati (Data/Schema/NNN_nome.sql) non ancora eseguiti.
/// La tabella SchemaVersions ricorda quali sono già stati applicati, così ogni aggiornamento
/// del software porta con sé le modifiche al database senza interventi manuali.
/// </summary>
public static class Migrator
{
    private static readonly Regex GoSplit = new(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

    public static IReadOnlyList<(int Version, string Name, string Sql)> Scripts()
    {
        var asm = Assembly.GetExecutingAssembly();
        return asm.GetManifestResourceNames()
            .Where(n => n.Contains(".Data.Schema.") && n.EndsWith(".sql"))
            .Select(n =>
            {
                var file = n[(n.IndexOf(".Data.Schema.", StringComparison.Ordinal) + 13)..];
                var version = int.Parse(file[..3]);
                using var s = asm.GetManifestResourceStream(n)!;
                using var reader = new StreamReader(s);
                return (version, file, reader.ReadToEnd());
            })
            .OrderBy(x => x.version)
            .ToList();
    }

    public static async Task<List<string>> ApplyAsync(string connectionString)
    {
        var applied = new List<string>();
        await using var cn = await Db.OpenAsync(connectionString);

        await using (var create = Db.Command(cn,
            "CREATE TABLE IF NOT EXISTS SchemaVersions (Version INT PRIMARY KEY, Name VARCHAR(200) NOT NULL, AppliedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;", null))
            await create.ExecuteNonQueryAsync();

        var done = new HashSet<int>();
        await using (var q = Db.Command(cn, "SELECT Version FROM SchemaVersions", null))
        await using (var r = await q.ExecuteReaderAsync())
            while (await r.ReadAsync()) done.Add(r.GetInt32(0));

        foreach (var (version, name, sql) in Scripts())
        {
            if (done.Contains(version)) continue;
            // In MySQL le istruzioni CREATE/ALTER confermano subito da sole: niente transazione,
            // ogni script deve quindi essere scritto in modo da poter essere rieseguito se si interrompe.
            foreach (var batch in GoSplit.Split(sql).Where(b => !string.IsNullOrWhiteSpace(b)))
            {
                await using var cmd = Db.Command(cn, batch, null);
                cmd.CommandTimeout = 120;
                await cmd.ExecuteNonQueryAsync();
            }
            await using (var mark = Db.Command(cn, "INSERT INTO SchemaVersions (Version, Name) VALUES (@Version, @Name)", new { Version = version, Name = name }))
                await mark.ExecuteNonQueryAsync();
            applied.Add(name);
        }
        return applied;
    }
}
