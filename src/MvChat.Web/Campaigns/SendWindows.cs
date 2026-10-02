using MvChat.Web.Infrastructure;

namespace MvChat.Web.Campaigns;

/// <summary>Una fascia oraria di invio: giorno (1 = lunedì … 7 = domenica) e minuti dalla mezzanotte, ora italiana.</summary>
public sealed record SendWindow(int Day, int Start, int End);

/// <summary>
/// Quando una palestra permette l'invio dei primi messaggi di una campagna.
/// Nessuna fascia = sempre (24 ore su 24). Le risposte ai clienti non sono mai bloccate dagli orari.
/// </summary>
public static class SendWindows
{
    public static readonly string[] DayNames = { "", "Lunedì", "Martedì", "Mercoledì", "Giovedì", "Venerdì", "Sabato", "Domenica" };

    public static int Day(DateTime rome) => rome.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)rome.DayOfWeek;

    public static bool IsOpen(IReadOnlyList<SendWindow> windows, DateTime utcNow)
    {
        if (windows.Count == 0) return true;
        var rome = utcNow.ToRome();
        var day = Day(rome);
        var minute = rome.Hour * 60 + rome.Minute;
        return windows.Any(w => w.Day == day && minute >= w.Start && minute < w.End);
    }

    /// <summary>Il prossimo momento (ora italiana) in cui l'invio è consentito, entro una settimana.</summary>
    public static DateTime? NextOpenRome(IReadOnlyList<SendWindow> windows, DateTime utcNow)
    {
        if (windows.Count == 0) return utcNow.ToRome();
        var rome = utcNow.ToRome();
        var now = rome.Hour * 60 + rome.Minute;
        for (var d = 0; d <= 7; d++)
        {
            var date = rome.Date.AddDays(d);
            var day = Day(date);
            foreach (var w in windows.Where(w => w.Day == day).OrderBy(w => w.Start))
            {
                if (d == 0 && now >= w.End) continue;
                var start = d == 0 ? Math.Max(w.Start, now) : w.Start;
                return date.AddMinutes(start);
            }
        }
        return null;
    }

    public static string Hm(int minutes) => minutes >= 1440 ? "24:00" : $"{minutes / 60:00}:{minutes % 60:00}";

    public static int? ParseHm(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var p = s.Trim().Replace('.', ':').Split(':');
        if (!int.TryParse(p[0], out var h)) return null;
        var m = p.Length > 1 && int.TryParse(p[1], out var mm) ? mm : 0;
        if (h < 0 || h > 24 || m < 0 || m > 59 || (h == 24 && m > 0)) return null;
        return h * 60 + m;
    }

    /// <summary>Descrizione breve per le pagine, es. «Lun–Ven 09:00–20:00 · Sab 09:00–13:00».</summary>
    public static string Describe(IReadOnlyList<SendWindow> windows)
    {
        if (windows.Count == 0) return "sempre, 24 ore su 24";
        var parts = windows.GroupBy(w => w.Day).OrderBy(g => g.Key)
            .Select(g => (Day: g.Key, Text: string.Join(", ", g.OrderBy(w => w.Start).Select(w => $"{Hm(w.Start)}–{Hm(w.End)}")))).ToList();
        var res = new List<string>();
        for (var i = 0; i < parts.Count;)
        {
            var j = i;
            while (j + 1 < parts.Count && parts[j + 1].Text == parts[i].Text && parts[j + 1].Day == parts[j].Day + 1) j++;
            var name = (int d) => DayNames[d][..3];
            res.Add((j > i ? $"{name(parts[i].Day)}–{name(parts[j].Day)}" : name(parts[i].Day)) + " " + parts[i].Text);
            i = j + 1;
        }
        return string.Join(" · ", res);
    }

    /// <summary>
    /// Limite di Meta: quante persone diverse si possono contattare per primi in 24 ore.
    /// Numeri simulati senza limite; se Meta non l'ha ancora comunicato si parte prudenti (250).
    /// </summary>
    public static int? MetaDailyLimit(string? tier, bool simulated)
    {
        if (simulated) return null;
        var t = (tier ?? "").ToUpperInvariant();
        if (t.Contains("UNLIMITED")) return null;
        var m = System.Text.RegularExpressions.Regex.Match(t, @"(\d+)\s*(K)?");
        if (!m.Success) return 250;
        var n = int.Parse(m.Groups[1].Value) * (m.Groups[2].Success ? 1000 : 1);
        return n > 0 ? n : 250;
    }
}
