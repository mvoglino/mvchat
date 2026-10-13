namespace MvChat.Web.Infrastructure;

/// <summary>
/// Il tipo di attività di un gruppo. Cambia le parole che l'assistente AI usa con i clienti
/// (palestra/hotel/studio, iscritto/ospite/paziente, abbonamento/soggiorno/trattamento):
/// le pagine di mvchat usano invece parole neutre (gruppo, attività, cliente).
/// </summary>
public sealed record Sector(string Key, string Label, string Place, string Customer, string Membership, string Activities,
    string KindLabel = "Tipo", string DateLabel = "Data", bool Sensitive = false, string Example = "")
{
    /// <summary>Esempio di istruzioni in più per una campagna, con le parole del settore.</summary>
    public string ExtraExample => Example.Length > 0 ? Example : "Es. Dal 20 al 27 dicembre siamo chiusi. Chi aderisce entro il 15 riceve anche un omaggio.";
}

public static class Sectors
{
    /// <summary>
    /// Per ogni tipo: parole dell'assistente (luogo, cliente, «abbonamento», attività) e nomi delle due colonne
    /// della lista che cambiano significato (Tipo e Data). Sensitive = dati che possono riguardare la salute:
    /// le note non vanno all'assistente e i vocali non si trascrivono.
    /// </summary>
    public static readonly Sector[] All =
    {
        new("palestra",   "Palestra / centro fitness",          "palestra",          "iscritto",  "abbonamento",  "Corsi",
            "Tipo abbonamento", "Scadenza abbonamento", false, "Es. Dal 20 al 27 dicembre la palestra è chiusa. Chi rinnova entro il 15 riceve anche la maglietta."),
        new("benessere",  "Centro estetico / benessere / spa",  "centro",            "cliente",   "trattamento",  "Trattamenti",
            "Trattamento / pacchetto", "Data ultimo trattamento", true, "Es. A dicembre il centro è aperto anche la domenica. Chi prenota entro il 15 riceve un massaggio viso in omaggio."),
        new("salute",     "Studio medico / dentistico / fisioterapia", "studio",     "paziente",  "percorso",     "Prestazioni",
            "Prestazione / percorso", "Data appuntamento o richiamo", true, "Es. Lo studio è chiuso dal 10 al 20 agosto. Per urgenze indicare il numero della segreteria."),
        new("hotel",      "Hotel / struttura ricettiva",        "struttura",         "ospite",    "soggiorno",    "Servizi ed esperienze",
            "Tipo di soggiorno", "Data ultimo soggiorno", false, "Es. La spa riapre il 1° marzo. Chi prenota entro fine gennaio ha la colazione inclusa."),
        new("ristorazione","Ristorante / bar",                  "locale",            "cliente",   "tessera",      "Menu ed eventi",
            "Tessera / tipo cliente", "Data ultima visita", false, "Es. Il locale è chiuso il lunedì. Per gruppi oltre 8 persone serve la prenotazione telefonica."),
        new("formazione", "Scuola / formazione / corsi",        "scuola",            "iscritto",  "iscrizione",   "Corsi",
            "Corso / iscrizione", "Scadenza iscrizione", false, "Es. Le lezioni iniziano il 6 ottobre. Chi si iscrive entro il 20 settembre ha il materiale incluso."),
        new("negozio",    "Negozio / commercio",                "negozio",           "cliente",   "tessera fedeltà", "Prodotti e promozioni",
            "Tessera / tipo cliente", "Scadenza tessera", false, "Es. Il negozio è aperto anche le domeniche di dicembre. La promozione non vale sui prodotti già scontati."),
        new("servizi",    "Servizi professionali",              "studio",            "cliente",   "contratto",    "Servizi",
            "Contratto / servizio", "Scadenza contratto", false, "Es. Lo studio è chiuso dal 10 al 20 agosto. Per le pratiche urgenti indicare l'email della segreteria."),
        new("altro",      "Altra attività",                     "attività",          "cliente",   "servizio",     "Attività",
            "Tipo di servizio", "Data di riferimento", false, ""),
    };

    public static Sector Get(string? key) => All.FirstOrDefault(s => s.Key == key) ?? All[^1];
}
