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

    // An SLA deadline as DESIGN.md 5.2 says it: "Breached 40m ago", "1h 10m left" or "6h left", and which of the three it is
    // (the screens color the first two, and mark their rows). The server decides when a ticket is at risk (Sla's rule);
    // this only compares those two times with now. No deadline running (a finished ticket) says nothing.
    public static SlaView Sla(DateTime? nextDueUtc, DateTime? atRiskFromUtc, DateTime nowUtc)
    {
        if (nextDueUtc is null) return new SlaView("", "none");

        if (nowUtc > nextDueUtc)
        {
            var late = nowUtc - nextDueUtc.Value;
            if (late < TimeSpan.FromMinutes(1)) return new SlaView("Breached just now", "breached");
            return new SlaView($"Breached {Span(late)} ago", "breached");
        }

        var left = nextDueUtc.Value - nowUtc;
        var text = "Under 1m left";
        if (left >= TimeSpan.FromMinutes(1)) text = $"{Span(left)} left";

        if (atRiskFromUtc is not null && nowUtc >= atRiskFromUtc) return new SlaView(text, "risk");
        return new SlaView(text, "ok");
    }

    // "40m", "1h 10m", "6h", "2d 3h": the two largest units that matter, nothing smaller.
    public static string Span(TimeSpan span)
    {
        var days = (int)span.TotalDays;
        var hours = span.Hours;
        var minutes = span.Minutes;

        if (days > 0)
        {
            if (hours == 0) return $"{days}d";
            return $"{days}d {hours}h";
        }
        if (span.TotalHours >= 1)
        {
            if (minutes == 0) return $"{(int)span.TotalHours}h";
            return $"{(int)span.TotalHours}h {minutes}m";
        }
        return $"{(int)span.TotalMinutes}m";
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

// An SLA deadline in words, and its state: "none", "ok", "risk" or "breached".
public sealed record SlaView(string Text, string State);
