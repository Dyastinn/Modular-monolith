using System.Globalization;

namespace Yasda.Web.Shared;

public static class Format
{
    private static readonly CultureInfo Ph = CultureInfo.GetCultureInfo("en-PH");

    public static string Date(DateTime? utc) => utc is { } d ? d.ToLocalTime().ToString("MMM d, yyyy h:mm tt", Ph) : "";

    // "TeamLead" -> "Team lead"
    public static string Words(string pascal) =>
        string.Concat(pascal.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : c.ToString()));
}
