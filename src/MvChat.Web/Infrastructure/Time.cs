namespace MvChat.Web.Infrastructure;

/// <summary>
/// Nel database tutte le date e ore sono in UTC (la connessione imposta time_zone='+00:00').
/// Per mostrarle si convertono nell'ora italiana, indipendentemente da dove gira il server.
/// </summary>
public static class Time
{
    private static readonly TimeZoneInfo Rome = Find("Europe/Rome") ?? Find("W. Europe Standard Time") ?? TimeZoneInfo.Utc;

    private static TimeZoneInfo? Find(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { return null; }
    }

    public static DateTime ToRome(this DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Rome);

    /// <summary>Un orario scritto in pagina (ora italiana) convertito in UTC per il database.</summary>
    public static DateTime FromRome(this DateTime local)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        // L'ora che "non esiste" quando a fine marzo si spostano le lancette (02:00-03:00) vale come l'ora dopo.
        if (Rome.IsInvalidTime(local)) local = local.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, Rome);
    }
}
