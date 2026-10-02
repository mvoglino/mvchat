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
            if (text.Length > 1000) text = text[..1000];
            return new AiReply(text, esito, string.IsNullOrWhiteSpace(nota) ? null : (nota.Length > 480 ? nota[..480] : nota));
        }
        catch { return null; }
    }

    private static readonly Regex Money = new(@"(?:€\s*(?<a>\d{1,3}(?:[.\s]\d{3})*(?:,\d{1,2})?|\d+(?:[.,]\d{1,2})?))|(?:(?<b>\d{1,3}(?:[.\s]\d{3})*(?:,\d{1,2})?|\d+(?:[.,]\d{1,2})?)\s*(?:€|euro\b|eur\b))", RegexOptions.IgnoreCase);

    public static List<decimal> Amounts(string text)
    {
        var list = new List<decimal>();
        foreach (Match m in Money.Matches(text))
        {
            var s = (m.Groups["a"].Success ? m.Groups["a"].Value : m.Groups["b"].Value).Replace(" ", "");
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

    /// <summary>Prima risposta: il cliente deve sapere che scrive con un assistente virtuale (AI Act). Se l'AI se ne dimentica, lo aggiunge mvchat.</summary>
    public static string EnsureDisclosure(string text, bool firstReply, string assistantName, string gymName)
    {
        if (!firstReply) return text;
        var t = text.ToLowerInvariant();
        if (t.Contains(assistantName.ToLowerInvariant()) || t.Contains("assistente") || t.Contains("virtuale")) return text;
        return $"Ciao, sono l'{assistantName} di {gymName}. {text}";
    }

    public const string HoldingMessage = "Grazie per il messaggio! Ti faccio ricontattare al più presto da un collega della reception.";
}
