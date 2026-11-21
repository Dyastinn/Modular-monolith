using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Yasda.Modules.Sla.Features;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Integration.Tests;

// The calendar is one row an Admin edits. Every test puts back what it changed, because all tests share one database.
[Collection("Integration")]
public class SlaCalendarTests(CustomWebAppFactory factory)
{
    private const string Admin = "admin@yasda.local", Lead = "lead@yasda.local", Agent = "agent@yasda.local", Requester = "requester@yasda.local";

    private sealed record Day(string DayOfWeek, string? Opens, string? Closes);
    private sealed record Holiday(string HolidayDate, string Name);
    private sealed record Calendar(string TimeZoneId, string UpdatedByName, List<Day> Days, List<Holiday> Holidays);

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        return problem.GetProperty("code").GetString();
    }

    private static async Task<Calendar> ReadAsync(HttpClient client)
    {
        return (await client.GetFromJsonAsync<Calendar>("/api/sla/calendar"))!;
    }

    private static async Task SetSaturdayAsync(HttpClient admin, string? opens, string? closes)
    {
        var response = await admin.PutAsJsonAsync("/api/sla/calendar",
            new { timeZoneId = "Asia/Manila", days = new[] { new { day = "Saturday", opens, closes } } });
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Staff_read_the_calendar_the_installation_starts_with()
    {
        var agent = await factory.ClientFor(Agent);

        var calendar = await ReadAsync(agent);

        calendar.TimeZoneId.Should().Be("Asia/Manila");
        calendar.Days.Select(d => d.DayOfWeek).Should().Equal("Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday");
        calendar.Days[0].Should().Be(new Day("Monday", "08:00:00", "17:00:00"));
        calendar.Days[5].Should().Be(new Day("Saturday", null, null));
        calendar.Holidays.Should().BeEmpty();
    }

    [Fact]
    public async Task A_requester_cant_read_the_calendar_and_only_an_admin_can_change_it()
    {
        var requester = await factory.ClientFor(Requester);
        var lead = await factory.ClientFor(Lead);
        var agent = await factory.ClientFor(Agent);

        (await requester.GetAsync("/api/sla/calendar")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        foreach (var client in new[] { lead, agent, requester })
        {
            var put = await client.PutAsJsonAsync("/api/sla/calendar", new { timeZoneId = "Asia/Manila", days = Array.Empty<object>() });
            var post = await client.PostAsJsonAsync("/api/sla/calendar/holidays", new { date = "2026-12-25", name = "Christmas Day" });
            var delete = await client.DeleteAsync("/api/sla/calendar/holidays/2026-12-25");

            put.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            post.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            delete.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task An_admin_changes_one_day_and_the_others_keep_their_hours()
    {
        var admin = await factory.ClientFor(Admin);
        try
        {
            await SetSaturdayAsync(admin, "09:00", "12:00");

            var calendar = await ReadAsync(admin);

            calendar.Days.Single(d => d.DayOfWeek == "Saturday").Should().Be(new Day("Saturday", "09:00:00", "12:00:00"));
            calendar.Days.Single(d => d.DayOfWeek == "Monday").Closes.Should().Be("17:00:00");
            calendar.UpdatedByName.Should().NotBe("Installation default");
        }
        finally
        {
            await SetSaturdayAsync(admin, null, null);
        }
    }

    [Fact]
    public async Task A_calendar_that_makes_no_sense_is_refused_with_its_code()
    {
        var admin = await factory.ClientFor(Admin);
        var closeEveryDay = Enum.GetNames<DayOfWeek>().Select(d => new { day = d, opens = (string?)null, closes = (string?)null }).ToArray();

        var unknownZone = await admin.PutAsJsonAsync("/api/sla/calendar", new { timeZoneId = "Mars/Olympus", days = Array.Empty<object>() });
        var noOpenDay = await admin.PutAsJsonAsync("/api/sla/calendar", new { timeZoneId = "Asia/Manila", days = closeEveryDay });
        var backwards = await admin.PutAsJsonAsync("/api/sla/calendar",
            new { timeZoneId = "Asia/Manila", days = new[] { new { day = "Monday", opens = "17:00", closes = "08:00" } } });
        var twice = await admin.PutAsJsonAsync("/api/sla/calendar",
            new { timeZoneId = "Asia/Manila", days = new[] { new { day = "Monday", opens = "08:00", closes = "09:00" }, new { day = "Monday", opens = "09:00", closes = "10:00" } } });

        unknownZone.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeOf(unknownZone)).Should().Be("SLA-0005");
        (await CodeOf(noOpenDay)).Should().Be("SLA-0003");
        (await CodeOf(backwards)).Should().Be("SLA-0004");
        (await CodeOf(twice)).Should().Be("SLA-0006");
        (await ReadAsync(admin)).Days[0].Opens.Should().Be("08:00:00", "a refused change changes nothing");
    }

    [Fact]
    public async Task A_holiday_is_added_listed_and_removed_and_a_date_is_a_holiday_only_once()
    {
        var admin = await factory.ClientFor(Admin);
        try
        {
            (await admin.PostAsJsonAsync("/api/sla/calendar/holidays", new { date = "2026-12-25", name = "Christmas Day" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
            await admin.PostAsJsonAsync("/api/sla/calendar/holidays", new { date = "2026-12-24", name = "Christmas Eve" });

            var again = await admin.PostAsJsonAsync("/api/sla/calendar/holidays", new { date = "2026-12-25", name = "Again" });
            var unnamed = await admin.PostAsJsonAsync("/api/sla/calendar/holidays", new { date = "2026-12-26", name = " " });

            (await CodeOf(again)).Should().Be("SLA-0008");
            (await CodeOf(unnamed)).Should().Be("SLA-0007");
            (await ReadAsync(admin)).Holidays.Should().Equal(new Holiday("2026-12-24", "Christmas Eve"), new Holiday("2026-12-25", "Christmas Day"));
        }
        finally
        {
            (await admin.DeleteAsync("/api/sla/calendar/holidays/2026-12-25")).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await admin.DeleteAsync("/api/sla/calendar/holidays/2026-12-24")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await ReadAsync(admin)).Holidays.Should().BeEmpty();
        (await admin.DeleteAsync("/api/sla/calendar/holidays/2026-12-25")).StatusCode.Should().Be(HttpStatusCode.NoContent, "removing a date that isn't a holiday is not an error");
    }

    [Fact]
    public async Task Saving_a_new_time_zone_changes_what_today_means_at_once_and_a_restart_loads_it_back()
    {
        var admin = await factory.ClientFor(Admin);
        var zone = factory.Services.GetRequiredService<CompanyZone>();
        try
        {
            var response = await admin.PutAsJsonAsync("/api/sla/calendar", new { timeZoneId = "America/New_York", days = Array.Empty<object>() });
            response.EnsureSuccessStatusCode();
            zone.Current.Id.Should().Be("America/New_York");

            // A restart: the holder starts as the default, and the loader reads the saved zone.
            zone.Change(TimeZoneInfo.Utc);
            var loader = new LoadCompanyZone(factory.Services.GetRequiredService<IDbConnectionFactory>(), zone);
            await loader.StartAsync(CancellationToken.None);

            zone.Current.Id.Should().Be("America/New_York");
        }
        finally
        {
            var back = await admin.PutAsJsonAsync("/api/sla/calendar", new { timeZoneId = "Asia/Manila", days = Array.Empty<object>() });
            back.EnsureSuccessStatusCode();
        }

        zone.Current.Id.Should().Be("Asia/Manila");
    }
}
