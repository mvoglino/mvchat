using System.Globalization;
using System.Text;
using MvChat.Web.Infrastructure;

namespace MvChat.Web.Catalog;

/// <summary>I dati dell'attività (struttura) che l'assistente può usare: tipo di attività e presentazione.</summary>
public sealed record OrgInfo(int Id, string Name, string SectorKey, string? Description, string? Website, string? Phone)
{
    public Sector Sector => Sectors.Get(SectorKey);
}

/// <summary>Dati del destinatario usati nei messaggi. Nell'anteprima sono di esempio.</summary>
public sealed record Recipient(string FirstName, string? LastName, string? Membership, DateTime? ExpiresOn);

/// <summary>
/// Compone le istruzioni che l'assistente AI riceve per una conversazione:
/// regole fisse di mvchat + modello di obiettivo + scheda sede + offerta + dati del cliente.
/// La sede non scrive prompt: compila moduli, e questa classe li traduce.
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
            .Replace("{{offerta}}", offer?.Title ?? "");

    public static string Build(GoalModel model, string gymName, GymProfile p, Offer? offer, Recipient r, DateTime today, OrgInfo? org = null)
    {
        var lei = p.Formality == "lei";
        var sector = org?.Sector ?? Sectors.Get("palestra");
        var membership = sector.Membership;
        var sb = new StringBuilder();
        sb.AppendLine($"Sei l'{p.AssistantName} di {gymName} ({sector.Label.ToLowerInvariant()}) e scrivi su WhatsApp a un {sector.Customer}.");
        sb.AppendLine($"Oggi è {Day(today)}. Scrivi in italiano, {(lei ? "dando del lei" : "dando del tu")}, con tono cordiale e diretto, come una persona dello staff.");
        sb.AppendLine();

        if (org is not null && (!string.IsNullOrWhiteSpace(org.Description) || !string.IsNullOrWhiteSpace(org.Website) || !string.IsNullOrWhiteSpace(org.Phone)))
        {
            sb.AppendLine($"## Chi siamo: {org.Name}");
            if (!string.IsNullOrWhiteSpace(org.Description)) sb.AppendLine(org.Description.Trim());
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
            if (!string.IsNullOrWhiteSpace(offer.ActionUrl)) sb.AppendLine($"Per aderire il cliente usa questo link: {offer.ActionUrl}");
            else sb.AppendLine($"Per aderire il cliente passa in sede ({gymName}): proponi di fissare quando.");
        }
        sb.AppendLine();

        sb.AppendLine($"## Scheda di {gymName}");
        void Line(string label, string? value) { if (!string.IsNullOrWhiteSpace(value)) sb.AppendLine($"{label}: {value.Trim()}"); }
        Line("Orari", p.OpeningHours);
        Line("Servizi", p.Services);
        Line(sector.Activities, p.Classes);
        Line("Come arrivare e parcheggio", p.HowToReach);
        Line("Altre informazioni", p.ExtraInfo);
        sb.AppendLine();

        sb.AppendLine("## Cliente");
        sb.AppendLine($"Nome: {r.FirstName}{(string.IsNullOrWhiteSpace(r.LastName) ? "" : " " + r.LastName)}");
        if (!string.IsNullOrWhiteSpace(r.Membership)) sb.AppendLine($"{char.ToUpper(membership[0]) + membership[1..]}: {r.Membership}");
        if (r.ExpiresOn is { } exp) sb.AppendLine($"Scadenza {membership}: {Day(exp)}");
        sb.AppendLine();

        sb.AppendLine("## Regole che valgono sempre");
        sb.AppendLine($"1. Nella prima risposta presentati come {p.AssistantName} di {gymName}: il cliente deve sapere che sta scrivendo con un sistema automatico e che può chiedere di parlare con una persona.");
        sb.AppendLine("2. Usa solo le informazioni scritte qui sopra. Non inventare prezzi, orari, servizi, sconti o promozioni. Se non sai una cosa, dillo e proponi di farlo ricontattare da una persona dello staff.");
        sb.AppendLine("3. L'unico prezzo che puoi citare è quello dell'offerta.");
        sb.AppendLine("4. Messaggi brevi: al massimo tre frasi, niente elenchi lunghi.");
        sb.AppendLine("5. Rispondi anche a domande che non c'entrano con l'obiettivo, poi riporta il discorso all'obiettivo con garbo e senza insistere.");
        sb.AppendLine("6. Se il cliente scrive STOP o chiede di non essere più contattato: conferma che non riceverà più messaggi e chiudi.");
        sb.AppendLine("7. Salute, infortuni, reclami, sospensioni, disdette, problemi di pagamento, richieste fuori da queste informazioni: proponi di farlo ricontattare da una persona dello staff.");
        sb.AppendLine($"8. Al massimo {model.MaxAiMessages} messaggi tuoi in questa conversazione; poi chiudi o passa a una persona.");
        sb.AppendLine("9. Al primo segnale di fastidio, scusati e chiudi.");
        return sb.ToString();
    }
}
