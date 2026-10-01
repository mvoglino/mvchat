using System.Globalization;

namespace MvChat.Web.Contacts;

/// <summary>
/// Regole con cui una riga dell'Excel diventa (o non diventa) un contatto da raggiungere su WhatsApp.
/// Sono separate dal resto per poterle provare da sole e cambiarle in un punto solo.
/// </summary>
public static class ImportRules
{
    public const string NoConsent = "no_consent";
    public const string BadPhone = "bad_phone";
    public const string Landline = "landline";
    public const string Duplicate = "duplicate";
    public const string OptedOut = "opted_out";
    public const string NoName = "no_name";

    public static string ReasonLabel(string code) => code switch
    {
        NoConsent => "Manca il consenso",
        BadPhone => "Numero non valido",
        Landline => "Numero fisso (WhatsApp richiede un cellulare)",
        Duplicate => "Numero ripetuto nel file",
        OptedOut => "Ha chiesto di non essere contattato",
        NoName => "Manca il nome",
        _ => code
    };

    private static readonly HashSet<string> YesValues = new(StringComparer.OrdinalIgnoreCase)
    { "si", "sì", "s", "yes", "y", "1", "x", "true", "vero", "ok", "accettato", "acconsento" };

    public static bool IsYes(string? value) => value is not null && YesValues.Contains(value.Trim().TrimEnd('.'));

    /// <summary>
    /// Porta un numero nel formato internazionale (+393331234567).
    /// Accetta spazi, trattini, punti, parentesi, prefisso +39 / 0039 / 39 o nessun prefisso.
    /// I numeri italiani devono essere cellulari (iniziano per 3): i fissi non hanno WhatsApp.
    /// </summary>
    public static (string? Phone, string? Reason) NormalizePhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, BadPhone);
        var t = raw.Trim();
        var international = t.StartsWith('+') || t.StartsWith("00");
        var d = new string(t.Where(char.IsDigit).ToArray());
        if (t.StartsWith("00")) d = d[2..];

        string national;
        if (international)
        {
            if (!d.StartsWith("39"))
                return d.Length is >= 8 and <= 15 ? ("+" + d, null) : (null, BadPhone);
            national = d[2..];
        }
        else
        {
            national = d.StartsWith("39") && d.Length is 11 or 12 ? d[2..] : d;
        }

        if (national.StartsWith('3') && national.Length is 9 or 10) return ("+39" + national, null);
        if (national.StartsWith('0') && national.Length is >= 6 and <= 11) return (null, Landline);
        return (null, BadPhone);
    }

    private static readonly string[] DateFormats =
    {
        "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "d/M/yy", "dd-MM-yyyy", "d-M-yyyy", "dd.MM.yyyy", "d.M.yyyy",
        "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss", "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss"
    };

    /// <summary>Le date dell'Excel arrivano già in formato ISO dal browser; quelle scritte a mano in formato italiano.</summary>
    public static DateTime? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        if (DateTime.TryParseExact(s, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            && d.Year is > 1900 and < 2200) return d.Date;
        return null;
    }

    public static string? Clean(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim();
        return t.Length > max ? t[..max] : t;
    }

    /// <summary>"ROSSI" → "Rossi", "de luca" → "De Luca": i messaggi iniziano con il nome, meglio che sia scritto bene.</summary>
    public static string? NiceName(string? s, int max)
    {
        var t = Clean(s, max);
        if (t is null) return null;
        if (t.Any(char.IsLower) && t.Any(char.IsUpper)) return t;
        return CultureInfo.GetCultureInfo("it-IT").TextInfo.ToTitleCase(t.ToLowerInvariant());
    }
}
