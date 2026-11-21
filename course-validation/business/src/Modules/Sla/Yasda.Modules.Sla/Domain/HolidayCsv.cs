using System.Globalization;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Sla.Domain;

// Turns the text of a CSV file into holidays. One holiday per line: a date as 2026-12-25, a comma, and the name. A first line
// that reads "date,name" is a header and is skipped, and so are blank lines. A line that can't be read refuses the whole file,
// with its line number, so nothing is half imported.
internal static class HolidayCsv
{
    public static List<PresetHoliday> Parse(string csv)
    {
        var holidays = new List<PresetHoliday>();
        var lines = csv.Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var number = index + 1;
            var line = lines[index].Trim();
            if (line.Length == 0) continue;
            if (number == 1 && IsHeader(line)) continue;

            var comma = line.IndexOf(',');
            if (comma < 0) throw LineProblem(number, "it needs a date, a comma and a name");

            var dateText = line[..comma].Trim();
            var name = line[(comma + 1)..].Trim().Trim('"');
            if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw LineProblem(number, $"'{dateText}' is not a date written like 2026-12-25");
            if (name.Length == 0) throw LineProblem(number, "the holiday has no name");

            holidays.Add(new PresetHoliday(date, name));
        }

        return holidays;
    }

    private static bool IsHeader(string line)
    {
        var lower = line.ToLowerInvariant().Replace(" ", "");
        return lower == "date,name";
    }

    private static DomainException LineProblem(int number, string why)
    {
        return new DomainException(SlaErrors.HolidayLineInvalid, $"Line {number}: {why}.");
    }
}
