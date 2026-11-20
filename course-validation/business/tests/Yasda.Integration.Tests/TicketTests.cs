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

// A ticket's whole life through the real API and a real database: who sees it, who may change it, and what happens
// when two people act at once.
[Collection("Integration")]
public class TicketTests(CustomWebAppFactory factory)
{
    private const string Requester = "requester@yasda.local", Agent = "agent@yasda.local", Lead = "lead@yasda.local";

    private sealed record Created(long Id);
    private sealed record Message(string AuthorName, string Body, bool IsInternal);
    private sealed record TicketView(long Id, string Subject, string Status, string Priority, long? AssigneeId, string? AssigneeName,
        List<Message> Messages);
    private sealed record Row(long Id, string Subject, string Status, long? AssigneeId);
    private sealed record Page(List<Row> Items, int TotalCount);
    private sealed record Me(long Id);

    private static async Task<long> File(HttpClient client, string subject = "Printer won't print")
    {
        var response = await client.PostAsJsonAsync("/api/tickets", new { subject, message = "It says paper jam.", priority = "High" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<Created>())!.Id;
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    [Fact]
    public async Task A_requester_files_a_ticket_and_finds_it_in_their_list()
    {
        var requester = await factory.ClientFor(Requester);

        var id = await File(requester, "Projector in room 2 is blank");

        var ticket = await requester.GetFromJsonAsync<TicketView>($"/api/tickets/{id}");
        ticket!.Status.Should().Be("New");
        ticket.Priority.Should().Be("High");
        ticket.Messages.Should().ContainSingle().Which.Body.Should().Be("It says paper jam.");
        var mine = await requester.GetFromJsonAsync<Page>("/api/tickets?pageSize=100");
        mine!.Items.Select(t => t.Id).Should().Contain(id);
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

    // The race: two agents click "Take" on the same new ticket at the same instant. Exactly one gets it; the other is
    // told no (409 if both loaded it before either saved, 422 if the first had already saved). Never both.
    [Fact]
    public async Task Two_agents_taking_the_same_ticket_at_once_never_both_get_it()
    {
        var first = await factory.ClientFor(Agent);
        var second = await factory.ClientFor(await factory.CreateUserAsync(Roles.Agent));
        var requester = await factory.ClientFor(Requester);

        for (var round = 0; round < 10; round++)
        {
            var id = await File(requester);

            var answers = await Task.WhenAll(first.PostAsync($"/api/tickets/{id}/take", null), second.PostAsync($"/api/tickets/{id}/take", null));

            answers.Count(a => a.StatusCode == HttpStatusCode.NoContent).Should().Be(1, "exactly one agent may win round {0}", round);
            answers.Select(a => a.StatusCode).Should().OnlyContain(s =>
                s == HttpStatusCode.NoContent || s == HttpStatusCode.Conflict || s == HttpStatusCode.UnprocessableEntity);
        }
    }

    // Lost update: two people load the same ticket; the first saves a reply, the second (still holding the old copy)
    // tries to change the priority. The second save must fail, not quietly overwrite.
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

        aliceCopy.Reply(new UserId(3), "Alan Reyes", "Looking now.", isInternal: false, authorIsStaff: true, DateTime.UtcNow);
        await aliceDb.SaveChangesAsync();
        bobCopy.ChangePriority(TicketPriority.Critical, DateTime.UtcNow);

        var bobSaves = () => bobDb.SaveChangesAsync();
        await bobSaves.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task Only_staff_can_be_assigned_and_only_a_lead_assigns()
    {
        var requester = await factory.ClientFor(Requester);
        var id = await File(requester);
        var requesterId = (await requester.GetFromJsonAsync<Me>("/api/auth/me"))!.Id;
        var agentId = (await (await factory.ClientFor(Agent)).GetFromJsonAsync<Me>("/api/auth/me"))!.Id;
        var lead = await factory.ClientFor(Lead);

        var toRequester = await lead.PostAsJsonAsync($"/api/tickets/{id}/assign", new { assigneeId = requesterId });
        toRequester.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeOf(toRequester)).Should().Be("TICKETS-0011");

        (await lead.PostAsJsonAsync($"/api/tickets/{id}/assign", new { assigneeId = agentId })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var agent = await factory.ClientFor(Agent);
        (await agent.PostAsJsonAsync($"/api/tickets/{id}/assign", new { assigneeId = agentId })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("take")]
    [InlineData("pending")]
    [InlineData("resolve")]
    [InlineData("close")]
    public async Task A_requester_cant_work_on_tickets(string action)
    {
        var requester = await factory.ClientFor(Requester);
        var id = await File(requester);

        (await requester.PostAsync($"/api/tickets/{id}/{action}", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_requester_can_cancel_their_own_ticket_with_a_reason()
    {
        var requester = await factory.ClientFor(Requester);
        var id = await File(requester);

        (await requester.PostAsJsonAsync($"/api/tickets/{id}/cancel", new { reason = "" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await requester.PostAsJsonAsync($"/api/tickets/{id}/cancel", new { reason = "Fixed itself" })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await requester.GetFromJsonAsync<TicketView>($"/api/tickets/{id}"))!.Status.Should().Be("Cancelled");
    }

    [Fact]
    public async Task The_queues_show_unassigned_tickets_and_my_own()
    {
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);
        var waiting = await File(requester, "Waiting for someone");
        var mine = await File(requester, "Taken by the agent");
        (await agent.PostAsync($"/api/tickets/{mine}/take", null)).EnsureSuccessStatusCode();

        var unassigned = await agent.GetFromJsonAsync<Page>("/api/tickets?assigned=unassigned&pageSize=100");
        var myQueue = await agent.GetFromJsonAsync<Page>("/api/tickets?assigned=me&pageSize=100");

        unassigned!.Items.Should().Contain(t => t.Id == waiting).And.OnlyContain(t => t.AssigneeId == null);
        myQueue!.Items.Should().Contain(t => t.Id == mine).And.NotContain(t => t.Id == waiting);
    }

    [Theory]
    [InlineData("""{ "subject": "", "message": "Help" }""")]
    [InlineData("""{ "subject": "Printer", "message": "" }""")]
    [InlineData("""{ "subject": "Printer", "message": "Help", "priority": "Whenever" }""")]
    public async Task A_malformed_ticket_is_refused_before_it_reaches_the_handler(string json)
    {
        var requester = await factory.ClientFor(Requester);

        var response = await requester.PostAsync("/api/tickets", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
