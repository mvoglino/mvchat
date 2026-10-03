using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MvChat.Web.Catalog;

namespace MvChat.Web.Ai;

public sealed record AiReply(string Text, string Outcome, string? Note);

/// <summary>
/// Controlli fatti da mvchat sulla risposta dell'AI PRIMA di mandarla al cliente.
/// Le istruzioni chiedono all'AI di rispettare le regole; questi controlli verificano che lo abbia fatto davvero.
/// </summary>
public static class Guardrails
{
    public const string OutputFormat =
@"## Formato della tua risposta
Rispondi SOLO con un oggetto JSON, senza altro testo prima o dopo:
{""risposta"": ""il messaggio da inviare al cliente"", ""esito"": ""in_corso | obiettivo_raggiunto | rifiuto | operatore | opt_out"", ""nota"": ""motivo dell'esito in poche parole, per lo staff""}
- in_corso: la conversazione continua.
- obiettivo_raggiunto: il cliente ha accettato (vedi quando la conversazione ha successo).
- rifiuto: il cliente ha detto chiaramente di no.
- operatore: serve una persona della reception (regola 7, dubbi che non sai risolvere, cliente che lo chiede).
- opt_out: il cliente non vuole più essere contattato.";

    private static readonly Regex Json = new(@"\{[\s\S]*\}");

    /// <summary>Legge la risposta dell'AI. Se non è nel formato richiesto restituisce null: in quel caso non si manda nulla.</summary>
    public static AiReply? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var m = Json.Match(raw);
        if (!m.Success) return null;
        try
        {
            var j = JsonNode.Parse(m.Value);
            var text = j?["risposta"]?.GetValue<string>()?.Trim();
            var esito = j?["esito"]?.GetValue<string>()?.Trim().ToLowerInvariant() ?? Outcomes.InCorso;
            var nota = j?["nota"]?.GetValue<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (!new[] { Outcomes.InCorso, Outcomes.Raggiunto, Outcomes.Rifiuto, Outcomes.Operatore, Outcomes.OptOut }.Contains(esito)) esito = Outcomes.InCorso;
            if (text.Length > 1000) text = text[..(char.IsHighSurrogate(text[999]) ? 999 : 1000)]; // senza spezzare un'emoji
            return new AiReply(text, esito, string.IsNullOrWhiteSpace(nota) ? null : (nota.Length > 480 ? nota[..480] : nota));
        }
        catch { return null; }
    }

    // Importi in euro scritti in tutti i modi comuni: «€ 49», «49€», «49,90 euro», «euro 299», «EUR 1.299,00», «€1299».
    private const string Num = @"\d{1,3}(?:[.'\u00A0\u202F]\d{3})+(?:,\d{1,2})?|\d+(?:[.,]\d{1,2})?";
    private static readonly Regex Money = new(
        $@"(?:(?:€|\beuro\b|\beur\b)\s*(?<a>{Num}))|(?:(?<b>{Num})\s*(?:€|euro\b|eur\b))", RegexOptions.IgnoreCase);
    // Sconti in percentuale: l'assistente può citare solo lo sconto dell'offerta (o meno).
    // Solo quando si parla di sconto: «100% soddisfatti» non è uno sconto.
    private static readonly Regex Percent = new(
        @"sconto\s+(?:del\s+|di\s+|pari\s+al\s+)?(?<p>\d{1,3}(?:[.,]\d{1,2})?)\s*(?:%|per\s*cento)|(?<p>\d{1,3}(?:[.,]\d{1,2})?)\s*(?:%|per\s*cento)\s+(?:di\s+)?sconto|-\s?(?<p>\d{1,3}(?:[.,]\d{1,2})?)\s*%",
        RegexOptions.IgnoreCase);

    public static List<decimal> Amounts(string text)
    {
        var list = new List<decimal>();
        foreach (Match m in Money.Matches(text))
        {
            var s = (m.Groups["a"].Success ? m.Groups["a"].Value : m.Groups["b"].Value);
            s = Regex.Replace(s, @"[\s'\u00A0\u202F]", "");
            if (s.Contains(',')) s = s.Replace(".", "").Replace(',', '.');
            else if (Regex.IsMatch(s, @"^\d{1,3}(\.\d{3})+$")) s = s.Replace(".", "");
            if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)) list.Add(v);
        }
        return list;
    }

    /// <summary>
    /// Un importo è ammesso solo se è il prezzo dell'offerta, il prezzo pieno, oppure un prezzo scontato
    /// entro lo sconto extra concesso. Qualsiasi altro importo è un prezzo inventato.
    /// </summary>
    public static string? PriceProblem(string text, Offer? offer)
    {
        var amounts = Amounts(text);
        if (amounts.Count == 0) return null;
        if (offer?.Price is not decimal price) return $"l'assistente ha citato un importo ({amounts[0]:0.##} €) senza un'offerta con prezzo";
        var min = price * (1 - offer.MaxExtraDiscountPct / 100m) - 0.5m;
        foreach (var a in amounts)
        {
            var ok = Math.Abs(a - price) < 0.01m || (offer.FullPrice is decimal f && Math.Abs(a - f) < 0.01m) || (a >= min && a <= price);
            if (!ok) return $"l'assistente ha proposto {a:0.##} €, fuori da prezzo e sconto consentiti";
        }
        return null;
    }

    /// <summary>Uno sconto in percentuale è ammesso solo fino allo sconto extra concesso dall'offerta (più lo sconto già compreso nel prezzo).</summary>
    public static string? PercentProblem(string text, Offer? offer)
    {
        foreach (Match m in Percent.Matches(text))
        {
            if (!decimal.TryParse(m.Groups["p"].Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var pct)) continue;
            var allowed = offer?.MaxExtraDiscountPct ?? 0m;
            if (offer?.Price is decimal p && offer.FullPrice is decimal f && f > 0 && p < f)
                allowed = Math.Max(allowed, Math.Round((1 - p / f) * 100m, 0, MidpointRounding.AwayFromZero) + allowed);
            if (pct > allowed + 0.5m) return $"l'assistente ha promesso uno sconto del {pct:0.##}%, oltre quello consentito";
        }
        return null;
    }

    private static readonly Regex Disclosed = new(@"assistente\s+(virtuale|digitale|automatico)|intelligenza\s+artificiale|\bchatbot\b|\bbot\b|sistema\s+automatico|risponditore\s+automatico", RegexOptions.IgnoreCase);

    /// <summary>
    /// Prima risposta: il cliente deve sapere che scrive con un assistente virtuale (AI Act).
    /// Non basta il nome dell'assistente («Sara»): serve una frase che dica chiaramente che è automatico. Se manca, la aggiunge mvchat.
    /// </summary>
    public static string EnsureDisclosure(string text, bool firstReply, string assistantName, string gymName)
    {
        if (!firstReply || Disclosed.IsMatch(text)) return text;
        var where = string.IsNullOrWhiteSpace(gymName) ? "" : " di " + gymName;
        return $"Ciao, sono {Catalog.PromptBuilder.Intro(assistantName)}{where}. {text}";
    }

    public const string HoldingMessage = "Grazie per il messaggio! Ti faccio ricontattare al più presto da un collega della reception.";
}
