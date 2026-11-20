using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Yasda.Modules.Tickets.Domain;
using Yasda.Modules.Tickets.Infrastructure;
using Yasda.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Yasda.Integration.Tests;

// Taking and assigning tickets through the real API, and the two ways the second of two agents loses.
[Collection("Integration")]
public class TakeTicketTests(CustomWebAppFactory factory)
{
    private const string Requester = "requester@yasda.local", Agent = "agent@yasda.local", Lead = "lead@yasda.local";

    private sealed record Created(long Id);
    private sealed record TicketView(string Status, long? AssigneeId, string? AssigneeName);
    private sealed record Me(long Id);

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

    [Fact]
    public async Task An_agent_takes_a_new_ticket_and_it_opens()
    {
        var id = await FileOne(await factory.ClientFor(Requester));
        var agent = await factory.ClientFor(Agent);

        var response = await agent.PostAsync($"/api/tickets/{id}/take", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var ticket = await agent.GetFromJsonAsync<TicketView>($"/api/tickets/{id}");
        ticket!.Status.Should().Be("Open");
        ticket.AssigneeName.Should().Be("Alan Reyes");
    }

    [Fact]
    public async Task A_requester_cant_take_a_ticket()
    {
        var requester = await factory.ClientFor(Requester);
        var id = await FileOne(requester);

        var response = await requester.PostAsync($"/api/tickets/{id}/take", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Taking_a_ticket_that_does_not_exist_is_404()
    {
        var agent = await factory.ClientFor(Agent);

        var response = await agent.PostAsync("/api/tickets/999999999/take", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // The other way to lose: the first agent had already saved when the second asked, so Ticket.Take itself refuses.
    [Fact]
    public async Task A_ticket_someone_else_already_has_cant_be_taken()
    {
        var id = await FileOne(await factory.ClientFor(Requester));
        var first = await factory.ClientFor(Agent);
        var second = await factory.ClientFor(await factory.CreateUserAsync(Roles.Agent));
        var firstAnswer = await first.PostAsync($"/api/tickets/{id}/take", null);
        firstAnswer.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var late = await second.PostAsync($"/api/tickets/{id}/take", null);

        late.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeOf(late)).Should().Be("TICKETS-0007");
    }

    // The race, without the luck: both agents load the ticket while nobody has it, then both save. The row version lets
    // only the first save through, every time.
    [Fact]
    public async Task The_second_of_two_takes_on_the_same_stale_ticket_is_refused()
    {
        var id = await FileOne(await factory.ClientFor(Requester));

        await using var firstScope = factory.Services.CreateAsyncScope();
        await using var secondScope = factory.Services.CreateAsyncScope();
        var firstDb = firstScope.ServiceProvider.GetRequiredService<TicketsDbContext>();
        var secondDb = secondScope.ServiceProvider.GetRequiredService<TicketsDbContext>();
        var firstCopy = await firstDb.Tickets.SingleAsync(t => t.Id == new TicketId(id));
        var secondCopy = await secondDb.Tickets.SingleAsync(t => t.Id == new TicketId(id));

        firstCopy.Take(new UserId(3), "Alan Reyes", DateTime.UtcNow);
        await firstDb.SaveChangesAsync();
        secondCopy.Take(new UserId(4), "Bea Lim", DateTime.UtcNow);

        var secondSaves = () => secondDb.SaveChangesAsync();
        await secondSaves.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    // The race itself: two agents click "Take" on the same new ticket at the same instant. Exactly one gets it; the other
    // is told no (409 if both loaded it before either saved, 422 if the first had already saved). Never both.
    [Fact]
    public async Task Two_agents_taking_the_same_ticket_at_once_never_both_get_it()
    {
        var first = await factory.ClientFor(Agent);
        var second = await factory.ClientFor(await factory.CreateUserAsync(Roles.Agent));
        var requester = await factory.ClientFor(Requester);

        for (var round = 0; round < 10; round++)
        {
            var id = await FileOne(requester);

            // Task.WhenAll on purpose: sending the two requests together is the whole test.
            var answers = await Task.WhenAll(
                first.PostAsync($"/api/tickets/{id}/take", null),
                second.PostAsync($"/api/tickets/{id}/take", null));

            answers.Count(a => a.StatusCode == HttpStatusCode.NoContent).Should().Be(1, "exactly one agent may win round {0}", round);
            answers.Select(a => a.StatusCode).Should().OnlyContain(s =>
                s == HttpStatusCode.NoContent || s == HttpStatusCode.Conflict || s == HttpStatusCode.UnprocessableEntity);
        }
    }

    [Fact]
    public async Task A_lead_assigns_a_ticket_to_an_agent_and_nobody_else_does()
    {
        var id = await FileOne(await factory.ClientFor(Requester));
        var agent = await factory.ClientFor(Agent);
        var me = await agent.GetFromJsonAsync<Me>("/api/auth/me");
        var lead = await factory.ClientFor(Lead);

        var byLead = await lead.PostAsJsonAsync($"/api/tickets/{id}/assign", new { assigneeId = me!.Id });
        var byAgent = await agent.PostAsJsonAsync($"/api/tickets/{id}/assign", new { assigneeId = me.Id });

        byLead.StatusCode.Should().Be(HttpStatusCode.NoContent);
        byAgent.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var ticket = await agent.GetFromJsonAsync<TicketView>($"/api/tickets/{id}");
        ticket!.AssigneeId.Should().Be(me.Id);
    }

    [Fact]
    public async Task A_ticket_cant_be_assigned_to_someone_who_isnt_staff()
    {
        var requester = await factory.ClientFor(Requester);
        var id = await FileOne(requester);
        var me = await requester.GetFromJsonAsync<Me>("/api/auth/me");
        var lead = await factory.ClientFor(Lead);

        var response = await lead.PostAsJsonAsync($"/api/tickets/{id}/assign", new { assigneeId = me!.Id });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeOf(response)).Should().Be("TICKETS-0011");
    }

    [Fact]
    public async Task Assigning_a_ticket_that_does_not_exist_is_404()
    {
        var lead = await factory.ClientFor(Lead);

        var response = await lead.PostAsJsonAsync("/api/tickets/999999999/assign", new { assigneeId = 3 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task An_assignee_id_that_isnt_positive_is_refused_before_the_handler(long assigneeId)
    {
        var id = await FileOne(await factory.ClientFor(Requester));
        var lead = await factory.ClientFor(Lead);

        var response = await lead.PostAsJsonAsync($"/api/tickets/{id}/assign", new { assigneeId });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
