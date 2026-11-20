using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;

namespace Yasda.Integration.Tests;

// Every list sorts on the server, by an allow-listed column; the SQL never builds an ORDER BY from the text.
[Collection("Integration")]
public class ListSortingTests(CustomWebAppFactory factory)
{
    private sealed record UserRow(long Id, string FullName, string Email);
    private sealed record TicketRow(long Id, string Priority);
    private sealed record Page<T>(List<T> Items, int TotalCount);

    [Fact]
    public async Task Users_sort_by_email_both_ways()
    {
        var admin = await factory.ClientFor("admin@yasda.local");

        var up = (await admin.GetFromJsonAsync<Page<UserRow>>("/api/users?sort=email&pageSize=100"))!.Items;
        var down = (await admin.GetFromJsonAsync<Page<UserRow>>("/api/users?sort=email&desc=true&pageSize=100"))!.Items;

        up.Should().HaveCountGreaterThan(1);
        up.Select(r => r.Email).Should().BeInAscendingOrder(StringComparer.Ordinal);
        down.Select(r => r.Email).Should().BeInDescendingOrder(StringComparer.Ordinal);
    }

    // Priority sorts by urgency, not by spelling: Critical > High > Normal > Low.
    [Fact]
    public async Task Tickets_sort_by_urgency_not_alphabetically()
    {
        var agent = await factory.ClientFor("agent@yasda.local");

        var rows = (await agent.GetFromJsonAsync<Page<TicketRow>>("/api/tickets?sort=priority&desc=true&pageSize=100"))!.Items;

        string[] order = ["Critical", "High", "Normal", "Low"];
        rows.Select(r => Array.IndexOf(order, r.Priority)).Should().BeInAscendingOrder();
    }

    [Theory]
    [InlineData("/api/users?sort=email;DROP TABLE identity.users")]
    [InlineData("/api/users?sort=Email")]            // the names are exact, the way the JSON writes them
    [InlineData("/api/users?sort=password")]         // not a column the list sorts by
    [InlineData("/api/tickets?sort=requesterId")]
    [InlineData("/api/tickets?page=0")]
    [InlineData("/api/tickets?pageSize=101")]
    [InlineData("/api/tickets?status=open")]          // statuses are exact names too
    public async Task A_sort_the_list_does_not_offer_is_a_400(string url)
    {
        var admin = await factory.ClientFor("admin@yasda.local");
        (await admin.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("/api/users?sort=fullName&desc=true")]
    [InlineData("/api/users?sort=role")]
    [InlineData("/api/users?sort=lastSignIn&desc=true")]
    [InlineData("/api/tickets?sort=priority&desc=true")]
    [InlineData("/api/tickets?sort=assigneeName")]
    [InlineData("/api/tickets?sort=requesterName&desc=true")]
    [InlineData("/api/tickets?sort=updatedUtc&status=Open&assigned=unassigned")]
    [InlineData("/api/teams?sort=name")]
    [InlineData("/api/teams?sort=members&desc=true")]
    [InlineData("/api/tickets?sort=nextDue")]
    [InlineData("/api/tickets?sort=nextDue&desc=true")]
    public async Task Every_offered_sort_runs(string url)
    {
        var admin = await factory.ClientFor("admin@yasda.local");
        (await admin.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
