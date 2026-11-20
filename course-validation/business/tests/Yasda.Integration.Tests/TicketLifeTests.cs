using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Integration.Tests;

// A ticket's life after it's filed (wait, resolve, reopen, close, cancel), through the real API and a real database:
// who may take each step, and what the ticket says when a step isn't allowed.
[Collection("Integration")]
public class TicketLifeTests(CustomWebAppFactory factory)
{
    private const string Requester = "requester@yasda.local", Agent = "agent@yasda.local";

    private sealed record Created(long Id);
    private sealed record TicketView(string Status, string? CancelReason);

    private static async Task<long> FileOne(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/tickets", new { subject = "Printer won't print", message = "It says paper jam.", priority = "High" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<Created>();
        return created!.Id;
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        return problem.GetProperty("code").GetString();
    }

    private static async Task<string> StatusOf(HttpClient client, long id)
    {
        var ticket = await client.GetFromJsonAsync<TicketView>($"/api/tickets/{id}");
        return ticket!.Status;
    }

    [Fact]
    public async Task A_ticket_goes_from_new_to_closed_and_then_refuses_every_step()
    {
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);
        var id = await FileOne(requester);

        (await agent.PostAsync($"/api/tickets/{id}/take", null)).EnsureSuccessStatusCode();
        (await StatusOf(agent, id)).Should().Be("Open");
        (await agent.PostAsync($"/api/tickets/{id}/pending", null)).EnsureSuccessStatusCode();
        (await StatusOf(agent, id)).Should().Be("Pending");
        (await agent.PostAsync($"/api/tickets/{id}/resolve", null)).EnsureSuccessStatusCode();
        (await StatusOf(agent, id)).Should().Be("Resolved");
        (await requester.PostAsync($"/api/tickets/{id}/reopen", null)).EnsureSuccessStatusCode();
        (await StatusOf(agent, id)).Should().Be("Open");
        (await agent.PostAsync($"/api/tickets/{id}/resolve", null)).EnsureSuccessStatusCode();
        (await agent.PostAsync($"/api/tickets/{id}/close", null)).EnsureSuccessStatusCode();
        (await StatusOf(agent, id)).Should().Be("Closed");

        var late = await agent.PostAsync($"/api/tickets/{id}/pending", null);
        late.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeOf(late)).Should().Be("TICKETS-0005");
    }

    [Fact]
    public async Task A_step_the_status_doesnt_allow_is_refused_with_its_code()
    {
        var id = await FileOne(await factory.ClientFor(Requester));
        var agent = await factory.ClientFor(Agent);

        var response = await agent.PostAsync($"/api/tickets/{id}/resolve", null);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeOf(response)).Should().Be("TICKETS-0006");
        (await StatusOf(agent, id)).Should().Be("New");
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("resolve")]
    [InlineData("close")]
    public async Task A_requester_cant_work_on_tickets(string action)
    {
        var requester = await factory.ClientFor(Requester);
        var id = await FileOne(requester);

        var response = await requester.PostAsync($"/api/tickets/{id}/{action}", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_requester_can_cancel_their_own_ticket_with_a_reason()
    {
        var requester = await factory.ClientFor(Requester);
        var id = await FileOne(requester);

        var withoutReason = await requester.PostAsJsonAsync($"/api/tickets/{id}/cancel", new { reason = "" });
        var withReason = await requester.PostAsJsonAsync($"/api/tickets/{id}/cancel", new { reason = "Fixed itself" });

        withoutReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        withReason.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var ticket = await requester.GetFromJsonAsync<TicketView>($"/api/tickets/{id}");
        ticket!.Status.Should().Be("Cancelled");
        ticket.CancelReason.Should().Be("Fixed itself");
    }

    [Fact]
    public async Task A_cancel_reason_over_500_characters_is_refused_before_the_handler()
    {
        var requester = await factory.ClientFor(Requester);
        var id = await FileOne(requester);

        var response = await requester.PostAsJsonAsync($"/api/tickets/{id}/cancel", new { reason = new string('x', 501) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Someone_elses_ticket_cant_be_cancelled_or_reopened_by_a_requester()
    {
        var id = await FileOne(await factory.ClientFor(Requester));
        var stranger = await factory.ClientFor(await factory.CreateUserAsync(Roles.Requester));

        var cancel = await stranger.PostAsJsonAsync($"/api/tickets/{id}/cancel", new { reason = "Not mine" });
        var reopen = await stranger.PostAsync($"/api/tickets/{id}/reopen", null);

        cancel.StatusCode.Should().Be(HttpStatusCode.NotFound);
        reopen.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var owner = await factory.ClientFor(Requester);
        (await StatusOf(owner, id)).Should().Be("New");
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("resolve")]
    [InlineData("close")]
    [InlineData("reopen")]
    public async Task A_step_on_a_ticket_that_does_not_exist_is_404(string action)
    {
        var agent = await factory.ClientFor(Agent);

        var response = await agent.PostAsync($"/api/tickets/999999999/{action}", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
