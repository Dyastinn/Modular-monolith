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

// The conversation on a ticket and the one thing staff edit on it, through the real API and a real database: who
// may write what, who sees which message, and what happens when two people save against the same copy.
[Collection("Integration")]
public class ConversationTests(CustomWebAppFactory factory)
{
    private const string Requester = "requester@yasda.local", Agent = "agent@yasda.local";

    private sealed record Created(long Id);
    private sealed record Message(string AuthorName, string Body, bool IsInternal);
    private sealed record TicketView(string Status, string Priority, List<Message> Messages);
    private sealed record Row(long Id);
    private sealed record Page(List<Row> Items);

    private static async Task<long> File(HttpClient client)
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
    public async Task Another_requesters_ticket_is_404_and_missing_from_their_list()
    {
        var id = await File(await factory.ClientFor(Requester));
        var someoneElse = await factory.ClientFor(await factory.CreateUserAsync(Roles.Requester));

        (await someoneElse.GetAsync($"/api/tickets/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await someoneElse.PostAsJsonAsync($"/api/tickets/{id}/replies", new { body = "Me too" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var theirs = await someoneElse.GetFromJsonAsync<Page>("/api/tickets?pageSize=100");
        theirs!.Items.Should().NotContain(t => t.Id == id);
    }

    [Fact]
    public async Task Internal_notes_are_hidden_from_the_requester()
    {
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);
        var id = await File(requester);

        (await agent.PostAsJsonAsync($"/api/tickets/{id}/replies", new { body = "On my way.", isInternal = false })).EnsureSuccessStatusCode();
        (await agent.PostAsJsonAsync($"/api/tickets/{id}/replies", new { body = "Fuser again, I bet.", isInternal = true })).EnsureSuccessStatusCode();

        var asRequester = await requester.GetFromJsonAsync<TicketView>($"/api/tickets/{id}");
        var asAgent = await agent.GetFromJsonAsync<TicketView>($"/api/tickets/{id}");
        asRequester!.Messages.Select(m => m.Body).Should().Equal("It says paper jam.", "On my way.");
        asAgent!.Messages.Should().HaveCount(3).And.ContainSingle(m => m.IsInternal);
    }

    [Fact]
    public async Task A_ticket_goes_from_new_to_closed_and_then_refuses_replies()
    {
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);
        var id = await File(requester);

        async Task<string> StatusOf() => (await agent.GetFromJsonAsync<TicketView>($"/api/tickets/{id}"))!.Status;

        (await agent.PostAsync($"/api/tickets/{id}/take", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await StatusOf()).Should().Be("Open");
        (await agent.PostAsync($"/api/tickets/{id}/pending", null)).EnsureSuccessStatusCode();
        (await StatusOf()).Should().Be("Pending");
        (await requester.PostAsJsonAsync($"/api/tickets/{id}/replies", new { body = "It's the 3rd floor one." })).EnsureSuccessStatusCode();
        (await StatusOf()).Should().Be("Open");
        (await agent.PostAsync($"/api/tickets/{id}/resolve", null)).EnsureSuccessStatusCode();
        (await requester.PostAsync($"/api/tickets/{id}/reopen", null)).EnsureSuccessStatusCode();
        (await StatusOf()).Should().Be("Open");
        (await agent.PostAsync($"/api/tickets/{id}/resolve", null)).EnsureSuccessStatusCode();
        (await agent.PostAsync($"/api/tickets/{id}/close", null)).EnsureSuccessStatusCode();
        (await StatusOf()).Should().Be("Closed");

        var late = await requester.PostAsJsonAsync($"/api/tickets/{id}/replies", new { body = "Hello?" });
        late.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeOf(late)).Should().Be("TICKETS-0005");
    }

    [Fact]
    public async Task A_change_based_on_a_stale_copy_of_the_ticket_is_refused()
    {
        var id = await File(await factory.ClientFor(Requester));

        await using var alice = factory.Services.CreateAsyncScope();
        await using var bob = factory.Services.CreateAsyncScope();
        var aliceDb = alice.ServiceProvider.GetRequiredService<TicketsDbContext>();
        var bobDb = bob.ServiceProvider.GetRequiredService<TicketsDbContext>();
        var aliceCopy = await aliceDb.Tickets.SingleAsync(t => t.Id == new TicketId(id));
        var bobCopy = await bobDb.Tickets.SingleAsync(t => t.Id == new TicketId(id));

        aliceCopy.Reply(new UserId(3), "Alan Reyes", "Checking the fuser.", isInternal: true, authorIsStaff: true, DateTime.UtcNow);
        await aliceDb.SaveChangesAsync();
        bobCopy.ChangePriority(TicketPriority.Critical, DateTime.UtcNow);

        var bobSaves = () => bobDb.SaveChangesAsync();
        await bobSaves.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task A_requester_cant_send_an_internal_note_through_the_api()
    {
        var requester = await factory.ClientFor(Requester);
        var id = await File(requester);

        var response = await requester.PostAsJsonAsync($"/api/tickets/{id}/replies", new { body = "Psst.", isInternal = true });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeOf(response)).Should().Be("TICKETS-0008");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_reply_with_no_words_is_refused_before_the_handler(string body)
    {
        var requester = await factory.ClientFor(Requester);
        var id = await File(requester);

        var response = await requester.PostAsJsonAsync($"/api/tickets/{id}/replies", new { body });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("YASDA-0001");
    }

    [Fact]
    public async Task A_reply_to_a_ticket_that_does_not_exist_is_404()
    {
        var agent = await factory.ClientFor(Agent);

        var response = await agent.PostAsJsonAsync("/api/tickets/999999999/replies", new { body = "Hello?" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Staff_change_a_tickets_priority()
    {
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);
        var id = await File(requester);

        var response = await agent.PutAsJsonAsync($"/api/tickets/{id}/priority", new { priority = "Critical" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var ticket = await requester.GetFromJsonAsync<TicketView>($"/api/tickets/{id}");
        ticket!.Priority.Should().Be("Critical");
    }

    [Fact]
    public async Task A_requester_cant_change_the_priority()
    {
        var requester = await factory.ClientFor(Requester);
        var id = await File(requester);

        var response = await requester.PutAsJsonAsync($"/api/tickets/{id}/priority", new { priority = "Critical" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_priority_nobody_knows_is_refused_before_the_handler()
    {
        var agent = await factory.ClientFor(Agent);
        var id = await File(await factory.ClientFor(Requester));

        var response = await agent.PutAsJsonAsync($"/api/tickets/{id}/priority", new { priority = "Whenever" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
