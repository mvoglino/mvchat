namespace MvChat.Web.Infrastructure;

/// <summary>
/// Il tipo di attività di una struttura. Cambia le parole che l'assistente AI usa con i clienti
/// (palestra/hotel/studio, iscritto/ospite/paziente, abbonamento/soggiorno/trattamento):
/// le pagine di mvchat usano invece parole neutre (struttura, sede, cliente).
/// </summary>
public sealed record Sector(string Key, string Label, string Place, string Customer, string Membership, string Activities);

public static class Sectors
{
    public static readonly Sector[] All =
    {
        new("palestra",   "Palestra / centro fitness",          "palestra",          "iscritto",  "abbonamento",  "Corsi"),
        new("benessere",  "Centro estetico / benessere / spa",  "centro",            "cliente",   "trattamento",  "Trattamenti"),
        new("salute",     "Studio medico / dentistico / fisioterapia", "studio",     "paziente",  "percorso",     "Prestazioni"),
        new("hotel",      "Hotel / struttura ricettiva",        "struttura",         "ospite",    "soggiorno",    "Servizi ed esperienze"),
        new("ristorazione","Ristorante / bar",                  "locale",            "cliente",   "tessera",      "Menu ed eventi"),
        new("formazione", "Scuola / formazione / corsi",        "scuola",            "iscritto",  "iscrizione",   "Corsi"),
        new("negozio",    "Negozio / commercio",                "negozio",           "cliente",   "tessera fedeltà", "Prodotti e promozioni"),
        new("servizi",    "Servizi professionali",              "studio",            "cliente",   "contratto",    "Servizi"),
        new("altro",      "Altra attività",                     "attività",          "cliente",   "servizio",     "Attività"),
    };

    public static Sector Get(string? key) => All.FirstOrDefault(s => s.Key == key) ?? All[^1];
}
