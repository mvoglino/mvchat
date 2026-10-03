using System.Globalization;
using System.Text.RegularExpressions;

namespace MvChat.Web.Infrastructure;

/// <summary>Importi scritti all'italiana: «39», «39,90», «39.90», «1.200», «1.234,50», «€ 49».</summary>
public static class Money
{
    public static decimal? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Replace("€", "").Replace(" ", "").Replace(" ", "").Trim();
        if (s.Contains(',')) s = s.Replace(".", "").Replace(',', '.');
        else if (Regex.IsMatch(s, @"^\d{1,3}(\.\d{3})+$")) s = s.Replace(".", ""); // 1.200 = milleduecento
        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
