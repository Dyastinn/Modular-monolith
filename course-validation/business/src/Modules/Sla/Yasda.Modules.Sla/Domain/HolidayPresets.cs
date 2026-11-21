namespace Yasda.Modules.Sla.Domain;

internal sealed record PresetHoliday(DateOnly Date, string Name);

// A country's holidays, ready to load into the calendar with one click. These come from the proclamations themselves, not from
// memory: for the Philippines, Proclamation No. 1006 (2026) and Proclamation No. 1427 (2027), both on https://lawphil.net.
//
// Left out on purpose, so an Admin adds them once they apply to the installation:
//   - Eid'l Fitr and Eid'l Adha: the proclamations say their dates are set later, by separate proclamations.
//   - Holidays that apply to one region only (such as the Metro Manila days of 16-18 November 2026).
//   - The "special working day" of 25 February: it is a normal working day.
internal sealed record HolidayPreset(string Code, string Country, IReadOnlyList<PresetHoliday> Holidays)
{
    private static readonly HolidayPreset Philippines = new("PH", "Philippines",
    [
        new(new DateOnly(2026, 1, 1), "New Year's Day"),
        new(new DateOnly(2026, 2, 17), "Chinese New Year"),
        new(new DateOnly(2026, 4, 2), "Maundy Thursday"),
        new(new DateOnly(2026, 4, 3), "Good Friday"),
        new(new DateOnly(2026, 4, 4), "Black Saturday"),
        new(new DateOnly(2026, 4, 9), "Araw ng Kagitingan"),
        new(new DateOnly(2026, 5, 1), "Labor Day"),
        new(new DateOnly(2026, 6, 12), "Independence Day"),
        new(new DateOnly(2026, 8, 21), "Ninoy Aquino Day"),
        new(new DateOnly(2026, 8, 31), "National Heroes Day"),
        new(new DateOnly(2026, 11, 1), "All Saints' Day"),
        new(new DateOnly(2026, 11, 2), "All Souls' Day"),
        new(new DateOnly(2026, 11, 30), "Bonifacio Day"),
        new(new DateOnly(2026, 12, 8), "Feast of the Immaculate Conception of Mary"),
        new(new DateOnly(2026, 12, 24), "Christmas Eve"),
        new(new DateOnly(2026, 12, 25), "Christmas Day"),
        new(new DateOnly(2026, 12, 30), "Rizal Day"),
        new(new DateOnly(2026, 12, 31), "Last Day of the Year"),

        new(new DateOnly(2027, 1, 1), "New Year's Day"),
        new(new DateOnly(2027, 2, 6), "Chinese New Year"),
        new(new DateOnly(2027, 3, 25), "Maundy Thursday"),
        new(new DateOnly(2027, 3, 26), "Good Friday"),
        new(new DateOnly(2027, 3, 27), "Black Saturday"),
        new(new DateOnly(2027, 4, 9), "Araw ng Kagitingan"),
        new(new DateOnly(2027, 5, 1), "Labor Day"),
        new(new DateOnly(2027, 6, 12), "Independence Day"),
        new(new DateOnly(2027, 8, 21), "Ninoy Aquino Day"),
        new(new DateOnly(2027, 8, 30), "National Heroes Day"),
        new(new DateOnly(2027, 11, 1), "All Saints' Day"),
        new(new DateOnly(2027, 11, 2), "All Souls' Day"),
        new(new DateOnly(2027, 11, 30), "Bonifacio Day"),
        new(new DateOnly(2027, 12, 8), "Feast of the Immaculate Conception of Mary"),
        new(new DateOnly(2027, 12, 24), "Christmas Eve"),
        new(new DateOnly(2027, 12, 25), "Christmas Day"),
        new(new DateOnly(2027, 12, 30), "Rizal Day"),
        new(new DateOnly(2027, 12, 31), "Last Day of the Year"),
    ]);

    // After the lists above it: static fields are filled in the order they are written.
    public static readonly IReadOnlyList<HolidayPreset> All = [Philippines];

    public static HolidayPreset? Find(string code)
    {
        foreach (var preset in All)
        {
            if (string.Equals(preset.Code, code, StringComparison.OrdinalIgnoreCase)) return preset;
        }
        return null;
    }
}
