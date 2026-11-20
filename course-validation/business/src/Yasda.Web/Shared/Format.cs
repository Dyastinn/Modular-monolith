using System.Globalization;

namespace Yasda.Web.Shared;

public static class Format
{
    private static readonly CultureInfo Ph = CultureInfo.GetCultureInfo("en-PH");

    public static string Date(DateTime? utc)
    {
        if (utc is null) return "";
        return utc.Value.ToLocalTime().ToString("MMM d, yyyy h:mm tt", Ph);
    }

    // The machine-readable form a <time datetime="..."> wants: 2026-11-20T09:30:00Z.
    public static string Iso(DateTime utc) => utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    // "Just now", "12m", "3h", "Yesterday", "Nov 3", "Nov 3, 2025": how long ago, the way people say it.
    public static string Ago(DateTime utc, DateTime nowUtc)
    {
        var since = nowUtc - utc;
        if (since < TimeSpan.FromMinutes(1)) return "Just now";   // also a clock a little ahead of ours
        if (since < TimeSpan.FromHours(1)) return $"{(int)since.TotalMinutes}m";

        var then = utc.ToLocalTime();
        var today = nowUtc.ToLocalTime().Date;
        if (then.Date == today) return $"{(int)since.TotalHours}h";
        if (then.Date == today.AddDays(-1)) return "Yesterday";
        return then.ToString(then.Year == today.Year ? "MMM d" : "MMM d, yyyy", Ph);
    }

    // "TeamLead" -> "Team lead": a space before each capital after the first, and that capital in lower case.
    public static string Words(string pascal)
    {
        var words = new System.Text.StringBuilder();
        for (var i = 0; i < pascal.Length; i++)
        {
            var letter = pascal[i];
            if (i > 0 && char.IsUpper(letter))
                words.Append(' ').Append(char.ToLowerInvariant(letter));
            else
                words.Append(letter);
        }
        return words.ToString();
    }
}
