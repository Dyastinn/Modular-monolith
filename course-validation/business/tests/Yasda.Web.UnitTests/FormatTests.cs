using AwesomeAssertions;
using Yasda.Web.Shared;

namespace Yasda.Web.UnitTests;

// The words the screens show for times. Format.Ago works in the computer's own time zone ("Yesterday" means the local
// day), so these start from noon in that zone, which is the same calendar day for everyone running them.
public class FormatTests
{
    private static readonly DateTime NoonToday = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Local).ToUniversalTime();

    [Fact]
    public void Under_a_minute_ago_is_just_now()
    {
        Format.Ago(NoonToday.AddSeconds(-30), NoonToday).Should().Be("Just now");
    }

    [Fact]
    public void A_time_a_little_ahead_of_our_clock_is_also_just_now()
    {
        Format.Ago(NoonToday.AddSeconds(20), NoonToday).Should().Be("Just now");
    }

    [Fact]
    public void Within_the_hour_it_is_minutes()
    {
        Format.Ago(NoonToday.AddMinutes(-12), NoonToday).Should().Be("12m");
        Format.Ago(NoonToday.AddMinutes(-59), NoonToday).Should().Be("59m");
    }

    [Fact]
    public void Earlier_the_same_day_it_is_hours()
    {
        Format.Ago(NoonToday.AddHours(-3), NoonToday).Should().Be("3h");
    }

    [Fact]
    public void The_day_before_it_is_yesterday_whatever_the_hour()
    {
        Format.Ago(NoonToday.AddHours(-20), NoonToday).Should().Be("Yesterday");
    }

    [Fact]
    public void Earlier_this_year_it_is_the_month_and_day()
    {
        Format.Ago(NoonToday.AddDays(-5), NoonToday).Should().Be("Nov 15");
    }

    [Fact]
    public void In_an_earlier_year_it_includes_the_year()
    {
        Format.Ago(NoonToday.AddYears(-1), NoonToday).Should().Be("Nov 20, 2025");
    }

    [Fact]
    public void An_unknown_date_is_empty()
    {
        Format.Date(null).Should().BeEmpty();
    }

    [Theory]
    [InlineData(40, "40m")]
    [InlineData(59, "59m")]
    [InlineData(60, "1h")]
    [InlineData(70, "1h 10m")]
    [InlineData(360, "6h")]
    [InlineData(1440, "1d")]
    [InlineData(1500, "1d 1h")]
    [InlineData(3060, "2d 3h")]
    public void A_span_shows_the_two_largest_units_that_matter(int minutes, string expected)
    {
        Format.Span(TimeSpan.FromMinutes(minutes)).Should().Be(expected);
    }

    [Fact]
    public void A_paused_clock_says_paused_whatever_the_times_are()
    {
        var view = Format.Sla(NoonToday.AddHours(-3), NoonToday.AddHours(-5), NoonToday, paused: true);

        view.State.Should().Be("paused");
        view.Text.Should().Be("Paused");
    }

    [Fact]
    public void Nothing_running_says_nothing()
    {
        var view = Format.Sla(null, null, NoonToday);

        view.State.Should().Be("none");
        view.Text.Should().BeEmpty();
    }

    [Fact]
    public void A_deadline_past_is_breached_and_says_for_how_long()
    {
        var view = Format.Sla(NoonToday.AddMinutes(-40), NoonToday.AddHours(-2), NoonToday);

        view.State.Should().Be("breached");
        view.Text.Should().Be("Breached 40m ago");
    }

    [Fact]
    public void A_deadline_that_passed_seconds_ago_is_breached_just_now()
    {
        var view = Format.Sla(NoonToday.AddSeconds(-20), NoonToday.AddHours(-2), NoonToday);

        view.State.Should().Be("breached");
        view.Text.Should().Be("Breached just now");
    }

    [Fact]
    public void A_deadline_exactly_now_is_not_breached_yet()
    {
        var view = Format.Sla(NoonToday, NoonToday.AddHours(-1), NoonToday);

        view.State.Should().Be("risk");
        view.Text.Should().Be("Under 1m left");
    }

    [Fact]
    public void From_the_at_risk_point_on_it_is_at_risk_and_says_how_long_is_left()
    {
        var view = Format.Sla(NoonToday.AddMinutes(70), NoonToday, NoonToday);

        view.State.Should().Be("risk");
        view.Text.Should().Be("1h 10m left");
    }

    [Fact]
    public void A_minute_before_the_at_risk_point_it_is_still_fine()
    {
        var view = Format.Sla(NoonToday.AddHours(6), NoonToday.AddMinutes(1), NoonToday);

        view.State.Should().Be("ok");
        view.Text.Should().Be("6h left");
    }

    [Fact]
    public void A_deadline_with_no_at_risk_point_is_fine_until_it_is_breached()
    {
        var view = Format.Sla(NoonToday.AddMinutes(5), null, NoonToday);

        view.State.Should().Be("ok");
    }
}
