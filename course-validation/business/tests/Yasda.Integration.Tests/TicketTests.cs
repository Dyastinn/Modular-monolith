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

    // ---- Filing: what the request itself is checked for, before and after it reaches the handler ----

    [Fact]
    public async Task A_requester_files_a_ticket_and_it_is_saved_with_their_name()
    {
        var requester = await factory.ClientFor(Requester);

        var response = await requester.PostAsJsonAsync("/api/tickets",
            new { subject = "Projector in room 2 is blank", message = "It says no signal.", priority = "High" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<CreatedId>();
        response.Headers.Location!.ToString().Should().Be($"/api/tickets/{created!.Id}");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsDbContext>();
        var ticket = await db.Tickets.SingleAsync(t => t.Id == new TicketId(created.Id));
        ticket.Subject.Should().Be("Projector in room 2 is blank");
        ticket.Priority.Should().Be(TicketPriority.High);
        ticket.Status.Should().Be(TicketStatus.New);
        ticket.RequesterName.Should().Be("Rita Cruz", "the name is copied from Identity, through its contract");
        ticket.Messages.Should().ContainSingle().Which.Body.Should().Be("It says no signal.");
    }

    [Fact]
    public async Task The_priority_is_normal_when_the_request_leaves_it_out()
    {
        var requester = await factory.ClientFor(Requester);

        var response = await requester.PostAsJsonAsync("/api/tickets", new { subject = "No priority given", message = "Help" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<CreatedId>();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsDbContext>();
        var ticket = await db.Tickets.SingleAsync(t => t.Id == new TicketId(created!.Id));
        ticket.Priority.Should().Be(TicketPriority.Normal);
    }

    [Fact]
    public async Task Filing_a_ticket_without_signing_in_is_401()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/tickets", new { subject = "Hello", message = "Anyone?" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_refused_request_names_the_field_and_carries_the_validation_code()
    {
        var requester = await factory.ClientFor(Requester);

        var response = await requester.PostAsync("/api/tickets",
            new StringContent("""{ "subject": "", "message": "Help" }""", System.Text.Encoding.UTF8, "application/json"));

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("YASDA-0001");
        problem.GetProperty("errors").GetProperty("Subject")[0].GetString().Should().Contain("must not be empty");
    }

    [Fact]
    public async Task A_subject_over_200_characters_is_refused()
    {
        var requester = await factory.ClientFor(Requester);

        var response = await requester.PostAsJsonAsync("/api/tickets", new { subject = new string('a', 201), message = "Help" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_body_that_is_not_json_is_a_400_with_its_own_code()
    {
        var requester = await factory.ClientFor(Requester);

        var response = await requester.PostAsync("/api/tickets",
            new StringContent("{ this is not json", System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("YASDA-0004");
    }

    // ---- Reading tickets back: the queue and the ticket page ----

    private sealed record QueueRow(long Id, string Subject, string Status, string Priority, string RequesterName, long? AssigneeId);
    private sealed record QueuePage(List<QueueRow> Items, int Page, int PageSize, int TotalCount);
    private sealed record TicketPageView(long Id, string Subject, string Status, string Priority, List<MessageView> Messages);
    private sealed record MessageView(string AuthorName, string Body, bool IsInternal);

    private static async Task<long> FileOne(HttpClient client, string subject, string priority = "Normal")
    {
        var response = await client.PostAsJsonAsync("/api/tickets", new { subject, message = "Details for " + subject, priority });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CreatedId>())!.Id;
    }

    [Fact]
    public async Task The_ticket_page_shows_the_ticket_and_its_first_message()
    {
        var requester = await factory.ClientFor("requester@yasda.local");
        var id = await FileOne(requester, $"Scanner {Guid.NewGuid():N}", "High");

        var ticket = await requester.GetFromJsonAsync<TicketPageView>($"/api/tickets/{id}");

        ticket!.Status.Should().Be("New");
        ticket.Priority.Should().Be("High");
        ticket.Messages.Should().ContainSingle().Which.Body.Should().StartWith("Details for Scanner");
    }

    [Fact]
    public async Task A_ticket_that_does_not_exist_is_404()
    {
        var agent = await factory.ClientFor("agent@yasda.local");

        var response = await agent.GetAsync("/api/tickets/999999999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Staff_see_everyones_tickets_and_a_requester_only_their_own()
    {
        var someoneElse = await factory.ClientFor(await factory.CreateUserAsync(Roles.Requester));
        var theirs = await FileOne(someoneElse, $"Theirs {Guid.NewGuid():N}");
        var requester = await factory.ClientFor("requester@yasda.local");
        var mine = await FileOne(requester, $"Mine {Guid.NewGuid():N}");
        var agent = await factory.ClientFor("agent@yasda.local");

        var staffQueue = await agent.GetFromJsonAsync<QueuePage>("/api/tickets?pageSize=100");
        var myQueue = await requester.GetFromJsonAsync<QueuePage>("/api/tickets?pageSize=100");

        staffQueue!.Items.Select(t => t.Id).Should().Contain([mine, theirs]);
        myQueue!.Items.Select(t => t.Id).Should().Contain(mine).And.NotContain(theirs);
        (await requester.GetAsync($"/api/tickets/{theirs}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_queue_filters_by_priority_and_by_search_text()
    {
        var requester = await factory.ClientFor("requester@yasda.local");
        var word = Guid.NewGuid().ToString("N");
        var critical = await FileOne(requester, $"Server {word} is down", "Critical");
        await FileOne(requester, $"Server {word} is slow", "Low");
        var agent = await factory.ClientFor("agent@yasda.local");

        var found = await agent.GetFromJsonAsync<QueuePage>($"/api/tickets?search={word}&priority=Critical");

        found!.TotalCount.Should().Be(1);
        found.Items.Should().ContainSingle().Which.Id.Should().Be(critical);
    }

    [Fact]
    public async Task A_search_for_a_percent_sign_matches_it_literally()
    {
        var requester = await factory.ClientFor("requester@yasda.local");
        var word = Guid.NewGuid().ToString("N");
        var literal = await FileOne(requester, $"{word} battery at 50% already");
        await FileOne(requester, $"{word} battery at 500 already");
        var agent = await factory.ClientFor("agent@yasda.local");

        var found = await agent.GetFromJsonAsync<QueuePage>($"/api/tickets?search={Uri.EscapeDataString(word + " battery at 50%")}");

        found!.Items.Should().ContainSingle().Which.Id.Should().Be(literal);
    }

    [Fact]
    public async Task The_queue_pages_and_reports_the_total()
    {
        var requester = await factory.ClientFor("requester@yasda.local");
        var word = Guid.NewGuid().ToString("N");
        for (var i = 1; i <= 3; i++)
            await FileOne(requester, $"Paged {word} number {i}");
        var agent = await factory.ClientFor("agent@yasda.local");

        var first = await agent.GetFromJsonAsync<QueuePage>($"/api/tickets?search={word}&pageSize=2&sort=id");
        var second = await agent.GetFromJsonAsync<QueuePage>($"/api/tickets?search={word}&pageSize=2&sort=id&page=2");

        first!.TotalCount.Should().Be(3);
        first.Items.Should().HaveCount(2);
        second!.Items.Should().HaveCount(1);
        first.Items.Select(t => t.Id).Should().NotIntersectWith(second.Items.Select(t => t.Id));
    }
}
