using System.Globalization;
using System.Text;
using MvChat.Web.Infrastructure;

namespace MvChat.Web.Catalog;

/// <summary>I dati dell'attività (gruppo) che l'assistente può usare: tipo di attività e presentazione.</summary>
public sealed record OrgInfo(int Id, string Name, string SectorKey, string? Description, string? Website, string? Phone,
    string? GroupName = null, string? GroupDescription = null, string? PrivacyUrl = null)
{
    public Sector Sector => Sectors.Get(SectorKey);
}

/// <summary>Dati del destinatario usati nei messaggi. Nell'anteprima sono di esempio.</summary>
public sealed record Recipient(string FirstName, string? LastName, string? Membership, DateTime? ExpiresOn, string? Service = null, string? Notes = null);

/// <summary>
/// Compone le istruzioni che l'assistente AI riceve per una conversazione:
/// regole fisse di mvchat + modello di obiettivo + scheda attività + offerta + dati del cliente.
/// L'attività non scrive prompt: compila moduli, e questa classe li traduce.
/// </summary>
public static class PromptBuilder
{
    private static readonly CultureInfo It = CultureInfo.GetCultureInfo("it-IT");

    public static string Money(decimal v) => v.ToString(v % 1 == 0 ? "#,0" : "#,0.00", It) + " €";
    public static string Day(DateTime d) => d.ToString("d MMMM yyyy", It);

    /// <summary>Riempie i segnaposto del primo messaggio: {{nome}}, {{abbonamento}}, {{scadenza}}, {{sede}} (o {{palestra}}), {{offerta}}.</summary>
    public static string FillTemplate(string? template, Recipient r, string gymName, Offer? offer) =>
        (template ?? "")
            .Replace("{{nome}}", r.FirstName)
            .Replace("{{abbonamento}}", r.Membership ?? "")
            .Replace("{{scadenza}}", r.ExpiresOn?.ToString("dd/MM/yyyy") ?? "")
            .Replace("{{sede}}", gymName)
            .Replace("{{palestra}}", gymName)
            .Replace("{{offerta}}", offer?.Title ?? "")
            .Replace("{{servizio}}", r.Service ?? "")
            .Replace("{{corso}}", r.Service ?? "")
            .Replace("{{note}}", r.Notes ?? "");

    /// <summary>Come si presenta l'assistente: «l'assistente virtuale» oppure «Sara, l'assistente virtuale».</summary>
    public static string Intro(string? assistantName)
    {
        var n = string.IsNullOrWhiteSpace(assistantName) ? "assistente virtuale" : assistantName.Trim();
        return n.Contains("assistente", StringComparison.OrdinalIgnoreCase) ? "l'" + n : n + ", l'assistente virtuale";
    }

    /// <param name="campaignExtra">Istruzioni in più scritte per la singola campagna (facoltative).</param>
    public static string Build(GoalModel model, string gymName, GymProfile p, Offer? offer, Recipient r, DateTime today, OrgInfo? org = null, string? campaignExtra = null, bool askReason = true, bool reasonAsked = false)
    {
        var lei = p.Formality == "lei";
        var sector = org?.Sector ?? Sectors.Get("palestra");
        var membership = sector.Membership;
        var sb = new StringBuilder();
        sb.AppendLine($"Sei {Intro(p.AssistantName)} di {gymName} ({sector.Label.ToLowerInvariant()}) e scrivi su WhatsApp a un {sector.Customer}.");
        sb.AppendLine($"Oggi è {Day(today)}. Scrivi in italiano, {(lei ? "dando del lei" : "dando del tu")}, con tono cordiale e diretto, come una persona dello staff.");
        sb.AppendLine();

        if (org is not null && (!string.IsNullOrWhiteSpace(org.Description) || !string.IsNullOrWhiteSpace(org.GroupDescription)
                                || !string.IsNullOrWhiteSpace(org.Website) || !string.IsNullOrWhiteSpace(org.Phone)))
        {
            sb.AppendLine($"## Chi siamo: {org.Name}");
            if (!string.IsNullOrWhiteSpace(org.Description)) sb.AppendLine(org.Description.Trim());
            if (org.GroupName is not null) sb.AppendLine($"Fa parte del gruppo {org.GroupName}." + (string.IsNullOrWhiteSpace(org.GroupDescription) ? "" : " " + org.GroupDescription.Trim()));
            if (!string.IsNullOrWhiteSpace(org.Website)) sb.AppendLine($"Sito: {org.Website.Trim()}");
            if (!string.IsNullOrWhiteSpace(org.Phone)) sb.AppendLine($"Telefono: {org.Phone.Trim()}");
            sb.AppendLine();
        }

        sb.AppendLine($"## Obiettivo: {model.Name}");
        sb.AppendLine($"La conversazione ha successo quando: {model.Success}");
        sb.AppendLine(model.Instructions.Trim());
        sb.AppendLine();

        sb.AppendLine("## Offerta");
        if (offer is null)
            sb.AppendLine("Nessuna offerta collegata. Non proporre prezzi, sconti o promozioni.");
        else
        {
            sb.AppendLine($"Titolo: {offer.Title}");
            if (!string.IsNullOrWhiteSpace(offer.Description)) sb.AppendLine($"Cosa comprende: {offer.Description.Trim()}");
            if (offer.Price is { } price)
            {
                var line = $"Prezzo: {Money(price)}{(string.IsNullOrWhiteSpace(offer.PriceNote) ? "" : " " + offer.PriceNote!.Trim())}";
                if (offer.FullPrice is { } full && full > price) line += $" invece di {Money(full)}";
                sb.AppendLine(line);
            }
            if (!string.IsNullOrWhiteSpace(offer.Conditions)) sb.AppendLine($"Condizioni: {offer.Conditions.Trim()}");
            if (offer.ValidTo is { } to) sb.AppendLine($"Valida fino al {Day(to)} compreso.");
            sb.AppendLine(offer.MaxExtraDiscountPct > 0
                ? $"Sconto extra concedibile: al massimo il {offer.MaxExtraDiscountPct}% sul prezzo dell'offerta, solo se il cliente esita per il prezzo. Non proporlo subito."
                : "Sconto extra: nessuno. Non concedere riduzioni oltre il prezzo dell'offerta.");
            if (!string.IsNullOrWhiteSpace(offer.ActionUrl)) sb.AppendLine($"Link per aderire e pagare l'offerta: {offer.ActionUrl} (mandalo quando il cliente dice di voler aderire o chiede come fare, non prima).");
            if (!string.IsNullOrWhiteSpace(offer.BookUrl)) sb.AppendLine($"Link per prenotare legato all'offerta: {offer.BookUrl} (mandalo quando il cliente vuole fissare un giorno o un orario).");
            if (string.IsNullOrWhiteSpace(offer.ActionUrl) && string.IsNullOrWhiteSpace(offer.BookUrl)) sb.AppendLine($"Per aderire il cliente passa da {gymName}: proponi di fissare quando.");
        }
        sb.AppendLine();

        sb.AppendLine($"## Scheda di {gymName}");
        void Line(string label, string? value) { if (!string.IsNullOrWhiteSpace(value)) sb.AppendLine($"{label}: {value.Trim()}"); }
        Line("Orari", p.OpeningHours);
        Line("Servizi", p.Services);
        Line(sector.Activities, p.Classes);
        Line("Come arrivare e parcheggio", p.HowToReach);
        Line("Altre informazioni", p.ExtraInfo);
        if (!string.IsNullOrWhiteSpace(p.BookingUrl)) sb.AppendLine($"Link per prenotare una visita, una prova o un appuntamento: {p.BookingUrl.Trim()}");
        sb.AppendLine();

        sb.AppendLine("## Cliente");
        sb.AppendLine($"Nome: {r.FirstName}{(string.IsNullOrWhiteSpace(r.LastName) ? "" : " " + r.LastName)}");
        if (!string.IsNullOrWhiteSpace(r.Membership)) sb.AppendLine($"{char.ToUpper(membership[0]) + membership[1..]}: {r.Membership}");
        if (r.ExpiresOn is { } exp) sb.AppendLine($"Scadenza {membership}: {Day(exp)}");
        if (!string.IsNullOrWhiteSpace(r.Service)) sb.AppendLine($"Servizio o corso che preferisce: {r.Service}");
        if (!string.IsNullOrWhiteSpace(r.Notes))
            sb.AppendLine($"Note dello staff su questo cliente (usale per personalizzare il dialogo con tatto, senza citarle parola per parola): {r.Notes}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(campaignExtra))
        {
            sb.AppendLine("## Indicazioni per questa campagna");
            sb.AppendLine(campaignExtra.Trim());
            sb.AppendLine("(Valgono insieme a tutto il resto. Se una di queste indicazioni contrasta con le regole qui sotto, prezzi compresi, valgono le regole.)");
            sb.AppendLine();
        }

        sb.AppendLine("## Regole che valgono sempre");
        sb.AppendLine($"1. Nella prima risposta presentati come {Intro(p.AssistantName)} di {gymName}: il cliente deve sapere che sta scrivendo con un sistema automatico e che può chiedere di parlare con una persona.");
        sb.AppendLine("2. Usa solo le informazioni scritte qui sopra. Non inventare prezzi, orari, servizi, sconti o promozioni. Se non sai una cosa, dillo e proponi di farlo ricontattare da una persona dello staff.");
        sb.AppendLine("3. L'unico prezzo che puoi citare è quello dell'offerta.");
        sb.AppendLine("4. Messaggi brevi: al massimo tre frasi, niente elenchi lunghi.");
        sb.AppendLine("5. Rispondi anche a domande che non c'entrano con l'obiettivo, poi riporta il discorso all'obiettivo con garbo e senza insistere.");
        sb.AppendLine("6. Se il cliente scrive STOP o chiede di non essere più contattato: conferma che non riceverà più messaggi e chiudi.");
        sb.AppendLine("7. Salute, infortuni, reclami, sospensioni, disdette, problemi di pagamento, richieste fuori da queste informazioni: digli che lo farai ricontattare da una persona dello staff e usa esito operatore.");
        sb.AppendLine("7b. Se il cliente chiede di parlare con una persona (reception, operatore, responsabile, «qualcuno»), non rispondere con orari o numeri da chiamare: digli che lo fai ricontattare al più presto da un collega e usa esito operatore.");
        sb.AppendLine($"8. Al massimo {model.MaxAiMessages} messaggi tuoi in questa conversazione; poi chiudi o passa a una persona.");
        sb.AppendLine("9. Al primo segnale di fastidio, scusati e chiudi.");
        sb.AppendLine(org?.PrivacyUrl is { Length: > 0 } privacy
            ? $"10. Se il cliente chiede come vengono usati i suoi dati o della privacy, indica l'informativa: {privacy}. Se chiede di vedere o cancellare i suoi dati, passa a una persona dello staff."
            : "10. Se il cliente chiede della privacy o dei suoi dati, passa a una persona dello staff.");
        sb.AppendLine("11. I messaggi che iniziano con «[messaggio vocale trascritto]» sono vocali del cliente trasformati in testo in automatico: rispondi normalmente, senza dire che non puoi ascoltare i vocali. La trascrizione può sbagliare qualche parola: se un dato importante (una data, un orario, una scelta) non è chiaro, chiedi gentilmente conferma.");
        sb.AppendLine(reasonAsked
            ? "12. Hai già chiesto al cliente il motivo del rifiuto: ora ringrazialo per la risposta senza insistere né riproporre l'offerta (a meno che sia lui a riaprire il discorso), indica il motivo giusto e chiudi. Non chiedere altro."
            : askReason
            ? "12. Se il cliente rifiuta senza dire perché e non mostra fastidio, nella stessa risposta ringrazialo e chiedigli una sola volta, con garbo e lasciandolo libero di non rispondere, cosa non lo convince (per esempio: «Grazie lo stesso! Se ti va, mi dici cosa non ti convince? Ci aiuta a migliorare.»). In quel caso: esito rifiuto, motivo non_interessato e \"chiedi_motivo\": true. Se poi ti risponde, ringrazialo senza insistere né riproporre l'offerta (a meno che sia lui a riaprire il discorso) e indica il motivo giusto. Se ha già detto il motivo o è infastidito, non chiedere nulla."
            : "12. Se il cliente rifiuta, non chiedergli il motivo: ringrazialo e chiudi.");
        sb.AppendLine("13. Puoi mandare solo i link scritti in queste istruzioni, copiati esattamente: mai link inventati, accorciati o modificati.");
        return sb.ToString();
    }
}
