using System.Globalization;
using System.Text;

namespace MvChat.Web.Infrastructure;

/// <summary>
/// Lingue dei clienti. Nella lista si può scrivere il codice (it, en…) o il nome in qualsiasi forma comune:
/// mvchat lo trasforma nel codice usato da Meta per i template. Lingua vuota = italiano.
/// </summary>
public static class Languages
{
    public const string Default = "it";

    public static readonly (string Code, string Label)[] Supported =
    {
        ("it", "Italiano"), ("en", "Inglese"), ("de", "Tedesco"), ("fr", "Francese"), ("es", "Spagnolo")
    };

    private static readonly Dictionary<string, string> Names = new()
    {
        ["it"] = "it", ["ita"] = "it", ["italiano"] = "it", ["italian"] = "it", ["italia"] = "it", ["it it"] = "it",
        ["en"] = "en", ["eng"] = "en", ["inglese"] = "en", ["english"] = "en", ["en gb"] = "en", ["en us"] = "en", ["uk"] = "en", ["gb"] = "en",
        ["de"] = "de", ["deu"] = "de", ["ger"] = "de", ["tedesco"] = "de", ["deutsch"] = "de", ["german"] = "de", ["de de"] = "de", ["de ch"] = "de",
        ["fr"] = "fr", ["fra"] = "fr", ["francese"] = "fr", ["francais"] = "fr", ["french"] = "fr", ["fr fr"] = "fr",
        ["es"] = "es", ["spa"] = "es", ["esp"] = "es", ["spagnolo"] = "es", ["espanol"] = "es", ["spanish"] = "es", ["es es"] = "es",
    };

    /// <summary>"English", "EN", "inglese" → "en". Sconosciuta o vuota → null (vale l'italiano).</summary>
    public static string? Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var n = new string(s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        n = string.Join(' ', n.Split(new[] { ' ', '-', '_', '.', '/' }, StringSplitOptions.RemoveEmptyEntries));
        return Names.TryGetValue(n, out var code) ? code : null;
    }

    public static string Label(string? code) => Supported.FirstOrDefault(x => x.Code == (code ?? Default)).Label ?? "Italiano";

    /// <summary>Nome della lingua per le istruzioni dell'assistente.</summary>
    public static string PromptName(string? code) => (code ?? Default) switch
    {
        "en" => "inglese", "de" => "tedesco", "fr" => "francese", "es" => "spagnolo", _ => "italiano"
    };
}
