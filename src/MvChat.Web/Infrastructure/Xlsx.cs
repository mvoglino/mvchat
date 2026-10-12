using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace MvChat.Web.Infrastructure;

/// <summary>Una percentuale da scrivere nel file Excel (0,25 = 25%).</summary>
public readonly record struct XPct(double Value)
{
    public static XPct Of(int part, int total) => new(total == 0 ? 0 : (double)part / total);
}

/// <summary>Un importo in euro da scrivere nel file Excel.</summary>
public readonly record struct XEur(decimal Value);

/// <summary>Una data senza ora.</summary>
public readonly record struct XDate(DateTime Value);

/// <summary>Un foglio del file Excel: un titolo, una o più tabelle con intestazione.</summary>
public sealed class XlsxSheet
{
    internal readonly List<(int Style, object?[] Cells)> Rows = new();
    internal int? FilterRow;      // riga (da 0) dell'intestazione con il filtro automatico
    internal int FilterCols;
    internal int? FreezeRow;
    public string Name { get; }
    internal XlsxSheet(string name) => Name = name;

    /// <summary>Riga di titolo in grassetto grande.</summary>
    public XlsxSheet Title(string text) { Rows.Add((Xlsx.STitle, new object?[] { text })); return this; }

    /// <summary>Riga di nota in corsivo grigio (periodo, avvertenze).</summary>
    public XlsxSheet Note(string text) { Rows.Add((Xlsx.SNote, new object?[] { text })); return this; }

    public XlsxSheet Blank() { Rows.Add((0, Array.Empty<object?>())); return this; }

    /// <summary>
    /// Intestazione di una tabella. La prima intestazione del foglio riceve il filtro automatico
    /// (le frecce per scegliere le righe) e resta ferma in alto scorrendo.
    /// </summary>
    public XlsxSheet Header(params string[] names)
    {
        if (FilterRow is null) { FilterRow = Rows.Count; FilterCols = names.Length; FreezeRow = Rows.Count + 1; }
        Rows.Add((Xlsx.SHeader, names.Cast<object?>().ToArray()));
        return this;
    }

    public XlsxSheet Row(params object?[] cells) { Rows.Add((0, cells)); return this; }

    /// <summary>Riga di totale in grassetto: interrompe il filtro automatico prima di sé.</summary>
    public XlsxSheet Total(params object?[] cells) { Rows.Add((Xlsx.SBold, cells)); return this; }
}

/// <summary>
/// File Excel (.xlsx) scritto da mvchat senza librerie esterne: più fogli, intestazioni in grassetto,
/// filtri automatici, numeri veri (non testo), percentuali, euro e date nel formato italiano.
/// I testi sono sempre scritti come testo: un contenuto che inizia con "=" non diventa mai una formula.
/// </summary>
public sealed class Xlsx
{
    internal const int SDefault = 0, SHeader = 1, SBold = 2, STitle = 3, SNote = 4;
    // Stili delle celle (indice in cellXfs): per ogni stile di riga, la variante per tipo di dato.
    private const int XNum = 5, XPctS = 6, XEurS = 7, XDateS = 8, XDateTimeS = 9, XDec = 10,
        XBoldNum = 11, XBoldPct = 12, XBoldEur = 13, XBoldDec = 14;

    private readonly List<XlsxSheet> _sheets = new();

    public XlsxSheet Sheet(string name)
    {
        var clean = new string(name.Where(c => !"[]:*?/\\".Contains(c)).ToArray()).Trim();
        if (clean.Length > 31) clean = clean[..31];
        if (clean.Length == 0) clean = "Foglio" + (_sheets.Count + 1);
        var baseName = clean; var i = 2;
        while (_sheets.Any(s => s.Name.Equals(clean, StringComparison.OrdinalIgnoreCase)))
            clean = (baseName.Length > 28 ? baseName[..28] : baseName) + " " + i++;
        var sh = new XlsxSheet(clean);
        _sheets.Add(sh);
        return sh;
    }

    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public byte[] ToBytes()
    {
        if (_sheets.Count == 0) Sheet("Foglio1");
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            Add(zip, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                string.Concat(_sheets.Select((_, i) => $"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>")) +
                "</Types>");
            Add(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                "</Relationships>");
            var defined = string.Concat(_sheets.Select((s, i) => s.FilterRow is int fr && s.FilterCols > 0
                ? $"<definedName name=\"_xlnm._FilterDatabase\" localSheetId=\"{i}\" hidden=\"1\">'{Esc(s.Name.Replace("'", "''"))}'!${Col(0)}${fr + 1}:${Col(s.FilterCols - 1)}${FilterEnd(s) + 1}</definedName>" : ""));
            Add(zip, "xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                "<sheets>" + string.Concat(_sheets.Select((s, i) => $"<sheet name=\"{Esc(s.Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>")) + "</sheets>" +
                (defined.Length > 0 ? "<definedNames>" + defined + "</definedNames>" : "") +
                "</workbook>");
            Add(zip, "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                string.Concat(_sheets.Select((_, i) => $"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>")) +
                $"<Relationship Id=\"rId{_sheets.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                "</Relationships>");
            Add(zip, "xl/styles.xml", Styles);
            for (var i = 0; i < _sheets.Count; i++) Add(zip, $"xl/worksheets/sheet{i + 1}.xml", SheetXml(_sheets[i]));
        }
        return ms.ToArray();
    }

    /// <summary>Ultima riga della tabella filtrata: fino alla prima riga vuota o di totale.</summary>
    private static int FilterEnd(XlsxSheet s)
    {
        var end = s.FilterRow!.Value;
        for (var r = end + 1; r < s.Rows.Count; r++)
        {
            if (s.Rows[r].Style != SDefault || s.Rows[r].Cells.Length == 0) break;
            end = r;
        }
        return end;
    }

    private static string SheetXml(XlsxSheet s)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
        sb.Append("<sheetViews><sheetView workbookViewId=\"0\">");
        if (s.FreezeRow is int fz && fz > 0)
            sb.Append($"<pane ySplit=\"{fz}\" topLeftCell=\"A{fz + 1}\" activePane=\"bottomLeft\" state=\"frozen\"/><selection pane=\"bottomLeft\" activeCell=\"A{fz + 1}\" sqref=\"A{fz + 1}\"/>");
        sb.Append("</sheetView></sheetViews>");
        // Larghezza delle colonne dal contenuto più lungo (titoli e note esclusi: occupano più colonne).
        var maxCols = s.Rows.Count == 0 ? 0 : s.Rows.Max(r => r.Cells.Length);
        if (maxCols > 0)
        {
            sb.Append("<cols>");
            for (var c = 0; c < maxCols; c++)
            {
                var w = s.Rows.Where(r => r.Style is not (STitle or SNote) && c < r.Cells.Length).Select(r => Width(r.Cells[c])).DefaultIfEmpty(8).Max();
                w = Math.Clamp(w + 2, 8, 60);
                sb.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{w.ToString(CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
            }
            sb.Append("</cols>");
        }
        sb.Append("<sheetData>");
        for (var r = 0; r < s.Rows.Count; r++)
        {
            var (style, cells) = s.Rows[r];
            if (cells.Length == 0) continue;
            sb.Append($"<row r=\"{r + 1}\"{(style == STitle ? " ht=\"22\" customHeight=\"1\"" : "")}>");
            for (var c = 0; c < cells.Length; c++)
            {
                var v = cells[c];
                if (v is null) continue;
                var refName = Col(c) + (r + 1);
                var bold = style is SHeader or SBold;
                switch (v)
                {
                    case XPct p: sb.Append($"<c r=\"{refName}\" s=\"{(bold ? XBoldPct : XPctS)}\"><v>{Num(p.Value)}</v></c>"); break;
                    case XEur e: sb.Append($"<c r=\"{refName}\" s=\"{(bold ? XBoldEur : XEurS)}\"><v>{Num((double)e.Value)}</v></c>"); break;
                    case XDate d: sb.Append($"<c r=\"{refName}\" s=\"{XDateS}\"><v>{Num(d.Value.Date.ToOADate())}</v></c>"); break;
                    case DateTime dt: sb.Append($"<c r=\"{refName}\" s=\"{XDateTimeS}\"><v>{Num(dt.ToOADate())}</v></c>"); break;
                    case int or long or short:
                        sb.Append($"<c r=\"{refName}\" s=\"{(bold ? XBoldNum : XNum)}\"><v>{Convert.ToInt64(v)}</v></c>"); break;
                    case decimal or double or float:
                        sb.Append($"<c r=\"{refName}\" s=\"{(bold ? XBoldDec : XDec)}\"><v>{Num(Convert.ToDouble(v))}</v></c>"); break;
                    case bool b: sb.Append($"<c r=\"{refName}\" t=\"inlineStr\" s=\"{style}\"><is><t>{(b ? "sì" : "no")}</t></is></c>"); break;
                    default:
                        sb.Append($"<c r=\"{refName}\" t=\"inlineStr\" s=\"{style}\"><is><t xml:space=\"preserve\">{Esc(v.ToString() ?? "")}</t></is></c>"); break;
                }
            }
            sb.Append("</row>");
        }
        sb.Append("</sheetData>");
        if (s.FilterRow is int f && s.FilterCols > 0)
            sb.Append($"<autoFilter ref=\"{Col(0)}{f + 1}:{Col(s.FilterCols - 1)}{FilterEnd(s) + 1}\"/>");
        sb.Append("<pageMargins left=\"0.5\" right=\"0.5\" top=\"0.6\" bottom=\"0.6\" header=\"0.3\" footer=\"0.3\"/>");
        sb.Append("</worksheet>");
        return sb.ToString();
    }

    private static int Width(object? v) => v switch
    {
        null => 0,
        XPct => 7, XEur e => e.Value.ToString("#,0.00", CultureInfo.InvariantCulture).Length + 3,
        XDate => 10, DateTime => 16,
        string t => Math.Min(t.Split('\n').Max(l => l.Length), 60),
        _ => (v.ToString() ?? "").Length + 1
    };

    private static string Num(double d) =>
        double.IsFinite(d) ? d.ToString("R", CultureInfo.InvariantCulture) : "0";

    public static string Col(int i)
    {
        var s = "";
        for (i++; i > 0; i = (i - 1) / 26) s = (char)('A' + (i - 1) % 26) + s;
        return s;
    }

    /// <summary>Testo sicuro per l'XML: caratteri di controllo tolti, &lt; &gt; &amp; e virgolette protetti.</summary>
    private static string Esc(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
            if (ch is '\t' or '\n' or '\r' || ch >= ' ' && ch != '￾' && ch != '￿') sb.Append(ch);
        var t = SecurityElement.Escape(sb.ToString()) ?? "";
        return t.Length > 32000 ? t[..32000] : t;
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
        w.Write(content);
    }

    // 0 normale · 1 intestazione (grassetto su grigio, a capo) · 2 grassetto · 3 titolo · 4 nota
    // 5 intero · 6 percentuale · 7 euro · 8 data · 9 data e ora · 10 decimale · 11-14 varianti in grassetto
    private const string Styles =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
        "<numFmts count=\"5\">" +
        "<numFmt numFmtId=\"164\" formatCode=\"#,##0.00\\ &quot;€&quot;\"/>" +
        "<numFmt numFmtId=\"165\" formatCode=\"dd/mm/yyyy\"/>" +
        "<numFmt numFmtId=\"166\" formatCode=\"dd/mm/yyyy\\ hh:mm\"/>" +
        "<numFmt numFmtId=\"167\" formatCode=\"0.0%\"/>" +
        "<numFmt numFmtId=\"168\" formatCode=\"#,##0.00##\"/>" +
        "</numFmts>" +
        "<fonts count=\"4\">" +
        "<font><sz val=\"11\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
        "<font><b/><sz val=\"11\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
        "<font><b/><sz val=\"14\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
        "<font><i/><sz val=\"10\"/><color rgb=\"FF666666\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
        "</fonts>" +
        "<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>" +
        "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFEDEAE4\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
        "<borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border>" +
        "<border><left/><right/><top/><bottom style=\"thin\"><color rgb=\"FFBBBBBB\"/></bottom><diagonal/></border></borders>" +
        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
        "<cellXfs count=\"15\">" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\" wrapText=\"1\"/></xf>" +
        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
        "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
        "<xf numFmtId=\"0\" fontId=\"3\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
        "<xf numFmtId=\"3\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
        "<xf numFmtId=\"167\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
        "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
        "<xf numFmtId=\"165\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
        "<xf numFmtId=\"166\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
        "<xf numFmtId=\"168\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
        "<xf numFmtId=\"3\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\"/>" +
        "<xf numFmtId=\"167\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\"/>" +
        "<xf numFmtId=\"164\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\"/>" +
        "<xf numFmtId=\"168\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\"/>" +
        "</cellXfs>" +
        "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
        "</styleSheet>";
}
