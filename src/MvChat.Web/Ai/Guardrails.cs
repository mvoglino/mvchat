using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MvChat.Web.Catalog;

namespace MvChat.Web.Ai;

public sealed record AiReply(string Text, string Outcome, string? Note, string? Reason = null, bool AskReason = false);

/// <summary>
/// Controlli fatti da mvchat sulla risposta dell'AI PRIMA di mandarla al cliente.
/// Le istruzioni chiedono all'AI di rispettare le regole; questi controlli verificano che lo abbia fatto davvero.
/// </summary>
public static class Guardrails
{
    public static readonly string OutputFormat =
@"## Formato della tua risposta
Rispondi SOLO con un oggetto JSON, senza altro testo prima o dopo:
{""risposta"": ""il messaggio da inviare al cliente"", ""esito"": ""in_corso | obiettivo_raggiunto | rifiuto | operatore | opt_out"", ""nota"": ""motivo dell'esito in poche parole, per lo staff"", ""motivo"": ""solo con esito rifiuto: uno dei codici qui sotto"", ""chiedi_motivo"": false}
- in_corso: la conversazione continua.
- obiettivo_raggiunto: il cliente ha accettato (vedi quando la conversazione ha successo).
- rifiuto: il cliente ha detto chiaramente di no.
- operatore: serve una persona della reception (regola 7, dubbi che non sai risolvere, cliente che lo chiede).
- opt_out: il cliente non vuole più essere contattato.
- chiedi_motivo: true solo quando, come dice la regola 12, in questa risposta chiedi al cliente il motivo del rifiuto; altrimenti false.
Con esito rifiuto, in ""motivo"" metti il codice che descrive meglio quello che ha detto il cliente (se non lo dice: non_interessato):
" + string.Join("\n", RefusalReasons.All.Select(r => $"- {r.Code}: {r.ForAi}"));


    private static readonly Regex Url = new(@"(?:https?://|www\.)[^\s<>""«»]+", RegexOptions.IgnoreCase);
    private static string NormUrl(string u)
    {
        u = u.Trim().TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '»', '"', '\'').ToLowerInvariant();
        foreach (var p in new[] { "https://", "http://" }) if (u.StartsWith(p)) u = u[p.Length..];
        if (u.StartsWith("www.")) u = u[4..];
        return u.TrimEnd('/');
    }

    // Il cliente chiede una persona: «posso parlare con qualcuno della reception?», «vorrei un operatore», «chiamatemi».
    private static readonly Regex HumanRequest = new(
        @"\b(parlare|parlarne|sentire|chiamare|contattare|passa(mi|temi)|mi\s+passi|mi\s+passate|mettermi\s+in\s+contatto)\b[^.?!\n]{0,40}\b(person[ae]|qualcuno|operator[ei]|operatrice|umano|reception|responsabile|direttor[ei]|direttrice|segreteria|staff|collega|titolare)\b"
        + @"|\b(voglio|vorrei|posso\s+avere|mi\s+serve|chiedo)\b[^.?!\n]{0,25}\b(un[ao']?\s*)?(operator[ei]|operatrice|persona\s+(vera|reale)|essere\s+umano|umano)\b"
        + @"|\b(richiamatemi|chiamatemi|mi\s+(ri)?chiam(i|ate)|potete\s+(ri)?chiamarmi|puoi\s+(ri)?chiamarmi)\b",
        RegexOptions.IgnoreCase);

    /// <summary>
    /// Il cliente ha chiesto esplicitamente di parlare con una persona. Se l'AI non passa la conversazione alla reception
    /// (a volte risponde con gli orari), lo fa mvchat: la regola non dipende da quanto è bravo il modello.
    /// </summary>
    public static bool WantsHuman(string? text) => !string.IsNullOrWhiteSpace(text) && HumanRequest.IsMatch(text);

    /// <summary>
    /// L'assistente può mandare solo i link scritti nelle sue istruzioni (offerta, prenotazione, informativa privacy, sito):
    /// un link inventato o modificato non parte e la conversazione passa a una persona.
    /// </summary>
    public static string? LinkProblem(string text, string instructions)
    {
        var allowed = Url.Matches(instructions).Select(m => NormUrl(m.Value)).ToHashSet();
        foreach (Match m in Url.Matches(text))
            if (!allowed.Contains(NormUrl(m.Value)))
                return $"l'assistente voleva mandare un link non previsto ({(m.Value.Length > 80 ? m.Value[..80] + "…" : m.Value)}): controlla e rispondi tu";
        return null;
    }

    /// <summary>
    /// Legge la risposta dell'AI. Accetta anche JSON dentro ```json … ``` o con del testo prima e dopo:
    /// prova uno per uno gli oggetti {…} completi e usa il primo che ha la «risposta». Se non ce n'è nessuno restituisce null.
    /// </summary>
    public static AiReply? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        foreach (var candidate in JsonObjects(raw))
            if (ParseOne(candidate) is { } reply) return reply;
        return null;
    }

    /// <summary>Gli oggetti {…} bilanciati nel testo (le graffe dentro le stringhe non contano).</summary>
    private static IEnumerable<string> JsonObjects(string s)
    {
        for (var start = s.IndexOf('{'); start >= 0; start = s.IndexOf('{', start + 1))
        {
            int depth = 0; var inString = false; var escape = false;
            for (var i = start; i < s.Length; i++)
            {
                var ch = s[i];
                if (inString)
                {
                    if (escape) escape = false;
                    else if (ch == '\\') escape = true;
                    else if (ch == '"') inString = false;
                    continue;
                }
                if (ch == '"') inString = true;
                else if (ch == '{') depth++;
                else if (ch == '}' && --depth == 0) { yield return s[start..(i + 1)]; break; }
            }
        }
    }

    private static AiReply? ParseOne(string json)
    {
        try
        {
            var j = JsonNode.Parse(json);
            string? Str(string key) { try { return j?[key]?.ToString(); } catch { return null; } }
            var text = Str("risposta")?.Trim();
            var esito = Str("esito")?.Trim().ToLowerInvariant() ?? Outcomes.InCorso;
            var nota = Str("nota")?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (!new[] { Outcomes.InCorso, Outcomes.Raggiunto, Outcomes.Rifiuto, Outcomes.Operatore, Outcomes.OptOut }.Contains(esito)) esito = Outcomes.InCorso;
            var motivo = Str("motivo")?.Trim().ToLowerInvariant();
            var reason = esito == Outcomes.Rifiuto ? (RefusalReasons.IsValid(motivo) ? motivo : RefusalReasons.Other) : null;
            var ask = esito == Outcomes.Rifiuto && string.Equals(Str("chiedi_motivo"), "true", StringComparison.OrdinalIgnoreCase);
            if (text.Length > 1000) text = text[..(char.IsHighSurrogate(text[999]) ? 999 : 1000)]; // senza spezzare un'emoji
            return new AiReply(text, esito, string.IsNullOrWhiteSpace(nota) ? null : (nota.Length > 480 ? nota[..480] : nota), reason, ask);
        }
        catch { return null; }
    }

    /// <summary>Messaggio per chiedere all'AI di rifare la risposta nel formato giusto (un solo tentativo).</summary>
    public const string FormatRetry = "(Nota del sistema, non del cliente) La tua risposta precedente non era nel formato richiesto. Riscrivila ora SOLO come oggetto JSON, come indicato nelle istruzioni, senza altro testo.";

    // Importi in euro scritti in tutti i modi comuni: «€ 49», «49€», «49,90 euro», «euro 299», «EUR 1.299,00», «€1299».
    private const string Num = @"\d{1,3}(?:[.'\u00A0\u202F]\d{3})+(?:,\d{1,2})?|\d+(?:[.,]\d{1,2})?";
    private static readonly Regex Money = new(
        $@"(?:(?:€|\beuro\b|\beur\b)\s*(?<a>{Num}))|(?:(?<b>{Num})\s*(?:€|euro\b|eur\b))", RegexOptions.IgnoreCase);
    // Sconti in percentuale: l'assistente può citare solo lo sconto dell'offerta (o meno).
    // Solo quando si parla di sconto: «100% soddisfatti» non è uno sconto.
    private static readonly Regex Percent = new(
        @"sconto\s+(?:del\s+|di\s+|pari\s+al\s+)?(?<p>\d{1,3}(?:[.,]\d{1,2})?)\s*(?:%|per\s*cento)|(?<p>\d{1,3}(?:[.,]\d{1,2})?)\s*(?:%|per\s*cento)\s+(?:di\s+)?sconto|-\s?(?<p>\d{1,3}(?:[.,]\d{1,2})?)\s*%",
        RegexOptions.IgnoreCase);

    private static readonly Regex SavingBefore = new(@"(risparmi\w*|sconto|riduzione|in\s+meno|meno\s+di|ti\s+togli\w*|abbuono)\W{0,3}(\w+\W+){0,3}$", RegexOptions.IgnoreCase);
    private static readonly Regex SavingAfter = new(@"^\W{0,3}(di\s+)?(risparmio|sconto|in\s+meno|di\s+riduzione)", RegexOptions.IgnoreCase);

    /// <summary>Gli importi che nel testo sono un risparmio o uno sconto, non un prezzo da pagare.</summary>
    public static HashSet<decimal> SavingAmounts(string text)
    {
        var set = new HashSet<decimal>();
        foreach (Match m in Money.Matches(text))
        {
            var before = text[Math.Max(0, m.Index - 40)..m.Index];
            var after = text[(m.Index + m.Length)..Math.Min(text.Length, m.Index + m.Length + 25)];
            if ((SavingBefore.IsMatch(before) || SavingAfter.IsMatch(after)) && Amounts(m.Value) is [var v]) set.Add(v);
        }
        return set;
    }

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
        // Un risparmio («risparmi 100 €», «100 € di sconto») può arrivare al massimo a: prezzo pieno − prezzo + sconto extra concesso.
        var maxSaving = (offer.FullPrice is decimal full && full > price ? full - price : 0m) + price * offer.MaxExtraDiscountPct / 100m + 0.5m;
        var savings = SavingAmounts(text);
        foreach (var a in amounts)
        {
            var ok = Math.Abs(a - price) < 0.01m || (offer.FullPrice is decimal f && Math.Abs(a - f) < 0.01m) || (a >= min && a <= price)
                     || (savings.Contains(a) && a <= maxSaving);
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
