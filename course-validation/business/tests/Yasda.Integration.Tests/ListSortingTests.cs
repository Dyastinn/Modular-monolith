using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;

namespace Yasda.Integration.Tests;

// Every list sorts on the server, by an allow-listed column; the SQL never builds an ORDER BY from the text.
[Collection("Integration")]
public class ListSortingTests(CustomWebAppFactory factory)
{
    private sealed record UserRow(long Id, string FullName, string Email);
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

    [Theory]
    [InlineData("/api/users?sort=email;DROP TABLE identity.users")]
    [InlineData("/api/users?sort=Email")]            // the names are exact, the way the JSON writes them
    [InlineData("/api/users?sort=password")]         // not a column the list sorts by
    public async Task A_sort_the_list_does_not_offer_is_a_400(string url)
    {
        var admin = await factory.ClientFor("admin@yasda.local");
        (await admin.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("/api/users?sort=fullName&desc=true")]
    [InlineData("/api/users?sort=role")]
    [InlineData("/api/users?sort=lastSignIn&desc=true")]
    public async Task Every_offered_sort_runs(string url)
    {
        var admin = await factory.ClientFor("admin@yasda.local");
        (await admin.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
