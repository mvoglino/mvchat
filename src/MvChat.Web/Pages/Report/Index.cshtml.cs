using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MvChat.Web.Data;
using MvChat.Web.Infrastructure;
using MvChat.Web.Reports;
using MvChat.Web.Security;

namespace MvChat.Web.Pages.Report;

/// <summary>
/// Risultati per livello: MVitalia vede gruppi e attività singole, l'amministratore di gruppo le sue attività,
/// l'amministratore di attività le sue campagne. Da ogni riga si scende di un livello.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ReportRepo _report; private readonly Repos _repos; private readonly AppConfigStore _config;
    public IndexModel(ReportRepo report, Repos repos, AppConfigStore config) { _report = report; _repos = repos; _config = config; }

    public Scope Me { get; private set; } = new();
    public DateTime From { get; private set; }
    public DateTime To { get; private set; }
    public int? Group { get; private set; }
    public int? Activity { get; private set; }
    public string Level { get; private set; } = "";        // clienti · attivita · campagne
    public string Title { get; private set; } = "";
    public List<(string Label, string? Url)> Crumbs { get; } = new();
    public List<ReportRow> Rows { get; private set; } = new();
    public ReportRow Total { get; private set; } = new();
    public List<DayPoint> Days { get; private set; } = new();
    public List<ReasonCount> Reasons { get; private set; } = new();
    public int ReasonsTotal => Reasons.Sum(r => r.Count);
    public MetaSettings Meta => _config.Current.Meta;
    public List<BreakdownRow> ByGoal { get; private set; } = new();
    public List<BreakdownRow> ByOffer { get; private set; } = new();
    public List<BreakdownRow> ByTemplate { get; private set; } = new();
    public List<RecontactRow> Recontact { get; private set; } = new();
    /// <summary>Lo stesso numero di giorni subito prima del periodo scelto, per il confronto.</summary>
    public DateTime PrevFrom { get; private set; }
    public DateTime PrevTo { get; private set; }
    public ReportRow Prev { get; private set; } = new();
    public decimal Value => ByOffer.Sum(o => o.Value);
    public decimal PrevValue { get; private set; }
    /// <summary>Il costo dell'AI lo vede solo MVitalia: per le attività è compreso nell'abbonamento.</summary>
    public bool ShowAi => Me.IsSuperAdmin;
    /// <summary>Costo del periodo in euro: messaggi Meta stimati (+ AI convertita in euro, solo per MVitalia).</summary>
    public decimal CostEur(ReportRow r) => Math.Round(r.MetaCostEur(Meta) + (ShowAi ? r.AiCostUsd * _config.Current.Billing.UsdToEur : 0), 2);
    /// <summary>Le attività visibili, per scegliere subito quella da guardare (solo se sono più di una).</summary>
    public List<Gym> Gyms { get; private set; } = new();
    /// <summary>Periodi pronti: un clic e i totali si aggiornano.</summary>
    public List<(string Label, DateTime From, DateTime To)> Periods { get; } = new();

    private async Task<bool> LoadAsync(DateTime? dal, DateTime? al, int? gruppo, int? attivita)
    {
        Me = User.Scope();
        var today = DateTime.UtcNow.ToRome().Date;
        To = (al ?? today).Date; From = (dal ?? To.AddDays(-29)).Date;
        if (From > To) (From, To) = (To, From);
        if ((To - From).TotalDays > 366) From = To.AddDays(-366);
        var fromUtc = From.FromRome(); var toUtc = To.AddDays(1).FromRome();

        var gyms = await _repos.GymsAsync(Me);
        Gyms = gyms.OrderBy(g => g.OrganizationName).ThenBy(g => g.Name).ToList();
        var first = new DateTime(today.Year, today.Month, 1);
        Periods.Add(("Ultimi 7 giorni", today.AddDays(-6), today));
        Periods.Add(("Ultimi 30 giorni", today.AddDays(-29), today));
        Periods.Add(("Questo mese", first, today));
        Periods.Add(("Mese scorso", first.AddMonths(-1), first.AddDays(-1)));
        Periods.Add(("Da inizio anno", new DateTime(today.Year, 1, 1), today));
        // Il livello si decide dal ruolo e da cosa è stato aperto; quello che arriva dall'indirizzo vale solo se è nel perimetro.
        if (Me.IsManager) attivita = Me.GymId;
        if (Me.IsOrgAdmin) gruppo = Me.OrganizationId;
        if (Me.IsAreaManager && attivita is null) gruppo = Me.OrganizationId; // il totale è quello delle sue attività (il filtro sta nelle query)
        var gym = attivita is int a ? gyms.FirstOrDefault(g => g.Id == a) : null;
        if (attivita is not null && gym is null) return false;
        if (gruppo is int gid && !gyms.Any(g => g.OrganizationId == gid)) return false;
        Activity = gym?.Id; Group = gym is not null ? (gym.InGroup ? gym.OrganizationId : null) : gruppo;

        Crumbs.Add(("Report", Me.IsSuperAdmin ? Url(null, null) : null));
        if (gym is null && Group is int g0)
        {
            Level = "attivita";
            var list = await _report.ActivitiesAsync(Me, fromUtc, toUtc, g0);
            Title = Me.IsAreaManager ? "Le mie attività" : list.FirstOrDefault().OrgName ?? "Gruppo";
            Rows = list.Select(x => { x.Row.Link = Url(g0, x.Row.Id); return x.Row; }).ToList();
            if (Me.IsSuperAdmin) Crumbs.Add((Title, null));
        }
        else if (gym is not null)
        {
            Level = "campagne"; Title = gym.Name;
            Rows = await _report.CampaignsAsync(Me, fromUtc, toUtc, gym.Id);
            foreach (var r in Rows) r.Link = $"/Campagne/{r.Id}";
            if (gym.InGroup && !Me.IsManager) Crumbs.Add((Me.IsAreaManager ? "Le mie attività" : gym.OrganizationName, Url(gym.OrganizationId, null)));
            Crumbs.Add((gym.Name, null));
            // I totali dell'attività comprendono anche le conversazioni fuori campagna (prove escluse).
            var act = (await _report.ActivitiesAsync(Me, fromUtc, toUtc, gym.OrganizationId)).First(x => x.Row.Id == gym.Id).Row;
            Total = act;
        }
        else
        {
            // MVitalia: i gruppi (somma delle loro attività) e le attività singole.
            Level = "clienti"; Title = "Tutti i clienti del servizio";
            var list = await _report.ActivitiesAsync(Me, fromUtc, toUtc, null);
            foreach (var grp in list.GroupBy(x => (x.OrgId, x.OrgName, x.InGroup)))
            {
                if (grp.Key.InGroup)
                {
                    var row = new ReportRow { Kind = "gruppo", Id = grp.Key.OrgId, Name = grp.Key.OrgName, Detail = $"gruppo · {grp.Count()} attività", Link = Url(grp.Key.OrgId, null) };
                    foreach (var x in grp) row.Add(x.Row);
                    Rows.Add(row);
                }
                else
                    foreach (var x in grp) { x.Row.Detail = "attività singola"; x.Row.Link = Url(null, x.Row.Id); Rows.Add(x.Row); }
            }
        }
        if (Level != "campagne") { Total = new ReportRow(); foreach (var r in Rows) Total.Add(r); }
        var fOrg = gym is null ? Group : null; var fGym = gym?.Id;
        Days = await _report.DailyAsync(Me, fromUtc, toUtc, fOrg, fGym);
        Reasons = await _report.RefusalReasonsAsync(Me, fromUtc, toUtc, fOrg, fGym);
        ByGoal = await _report.BreakdownAsync(Me, fromUtc, toUtc, fOrg, fGym, "tipo");
        ByOffer = await _report.BreakdownAsync(Me, fromUtc, toUtc, fOrg, fGym, "offerta");
        ByTemplate = await _report.BreakdownAsync(Me, fromUtc, toUtc, fOrg, fGym, "template");
        Recontact = await _report.RecontactAsync(Me, fromUtc, toUtc, fOrg, fGym);

        // Confronto: stessi giorni subito prima, stesso perimetro.
        var days = (To - From).Days + 1;
        PrevTo = From.AddDays(-1); PrevFrom = PrevTo.AddDays(-(days - 1));
        var pFromUtc = PrevFrom.FromRome(); var pToUtc = PrevTo.AddDays(1).FromRome();
        var prevList = await _report.ActivitiesAsync(Me, pFromUtc, pToUtc, gym is not null ? gym.OrganizationId : Group);
        Prev = new ReportRow();
        foreach (var x in prevList.Where(x => gym is null || x.Row.Id == gym.Id)) Prev.Add(x.Row);
        PrevValue = (await _report.BreakdownAsync(Me, pFromUtc, pToUtc, fOrg, fGym, "offerta")).Sum(o => o.Value);
        return true;
    }

    public async Task<IActionResult> OnGetAsync(DateTime? dal, DateTime? al, int? gruppo, int? attivita) =>
        await LoadAsync(dal, al, gruppo, attivita) ? Page() : NotFound();

    /// <summary>La tabella del report in un file che si apre con Excel.</summary>
    public async Task<IActionResult> OnGetCsvAsync(DateTime? dal, DateTime? al, int? gruppo, int? attivita)
    {
        if (!await LoadAsync(dal, al, gruppo, attivita)) return NotFound();
        var it = CultureInfo.GetCultureInfo("it-IT");
        static string C(string? v) => Csv.Cell(v);
        var sb = new StringBuilder();
        sb.AppendLine("Nome;Tipo;Primi messaggi;Consegnati;Letti;Conversazioni;Con risposta;Obiettivo raggiunto;Rifiuti;Non contattare più;Solo assistente AI;Con operatore;Costo Meta stimato (€)" + (ShowAi ? ";Costo AI ($)" : ""));
        foreach (var r in Rows.Append(new ReportRow { Name = "Totale", Kind = "" }.Also(t => t.Add(Total))))
            sb.AppendLine(string.Join(";", C(r.Name), C(r.Detail ?? r.Kind), r.Sent, r.Delivered, r.Read, r.Conversations, r.Replied, r.Reached, r.Refused, r.OptOut,
                r.AiOnly, r.WithOperator, r.MetaCostEur(Meta).ToString("0.00", it)) + (ShowAi ? ";" + r.AiCostUsd.ToString("0.0000", it) : ""));
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"report-{From:yyyyMMdd}-{To:yyyyMMdd}.csv");
    }

    /// <summary>I motivi dei rifiuti in un file per Excel: numero e percentuale per motivo.</summary>
    public async Task<IActionResult> OnGetMotiviAsync(DateTime? dal, DateTime? al, int? gruppo, int? attivita)
    {
        if (!await LoadAsync(dal, al, gruppo, attivita)) return NotFound();
        var it = CultureInfo.GetCultureInfo("it-IT");
        var sb = new StringBuilder();
        sb.AppendLine($"Motivi dei rifiuti;{Csv.Cell(Title)};dal {From:dd/MM/yyyy} al {To:dd/MM/yyyy}");
        sb.AppendLine("Motivo;Rifiuti;Percentuale");
        foreach (var r in Reasons)
            sb.AppendLine(string.Join(";", Csv.Cell(r.Label), r.Count, (ReasonsTotal == 0 ? 0m : 100m * r.Count / ReasonsTotal).ToString("0.0", it) + "%"));
        sb.AppendLine($"Totale;{ReasonsTotal};100%");
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"motivi-rifiuti-{From:yyyyMMdd}-{To:yyyyMMdd}.csv");
    }

    /// <summary>Le parti del report che si possono scaricare da sole (una per riquadro).</summary>
    public static readonly (string Code, string Label)[] Parts =
    {
        ("riepilogo", "Riepilogo"), ("dettaglio", "Dettaglio"), ("tipi", "Per tipo di campagna"), ("offerte", "Per offerta"),
        ("template", "Per primo messaggio"), ("motivi", "Motivi dei rifiuti"), ("giorni", "Giorno per giorno"), ("ricontattare", "Da ricontattare")
    };

    /// <summary>
    /// Il report in un file Excel con un foglio per tipologia (o solo la parte scelta).
    /// Ogni tabella ha il filtro automatico: il cliente sceglie le righe con le frecce dell'intestazione.
    /// </summary>
    public async Task<IActionResult> OnGetExcelAsync(DateTime? dal, DateTime? al, int? gruppo, int? attivita, string? parte)
    {
        if (!await LoadAsync(dal, al, gruppo, attivita)) return NotFound();
        var only = Parts.Any(p => p.Code == parte) ? parte : null;
        bool Want(string code) => only is null || only == code;
        var wb = new Xlsx();
        var period = $"dal {From:dd/MM/yyyy} al {To:dd/MM/yyyy} · prove escluse";
        var levelName = Level switch { "clienti" => "Cliente", "attivita" => "Attività", _ => "Campagna" };

        if (Want("riepilogo"))
        {
            var sh = wb.Sheet("Riepilogo").Title("Report · " + Title).Note(period)
                .Note($"Confronto con lo stesso numero di giorni subito prima: dal {PrevFrom:dd/MM/yyyy} al {PrevTo:dd/MM/yyyy}").Blank()
                .Header("Indicatore", "Periodo scelto", "Periodo precedente", "Differenza");
            void Line(string name, object cur, object prev, object diff) => sh.Row(name, cur, prev, diff);
            Line("Primi messaggi inviati", Total.Sent, Prev.Sent, Change(Total.Sent, Prev.Sent));
            Line("Messaggi letti", Total.Read, Prev.Read, Change(Total.Read, Prev.Read));
            Line("Clienti contattati", Total.Conversations, Prev.Conversations, Change(Total.Conversations, Prev.Conversations));
            Line("Clienti che hanno risposto", Total.Replied, Prev.Replied, Change(Total.Replied, Prev.Replied));
            Line("% di risposta", XPct.Of(Total.Replied, Total.Conversations), XPct.Of(Prev.Replied, Prev.Conversations), "");
            Line("Obiettivi raggiunti", Total.Reached, Prev.Reached, Change(Total.Reached, Prev.Reached));
            Line("% obiettivo sui contattati", XPct.Of(Total.Reached, Total.Conversations), XPct.Of(Prev.Reached, Prev.Conversations), "");
            Line("Rifiuti", Total.Refused, Prev.Refused, Change(Total.Refused, Prev.Refused));
            Line("Non contattare più (STOP)", Total.OptOut, Prev.OptOut, Change(Total.OptOut, Prev.OptOut));
            Line("Dialoghi solo con l'assistente AI", Total.AiOnly, Prev.AiOnly, Change(Total.AiOnly, Prev.AiOnly));
            Line("Dialoghi con operatore", Total.WithOperator, Prev.WithOperator, Change(Total.WithOperator, Prev.WithOperator));
            Line("Valore stimato generato (adesioni × prezzo offerta)", new XEur(Value), new XEur(PrevValue), ChangeD(Value, PrevValue));
            Line("Costo Meta stimato", new XEur(Total.MetaCostEur(Meta)), new XEur(Prev.MetaCostEur(Meta)), ChangeD(Total.MetaCostEur(Meta), Prev.MetaCostEur(Meta)));
            if (ShowAi)
            {
                Line("Consumo AI (in euro)", new XEur(Math.Round(Total.AiCostUsd * _config.Current.Billing.UsdToEur, 2)), new XEur(Math.Round(Prev.AiCostUsd * _config.Current.Billing.UsdToEur, 2)), ChangeD(Total.AiCostUsd, Prev.AiCostUsd));
                Line("Costo totale (Meta + AI)", new XEur(CostEur(Total)), new XEur(CostEur(Prev)), ChangeD(CostEur(Total), CostEur(Prev)));
            }
            Line("Costo per obiettivo raggiunto", new XEur(Total.Reached == 0 ? 0 : Math.Round(CostEur(Total) / Total.Reached, 2)), new XEur(Prev.Reached == 0 ? 0 : Math.Round(CostEur(Prev) / Prev.Reached, 2)), "");
            sh.Blank().Note("Il valore stimato conta le adesioni delle campagne con un'offerta che ha un prezzo; non è un incasso registrato.");
        }
        if (Want("dettaglio"))
        {
            var sh = wb.Sheet(Level switch { "clienti" => "Gruppi e attività", "attivita" => "Attività", _ => "Campagne" })
                .Title((Level switch { "clienti" => "Gruppi e attività singole", "attivita" => "Confronto tra le attività", _ => "Campagne" }) + " · " + Title).Note(period).Blank()
                .Header(new[] { levelName, "Tipo", "Primi messaggi", "Consegnati", "Letti", "% letti", "Contattati", "Con risposta", "% risposta",
                    "Obiettivo raggiunto", "% obiettivo", "Rifiuti", "Non contattare più", "Solo assistente AI", "Con operatore", "Costo Meta stimato" }.Concat(ShowAi ? new[] { "Consumo AI ($)" } : Array.Empty<string>()).ToArray());
            foreach (var r in Rows) DetailRow(sh.Row, r, r.Name, r.Detail ?? r.Kind);
            DetailRow(sh.Total, Total, "Totale", "");
        }
        if (Want("tipi")) BreakdownSheet(wb, "Per tipo di campagna", "Tipo di campagna (obiettivo)", ByGoal, period, false, false);
        if (Want("offerte")) BreakdownSheet(wb, "Per offerta", "Offerta", ByOffer, period, true, true);
        if (Want("template")) BreakdownSheet(wb, "Per primo messaggio", "Primo messaggio (template)", ByTemplate, period, true, false);
        if (Want("motivi"))
        {
            var sh = wb.Sheet("Motivi dei rifiuti").Title("Motivi dei rifiuti · " + Title).Note(period).Blank().Header("Motivo", "Rifiuti", "Percentuale");
            foreach (var r in Reasons) sh.Row(r.Label, r.Count, XPct.Of(r.Count, ReasonsTotal));
            sh.Total("Totale", ReasonsTotal, new XPct(ReasonsTotal == 0 ? 0 : 1));
        }
        if (Want("giorni"))
        {
            var sh = wb.Sheet("Giorno per giorno").Title("Andamento giorno per giorno · " + Title).Note(period).Blank()
                .Header("Giorno", "Primi messaggi inviati", "Messaggi dei clienti", "Obiettivi raggiunti");
            foreach (var d in Days) sh.Row(new XDate(d.Day), d.Sent, d.Received, d.Reached);
            sh.Total("Totale", Days.Sum(d => d.Sent), Days.Sum(d => d.Received), Days.Sum(d => d.Reached));
        }
        if (Want("ricontattare"))
        {
            var sh = wb.Sheet("Da ricontattare").Title("Clienti da ricontattare · " + Title).Note(period)
                .Note("Esclusi i clienti nella lista STOP e chi in seguito ha raggiunto l'obiettivo. Usa le frecce dell'intestazione per scegliere il motivo.").Blank()
                .Header("Motivo", "Cliente", "Cellulare", "Attività", "Campagna", "Tipo di campagna", "Tipo (abbonamento, soggiorno…)", "Data (scadenza, soggiorno…)", "Servizio / corso",
                    "Esito", "Nota", "Ultimo messaggio del cliente", "Contattato il");
            foreach (var r in Recontact)
                sh.Row(RecontactRow.Label(r.Category), r.Name, r.Phone, r.GymName, r.CampaignName ?? "", r.GoalName, r.Membership ?? "",
                    r.ExpiresOn is DateTime e ? new XDate(e) : null, r.Service ?? "", Ai.Outcomes.Label(r.Outcome), r.Note ?? "",
                    r.LastInboundAt is DateTime li ? li.ToRome() : null, r.CreatedAt.ToRome());
            await _repos.AuditAsync(Me, "report.export", $"clienti da ricontattare: {Recontact.Count}", HttpContext.Connection.RemoteIpAddress?.ToString());
        }
        var name = only is null ? "report" : "report-" + only;
        return File(wb.ToBytes(), Xlsx.ContentType, $"{name}-{From:yyyyMMdd}-{To:yyyyMMdd}.xlsx");
    }

    private void DetailRow(Func<object?[], XlsxSheet> add, ReportRow r, string name, string kind) =>
        add(new object?[] { name, kind, r.Sent, r.Delivered, r.Read, XPct.Of(r.Read, r.Sent), r.Conversations, r.Replied, XPct.Of(r.Replied, r.Conversations),
            r.Reached, XPct.Of(r.Reached, r.Conversations), r.Refused, r.OptOut, r.AiOnly, r.WithOperator, new XEur(r.MetaCostEur(Meta)) }.Concat(ShowAi ? new object?[] { Math.Round(r.AiCostUsd, 4) } : Array.Empty<object?>()).ToArray());

    private void BreakdownSheet(Xlsx wb, string sheet, string first, List<BreakdownRow> rows, string period, bool withGym, bool withValue)
    {
        var sh = wb.Sheet(sheet).Title(sheet + " · " + Title).Note(period).Blank();
        var head = new List<string> { first };
        if (withGym) head.Add("Attività");
        head.AddRange(new[] { "Contattati", "Con risposta", "% risposta", "Obiettivo raggiunto", "% obiettivo", "Rifiuti", "Non contattare più" });
        if (withValue) head.AddRange(new[] { "Prezzo offerta", "Valore stimato" });
        sh.Header(head.ToArray());
        object?[] Cells(BreakdownRow r, string label, string? gym)
        {
            var c = new List<object?> { label };
            if (withGym) c.Add(gym ?? "");
            c.AddRange(new object?[] { r.Contacted, r.Replied, XPct.Of(r.Replied, r.Contacted), r.Reached, XPct.Of(r.Reached, r.Contacted), r.Refused, r.OptOut });
            if (withValue) c.AddRange(new object?[] { r.Price is decimal p ? new XEur(p) : "", new XEur(r.Value) });
            return c.ToArray();
        }
        foreach (var r in rows) sh.Row(Cells(r, r.Label, r.Detail));
        var tot = new BreakdownRow("Totale", null, rows.Sum(r => r.Contacted), rows.Sum(r => r.Replied), rows.Sum(r => r.Reached), rows.Sum(r => r.Refused), rows.Sum(r => r.OptOut), null);
        var t = Cells(tot, "Totale", "");
        if (withValue) t[^1] = new XEur(rows.Sum(r => r.Value));
        sh.Total(t);
    }

    /// <summary>Variazione percentuale rispetto al periodo precedente (vuota se prima era zero).</summary>
    public static object Change(int cur, int prev) => prev == 0 ? (cur == 0 ? new XPct(0) : "nuovo") : new XPct((double)(cur - prev) / prev);
    public static object ChangeD(decimal cur, decimal prev) => prev == 0 ? (cur == 0 ? new XPct(0) : "nuovo") : new XPct((double)((cur - prev) / prev));

    /// <summary>Freccia e percentuale per la pagina: ▲ +12% · ▼ −5% · = .</summary>
    public static (string Text, string Css) Delta(decimal cur, decimal prev, bool lowerIsBetter = false)
    {
        if (prev == 0) return cur == 0 ? ("= come prima", "") : ("nuovo", "up");
        var pct = (cur - prev) / prev * 100m;
        if (Math.Abs(pct) < 0.5m) return ("= come prima", "");
        var up = pct > 0;
        var good = up != lowerIsBetter;
        return ($"{(up ? "▲ +" : "▼ −")}{Math.Abs(pct):0}%", good ? "up" : "down");
    }

    /// <summary>Stessa vista, altro periodo.</summary>
    public string PeriodUrl(DateTime from, DateTime to) =>
        $"/Report?dal={from:yyyy-MM-dd}&al={to:yyyy-MM-dd}" + (Group is int g ? $"&gruppo={g}" : "") + (Activity is int a ? $"&attivita={a}" : "");

    public string ExcelUrl(string? parte = null) => Url(Group, Activity, "Excel") + (parte is null ? "" : "&parte=" + parte);

    public string Url(int? gruppo, int? attivita, string? handler = null) =>
        $"/Report?dal={From:yyyy-MM-dd}&al={To:yyyy-MM-dd}" + (gruppo is int g ? $"&gruppo={g}" : "") + (attivita is int a ? $"&attivita={a}" : "") + (handler is null ? "" : $"&handler={handler}");
}

internal static class Fluent
{
    public static T Also<T>(this T x, Action<T> a) { a(x); return x; }
}
