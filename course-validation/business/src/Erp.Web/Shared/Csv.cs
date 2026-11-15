using System.Globalization;
using System.Text;

namespace Erp.Web.Shared;

// Reads a CSV file the way spreadsheets write it (RFC 4180): commas, quoted fields with commas, quotes ("") and line
// breaks inside, Windows or Unix line endings, and a UTF-8 byte-order mark. The first row names the columns.
public static class Csv
{
    public static List<Dictionary<string, string>> Read(string text)
    {
        var rows = Parse(text.TrimStart('﻿'));
        if (rows.Count == 0) return [];
        var header = rows[0].Select(h => h.Trim()).ToList();
        return rows.Skip(1)
            .Where(r => r.Any(c => c.Trim().Length > 0))   // a blank line at the end isn't a row
            .Select(r => header.Select((h, i) => (h, v: i < r.Count ? r[i].Trim() : ""))
                .ToDictionary(x => x.h, x => x.v, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    // A header line for a template file.
    public static string Header(params string[] columns) => string.Join(",", columns) + "\r\n";

    // Numbers as a spreadsheet may write them: "1,250.50" or "₱1,250.50".
    public static bool TryDecimal(string text, out decimal value) =>
        decimal.TryParse(text.Replace("₱", "").Replace(",", "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    public static bool TryBool(string text, bool fallback, out bool value)
    {
        var t = text.Trim().ToLowerInvariant();
        value = t switch { "" => fallback, "yes" or "y" or "true" or "1" => true, "no" or "n" or "false" or "0" => false, _ => fallback };
        return t is "" or "yes" or "y" or "true" or "1" or "no" or "n" or "false" or "0";
    }

    public static bool TryDate(string text, out DateOnly value) =>
        DateOnly.TryParseExact(text.Trim(), ["yyyy-MM-dd", "M/d/yyyy", "MM/dd/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

    private static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
            }
            else if (c == '"' && field.Length == 0) quoted = true;
            else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString()); field.Clear();
                rows.Add(row); row = [];
            }
            else field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
        return rows;
    }
}
