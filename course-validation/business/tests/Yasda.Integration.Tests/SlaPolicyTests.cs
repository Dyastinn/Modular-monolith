using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;

namespace Yasda.Integration.Tests;

// The SLA policies are rows a team lead edits. The tests change the Low policy and always put it back, because every test
// shares one database and the others count on the numbers they started with.
[Collection("Integration")]
public class SlaPolicyTests(CustomWebAppFactory factory)
{
    private const string Requester = "requester@yasda.local", Agent = "agent@yasda.local", Lead = "lead@yasda.local";

    private sealed record Policy(string Priority, int RespondMinutes, int ResolveMinutes, DateTime UpdatedUtc, long UpdatedById, string UpdatedByName);
    private sealed record Me(long Id, string FullName);
    private sealed record Created(long Id);
    private sealed record Deadlines(DateTime ResponseDueUtc, DateTime ResolveDueUtc);
    private sealed record TicketView(DateTime CreatedUtc, Deadlines? Sla);

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        return problem.GetProperty("code").GetString();
    }

    private static async Task<Policy> LowOf(HttpClient client)
    {
        var all = await client.GetFromJsonAsync<List<Policy>>("/api/sla/policies");
        return all!.Single(p => p.Priority == "Low");
    }

    private static async Task SetLow(HttpClient lead, int respondMinutes, int resolveMinutes)
    {
        var response = await lead.PutAsJsonAsync("/api/sla/policies/Low", new { respondMinutes, resolveMinutes });
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Staff_read_the_four_policies_most_urgent_first()
    {
        var agent = await factory.ClientFor(Agent);

        var policies = (await agent.GetFromJsonAsync<List<Policy>>("/api/sla/policies"))!;

        policies.Select(p => p.Priority).Should().Equal("Critical", "High", "Normal", "Low");
        policies[0].RespondMinutes.Should().Be(60);
        policies[0].ResolveMinutes.Should().Be(240);
        policies[3].ResolveMinutes.Should().Be(7200);
    }

    [Fact]
    public async Task A_requester_cant_read_the_policies()
    {
        var requester = await factory.ClientFor(Requester);

        var response = await requester.GetAsync("/api/sla/policies");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_agent_cant_change_a_policy()
    {
        var agent = await factory.ClientFor(Agent);

        var response = await agent.PutAsJsonAsync("/api/sla/policies/Low", new { respondMinutes = 10, resolveMinutes = 20 });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_team_lead_changes_a_policy_and_it_says_who_and_when()
    {
        var lead = await factory.ClientFor(Lead);
        var me = await lead.GetFromJsonAsync<Me>("/api/auth/me");
        var before = await LowOf(lead);
        var started = DateTime.UtcNow.AddSeconds(-5);
        try
        {
            await SetLow(lead, 1500, 7500);

            var changed = await LowOf(lead);
            changed.RespondMinutes.Should().Be(1500);
            changed.ResolveMinutes.Should().Be(7500);
            changed.UpdatedById.Should().Be(me!.Id);
            changed.UpdatedByName.Should().Be(me.FullName);
            changed.UpdatedUtc.Should().BeAfter(started);
        }
        finally
        {
            await SetLow(lead, before.RespondMinutes, before.ResolveMinutes);
        }
    }

    [Theory]
    [InlineData(0, 100, "SLA-0001")]
    [InlineData(-5, 100, "SLA-0001")]
    [InlineData(100, 99, "SLA-0002")]
    public async Task A_policy_that_makes_no_sense_is_refused_with_its_code(int respondMinutes, int resolveMinutes, string code)
    {
        var lead = await factory.ClientFor(Lead);
        var before = await LowOf(lead);

        var response = await lead.PutAsJsonAsync("/api/sla/policies/Low", new { respondMinutes, resolveMinutes });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeOf(response)).Should().Be(code);
        var after = await LowOf(lead);
        after.Should().Be(before, "a refused change leaves the policy as it was");
    }

    [Fact]
    public async Task A_target_longer_than_a_year_is_refused_before_the_handler()
    {
        var lead = await factory.ClientFor(Lead);

        var response = await lead.PutAsJsonAsync("/api/sla/policies/Low", new { respondMinutes = 600_000, resolveMinutes = 700_000 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_priority_nobody_has_a_policy_for_is_404()
    {
        var lead = await factory.ClientFor(Lead);

        var response = await lead.PutAsJsonAsync("/api/sla/policies/Urgent", new { respondMinutes = 10, resolveMinutes = 20 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Decision D5: an edited policy is for tickets filed after it. A ticket filed before keeps the numbers it started with.
    [Fact]
    public async Task A_changed_policy_applies_to_new_tickets_only()
    {
        var lead = await factory.ClientFor(Lead);
        var requester = await factory.ClientFor(Requester);
        var before = await LowOf(lead);
        var older = await FileLow(requester);
        await ReadDeadlines(lead, older);
        try
        {
            await SetLow(lead, 600, 3000);
            var newer = await FileLow(requester);

            var olderTicket = await ReadDeadlines(lead, older);
            var newerTicket = await ReadDeadlines(lead, newer);

            olderTicket.Sla!.ResponseDueUtc.Should().Be(olderTicket.CreatedUtc.AddHours(24));
            newerTicket.Sla!.ResponseDueUtc.Should().Be(newerTicket.CreatedUtc.AddMinutes(600));
            newerTicket.Sla.ResolveDueUtc.Should().Be(newerTicket.CreatedUtc.AddMinutes(3000));
        }
        finally
        {
            await SetLow(lead, before.RespondMinutes, before.ResolveMinutes);
        }
    }

    private static async Task<long> FileLow(HttpClient requester)
    {
        var response = await requester.PostAsJsonAsync("/api/tickets",
            new { subject = $"Policy {Guid.NewGuid():N}", message = "Please help.", priority = "Low" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<Created>())!.Id;
    }

    private static async Task<TicketView> ReadDeadlines(HttpClient staff, long id)
    {
        var ticket = await Waiting.Eventually(() => staff.GetFromJsonAsync<TicketView>($"/api/tickets/{id}")!, t => t!.Sla is not null);
        return ticket!;
    }
}
