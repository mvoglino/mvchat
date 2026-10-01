using System.Data;
using System.Data.Common;
using System.Reflection;

namespace MvChat.Web.Infrastructure;

/// <summary>
/// Accesso al database MySQL/MariaDB con ADO.NET puro: niente ORM, query esplicite e parametrizzate.
/// Ogni metodo apre e chiude la propria connessione (il pool la riusa).
/// </summary>
public sealed class Db
{
    private readonly AppConfigStore _config;
    public Db(AppConfigStore config) => _config = config;

    public static DbProviderFactory Factory =>
#if HAS_MYSQL
        MySqlConnector.MySqlConnectorFactory.Instance;
#else
        throw new InvalidOperationException("Build senza driver MySQL (OfflineBuild).");
#endif

    public static async Task<DbConnection> OpenAsync(string connectionString)
    {
        var cn = Factory.CreateConnection()!;
        cn.ConnectionString = connectionString;
        await cn.OpenAsync();
        // Tutte le date in UTC, qualunque sia il fuso del server MySQL.
        await using (var tz = cn.CreateCommand())
        {
            tz.CommandText = "SET time_zone = '+00:00'";
            await tz.ExecuteNonQueryAsync();
        }
        return cn;
    }

    public Task<DbConnection> OpenAsync() => OpenAsync(_config.Current.ConnectionString);

    public async Task<List<T>> QueryAsync<T>(string sql, object? args, Func<DbDataReader, T> map)
    {
        await using var cn = await OpenAsync();
        await using var cmd = Command(cn, sql, args);
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<T>();
        while (await r.ReadAsync()) list.Add(map(r));
        return list;
    }

    public async Task<T?> FirstAsync<T>(string sql, object? args, Func<DbDataReader, T> map) where T : class
        => (await QueryAsync(sql, args, map)).FirstOrDefault();

    public async Task<int> ExecuteAsync(string sql, object? args = null)
    {
        await using var cn = await OpenAsync();
        await using var cmd = Command(cn, sql, args);
        return await cmd.ExecuteNonQueryAsync();
    }

    public async Task<T?> ScalarAsync<T>(string sql, object? args = null)
    {
        await using var cn = await OpenAsync();
        await using var cmd = Command(cn, sql, args);
        var v = await cmd.ExecuteScalarAsync();
        if (v is null || v is DBNull) return default;
        return (T)Convert.ChangeType(v, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
    }

    /// <summary>Crea un comando con parametri presi dalle proprietà di un oggetto anonimo: new { Id = 5 } diventa @Id.</summary>
    public static DbCommand Command(DbConnection cn, string sql, object? args, DbTransaction? tx = null)
    {
        var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        if (args is null) return cmd;
        foreach (var p in args.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var prm = cmd.CreateParameter();
            prm.ParameterName = "@" + p.Name;
            var value = p.GetValue(args);
            prm.Value = value ?? DBNull.Value;
            if (value is string s) { prm.DbType = DbType.String; prm.Size = s.Length > 4000 ? -1 : 4000; }
            cmd.Parameters.Add(prm);
        }
        return cmd;
    }
}

public static class ReaderExtensions
{
    public static string? Str(this DbDataReader r, string col) { var i = r.GetOrdinal(col); return r.IsDBNull(i) ? null : r.GetString(i); }
    public static int Int(this DbDataReader r, string col) => r.GetInt32(r.GetOrdinal(col));
    public static int? IntN(this DbDataReader r, string col) { var i = r.GetOrdinal(col); return r.IsDBNull(i) ? null : r.GetInt32(i); }
    public static bool Bool(this DbDataReader r, string col) => r.GetBoolean(r.GetOrdinal(col));
    public static DateTime? Date(this DbDataReader r, string col) { var i = r.GetOrdinal(col); return r.IsDBNull(i) ? null : r.GetDateTime(i); }
}
