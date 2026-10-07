using System.Text.RegularExpressions;

namespace MvChat.Web.WhatsApp;

/// <summary>
/// I template si scrivono con i segnaposto di mvchat ({{nome}}, {{scadenza}}, …).
/// Meta invece vuole {{1}}, {{2}}, … e un esempio per ciascuno: qui avviene la traduzione.
/// </summary>
public static class TemplateText
{
    public static readonly IReadOnlyDictionary<string, string> Placeholders = new Dictionary<string, string>
    {
        ["nome"] = "Giulia",
        ["cognome"] = "Rossi",
        ["abbonamento"] = "Annuale",
        ["scadenza"] = "18/10/2026",
        ["sede"] = "FitActive Alba",
        ["palestra"] = "FitActive Alba", // nome storico di {{sede}}: resta valido per i template già approvati
        ["offerta"] = "Rinnovo con 2 mesi omaggio",
        ["servizio"] = "Pilates",
        ["corso"] = "Pilates", // sinonimo di {{servizio}}
        ["note"] = "ti aspettiamo la sera",
    };

    private static readonly Regex Var = new(@"\{\{\s*([a-zA-Zàèéìòù_]+|\d+)\s*\}\}");

    /// <summary>Segnaposto nell'ordine in cui compaiono la prima volta.</summary>
    public static List<string> Variables(string body) =>
        Var.Matches(body).Select(m => m.Groups[1].Value.ToLowerInvariant()).Distinct().ToList();

    /// <summary>Errori che farebbero rifiutare il template da Meta o che mvchat non saprebbe riempire.</summary>
    public static List<string> Problems(string body)
    {
        var errors = new List<string>();
        var t = body.Trim();
        if (t.Length == 0) { errors.Add("Il testo è vuoto."); return errors; }
        if (t.Length > 1024) errors.Add("Il testo supera i 1.024 caratteri ammessi da Meta.");
        foreach (var v in Variables(t))
            if (!Placeholders.ContainsKey(v)) errors.Add($"Segnaposto sconosciuto: {{{{{v}}}}}. Usa: {string.Join(", ", Placeholders.Keys.Where(k => k is not ("palestra" or "corso")).Select(k => "{{" + k + "}}"))}.");
        if (Var.Match(t) is { Success: true } first && first.Index == 0) errors.Add("Il testo non può iniziare con un segnaposto (regola di Meta).");
        if (Var.Matches(t).LastOrDefault() is { } last && last.Index + last.Length == t.Length) errors.Add("Il testo non può finire con un segnaposto: aggiungi la punteggiatura o una frase (regola di Meta).");
        if (Regex.IsMatch(t, @"\n{3,}")) errors.Add("Troppe righe vuote di fila.");
        return errors;
    }

    /// <summary>"Ciao {{nome}}, scade il {{scadenza}}" → "Ciao {{1}}, scade il {{2}}".</summary>
    public static string ToMeta(string body, IList<string> variables) =>
        Var.Replace(body.Trim(), m => "{{" + (variables.IndexOf(m.Groups[1].Value.ToLowerInvariant()) + 1) + "}}");

    /// <summary>{{sede}} e {{palestra}} sono la stessa cosa: chi riempie i valori può usare l'uno o l'altro nome.</summary>
    private static readonly Dictionary<string, string> Aliases = new() { ["sede"] = "palestra", ["palestra"] = "sede", ["corso"] = "servizio", ["servizio"] = "corso" };

    public static bool TryValue(IDictionary<string, string?> values, string key, out string? value)
    {
        if (values.TryGetValue(key, out value)) return true;
        return Aliases.TryGetValue(key, out var alias) && values.TryGetValue(alias, out value);
    }

    public static string Fill(string body, IDictionary<string, string?> values) =>
        Var.Replace(body, m => TryValue(values, m.Groups[1].Value.ToLowerInvariant(), out var v) ? v ?? "" : m.Value);

    /// <summary>Nome tecnico per Meta: minuscole, numeri e trattino basso.</summary>
    public static string MetaName(string s)
    {
        var n = Regex.Replace(s.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD), @"\p{Mn}", "");
        n = Regex.Replace(n, "[^a-z0-9]+", "_").Trim('_');
        return n.Length > 60 ? n[..60] : n;
    }
}
