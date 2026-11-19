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
