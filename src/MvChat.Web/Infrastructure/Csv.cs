namespace MvChat.Web.Infrastructure;

/// <summary>Celle dei file per Excel. Un testo che inizia con = + - @ verrebbe preso da Excel come formula: si neutralizza con un apice.</summary>
public static class Csv
{
    public static string Cell(string? v)
    {
        var s = v ?? "";
        if (s.Length > 0 && "=+-@\t\r".Contains(s[0])) s = "'" + s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
