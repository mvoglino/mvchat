namespace MvChat.Web.Ai;

/// <summary>
/// Motivi per cui un cliente rifiuta la proposta. Elenco fisso e uguale per tutti i tipi di attività:
/// così i numeri del Report si possono confrontare tra campagne, attività e periodi.
/// </summary>
public static class RefusalReasons
{
    public const string Other = "altro";

    public static readonly (string Code, string Label, string ForAi)[] All =
    {
        ("prezzo", "Prezzo / costo", "il prezzo è troppo alto o non può permetterselo"),
        ("tempo", "Mancanza di tempo", "non ha tempo (lavoro, famiglia, orari)"),
        ("distanza", "Trasferito / troppo lontano", "si è trasferito o la sede è scomoda da raggiungere"),
        ("salute", "Motivi di salute", "infortunio, malattia, gravidanza o altri motivi di salute"),
        ("concorrenza", "Ha scelto un'altra attività", "va o andrà da un concorrente"),
        ("servizio", "Non soddisfatto del servizio", "è scontento del servizio, del personale o della struttura"),
        ("rimanda", "Ci pensa / più avanti", "forse più avanti, ci deve pensare, ora non è il momento"),
        ("non_interessato", "Non interessato", "non interessato senza dire perché"),
        (Other, "Altro", "un motivo diverso da quelli sopra"),
    };

    public static bool IsValid(string? code) => code is not null && All.Any(r => r.Code == code);

    public static string Label(string? code) =>
        code is null ? "Non indicato" : All.FirstOrDefault(r => r.Code == code).Label ?? "Non indicato";
}
