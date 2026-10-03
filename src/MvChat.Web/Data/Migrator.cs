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

    /// <summary>Ultimo errore dell'aggiornamento automatico all'avvio (null = tutto a posto). Lo mostrano /health e il pannello di MVitalia.</summary>
    public static string? LastError { get; set; }

    public static async Task<List<string>> ApplyAsync(string connectionString)
    {
        var applied = new List<string>();
        await using var cn = await Db.OpenAsync(connectionString);

        // Un solo aggiornamento alla volta: se IIS avvia due copie del programma, la seconda aspetta la prima.
        await using (var lk = Db.Command(cn, "SELECT GET_LOCK('mvchat_schema', 120)", null))
            if (Convert.ToInt32(await lk.ExecuteScalarAsync() ?? 0) != 1)
                throw new InvalidOperationException("Aggiornamento del database già in corso da un'altra parte: riprovo al prossimo avvio.");
        try
        {
            foreach (var ddl in new[] {
                "CREATE TABLE IF NOT EXISTS SchemaVersions (Version INT PRIMARY KEY, Name VARCHAR(200) NOT NULL, AppliedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",
                // Ricorda i singoli pezzi (separati da GO) già eseguiti: se uno script si interrompe a metà, si riprende dal pezzo che ha dato errore.
                "CREATE TABLE IF NOT EXISTS SchemaBatches (Version INT NOT NULL, Batch INT NOT NULL, AppliedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP, PRIMARY KEY (Version, Batch)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;" })
                await using (var create = Db.Command(cn, ddl, null))
                    await create.ExecuteNonQueryAsync();

            var done = new HashSet<int>();
            await using (var q = Db.Command(cn, "SELECT Version FROM SchemaVersions", null))
            await using (var r = await q.ExecuteReaderAsync())
                while (await r.ReadAsync()) done.Add(r.GetInt32(0));

            foreach (var (version, name, sql) in Scripts())
            {
                if (done.Contains(version)) continue;
                var batchesDone = new HashSet<int>();
                await using (var q = Db.Command(cn, "SELECT Batch FROM SchemaBatches WHERE Version=@version", new { version }))
                await using (var r = await q.ExecuteReaderAsync())
                    while (await r.ReadAsync()) batchesDone.Add(r.GetInt32(0));
                // In MySQL le istruzioni CREATE/ALTER confermano subito da sole: niente transazione,
                // quindi ogni pezzo eseguito viene segnato e non si ripete al tentativo successivo.
                var batches = GoSplit.Split(sql).Where(b => !string.IsNullOrWhiteSpace(b)).ToList();
                for (var i = 0; i < batches.Count; i++)
                {
                    if (batchesDone.Contains(i)) continue;
                    await using (var cmd = Db.Command(cn, batches[i], null))
                    {
                        cmd.CommandTimeout = 300;
                        try { await cmd.ExecuteNonQueryAsync(); }
                        catch (Exception ex) { throw new InvalidOperationException($"Aggiornamento {name}, parte {i + 1}: {ex.Message}", ex); }
                    }
                    await using (var mark = Db.Command(cn, "INSERT IGNORE INTO SchemaBatches (Version, Batch) VALUES (@version, @i)", new { version, i }))
                        await mark.ExecuteNonQueryAsync();
                }
                await using (var mark = Db.Command(cn, "INSERT INTO SchemaVersions (Version, Name) VALUES (@Version, @Name)", new { Version = version, Name = name }))
                    await mark.ExecuteNonQueryAsync();
                applied.Add(name);
            }
            return applied;
        }
        finally
        {
            await using var rl = Db.Command(cn, "SELECT RELEASE_LOCK('mvchat_schema')", null);
            try { await rl.ExecuteScalarAsync(); } catch { /* la connessione chiusa libera comunque il blocco */ }
        }
    }

    /// <summary>Quanti script sono applicati e se ne mancano (per /health e il pannello di MVitalia).</summary>
    public static async Task<(int Applied, int Expected)> StatusAsync(Db db)
    {
        var applied = await db.ScalarAsync<int>("SELECT COUNT(*) FROM SchemaVersions");
        return (applied, Scripts().Count);
    }
}
