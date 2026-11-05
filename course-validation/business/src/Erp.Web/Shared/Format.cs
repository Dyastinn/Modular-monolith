using System.Globalization;

namespace Erp.Web.Shared;

public static class Format
{
    private static readonly CultureInfo Ph = CultureInfo.GetCultureInfo("en-PH");

    public static string Peso(decimal amount) => "₱" + amount.ToString("N2", Ph);
    public static string Qty(decimal quantity) => quantity.ToString("0.###", Ph);
    public static string Date(DateOnly date) => date.ToString("MMM d, yyyy", Ph);
    public static string Date(DateTime? utc) => utc is { } d ? d.ToLocalTime().ToString("MMM d, yyyy h:mm tt", Ph) : "";

    // "PendingApproval" -> "Pending approval"
    public static string Words(string pascal) =>
        string.Concat(pascal.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLower(c) : c.ToString()));
}
