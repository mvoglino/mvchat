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
}
