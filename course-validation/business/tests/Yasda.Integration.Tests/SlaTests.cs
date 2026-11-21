using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Dapper;
using Yasda.Modules.Sla.Domain;
using Yasda.Modules.Tickets.Contracts;
using Yasda.Shared.BuildingBlocks;
using Microsoft.Extensions.DependencyInjection;

namespace Yasda.Integration.Tests;

// A ticket's deadlines, from the ticket being filed to the screen that shows them: Tickets raises an event, the outbox
// delivers it, Sla works the deadlines out, and the answer travels back the same way. Every step is a moment later, so the
// tests wait for what they expect instead of guessing how long to sleep.
[Collection("Integration")]
public class SlaTests(CustomWebAppFactory factory)
{
    private const string Requester = "requester@yasda.local", Agent = "agent@yasda.local", Admin = "admin@yasda.local";

    private sealed record Created(long Id);
    private sealed record Sla(DateTime ResponseDueUtc, bool? ResponseMet, DateTime ResolveDueUtc, bool? ResolutionMet,
        DateTime? NextDueUtc, DateTime? AtRiskFromUtc, bool Paused);
    private sealed record TicketView(long Id, string Status, string Priority, DateTime CreatedUtc, Sla? Sla);
    private sealed record Row(long Id, DateTime? NextDueUtc, DateTime? AtRiskFromUtc, bool? SlaPaused);
    private sealed record Page(List<Row> Items);

    // The installation's calendar (Monday to Friday, 08:00 to 17:00, Manila). The tests ask it where a deadline should land, so they
    // pass at any time of day: counting open hours is the calendar's job, and its own tests prove it.
    private static readonly BusinessCalendar OfficeHours = new(
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"),
        new Dictionary<DayOfWeek, OpenHours>
        {
            [DayOfWeek.Monday] = new(new TimeOnly(8, 0), new TimeOnly(17, 0)),
            [DayOfWeek.Tuesday] = new(new TimeOnly(8, 0), new TimeOnly(17, 0)),
            [DayOfWeek.Wednesday] = new(new TimeOnly(8, 0), new TimeOnly(17, 0)),
            [DayOfWeek.Thursday] = new(new TimeOnly(8, 0), new TimeOnly(17, 0)),
            [DayOfWeek.Friday] = new(new TimeOnly(8, 0), new TimeOnly(17, 0)),
        },
        []);

    // The database keeps whole microseconds, so the ticket's time is cut the same way before counting.
    private static DateTime DueAfter(DateTime createdUtc, TimeSpan openTime)
    {
        var whole = new DateTime(createdUtc.Ticks - createdUtc.Ticks % 10, DateTimeKind.Utc);
        return OfficeHours.Add(whole, openTime);
    }

    private static async Task<long> File(HttpClient client, string priority, string? subject = null)
    {
        var response = await client.PostAsJsonAsync("/api/tickets",
            new { subject = subject ?? $"Deadlines {Guid.NewGuid():N}", message = "Please help.", priority });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<Created>();
        return created!.Id;
    }

    // Reads the ticket until the deadlines Sla worked out have arrived and satisfy the test, then hands back what it read.
    private static async Task<TicketView> ReadUntil(HttpClient staff, long id, Func<TicketView, bool> arrived)
    {
        var ticket = await Waiting.Eventually(() => staff.GetFromJsonAsync<TicketView>($"/api/tickets/{id}")!, arrived);
        return ticket!;
    }

    private static bool HasDeadlines(TicketView ticket)
    {
        return ticket.Sla is not null;
    }

    [Fact]
    public async Task A_new_ticket_gets_deadlines_from_its_priority_policy()
    {
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);
        var id = await File(requester, "High");

        var ticket = await ReadUntil(agent, id, HasDeadlines);

        // High is 4 open hours to respond and 24 to resolve (decision D3), counted from when the ticket was filed. It is at risk
        // for the last quarter of the open time, so from 3 open hours on.
        ticket.Sla.Should().NotBeNull();
        ticket.Sla!.ResponseDueUtc.Should().Be(DueAfter(ticket.CreatedUtc, TimeSpan.FromHours(4)));
        ticket.Sla.ResolveDueUtc.Should().Be(DueAfter(ticket.CreatedUtc, TimeSpan.FromHours(24)));
        ticket.Sla.NextDueUtc.Should().Be(ticket.Sla.ResponseDueUtc);
        ticket.Sla.AtRiskFromUtc.Should().Be(DueAfter(ticket.CreatedUtc, TimeSpan.FromHours(3)));
        ticket.Sla.ResponseMet.Should().BeNull();
        ticket.Sla.ResolutionMet.Should().BeNull();
    }

    [Fact]
    public async Task The_first_agent_reply_meets_the_response_target()
    {
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);
        var id = await File(requester, "Normal");
        await ReadUntil(agent, id, HasDeadlines);

        (await agent.PostAsJsonAsync($"/api/tickets/{id}/replies", new { body = "On my way." })).EnsureSuccessStatusCode();
        var ticket = await ReadUntil(agent, id, t => t.Sla?.ResponseMet is not null);

        ticket.Sla!.ResponseMet.Should().BeTrue();
        ticket.Sla.NextDueUtc.Should().Be(ticket.Sla.ResolveDueUtc, "with the response done, the resolve target is next");
    }

    [Fact]
    public async Task Resolving_stops_and_reopening_restarts_the_resolve_target()
    {
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);
        var id = await File(requester, "Normal");
        (await agent.PostAsJsonAsync($"/api/tickets/{id}/replies", new { body = "Looking." })).EnsureSuccessStatusCode();
        await ReadUntil(agent, id, t => t.Sla?.ResponseMet is not null);

        (await agent.PostAsync($"/api/tickets/{id}/resolve", null)).EnsureSuccessStatusCode();
        var resolved = await ReadUntil(agent, id, t => t.Sla?.ResolutionMet is not null);
        resolved.Sla!.ResolutionMet.Should().BeTrue();
        resolved.Sla.NextDueUtc.Should().BeNull("nothing is left running");

        (await requester.PostAsync($"/api/tickets/{id}/reopen", null)).EnsureSuccessStatusCode();
        var reopened = await ReadUntil(agent, id, t => t.Sla?.ResolutionMet is null);
        reopened.Sla!.ResolutionMet.Should().BeNull();
        reopened.Sla.NextDueUtc.Should().Be(reopened.Sla.ResolveDueUtc, "the resolve target runs again, with the same due time");
        reopened.Sla.ResolveDueUtc.Should().Be(resolved.Sla.ResolveDueUtc);
    }

    // Decision of 2026-11-20: resolving does not count as a response. A ticket fixed with no public reply keeps showing its
    // unanswered response target; only an agent's reply stops it.
    [Fact]
    public async Task A_ticket_resolved_without_a_reply_keeps_its_response_target_running()
    {
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);
        var id = await File(requester, "Normal");
        await ReadUntil(agent, id, HasDeadlines);
        (await agent.PostAsync($"/api/tickets/{id}/take", null)).EnsureSuccessStatusCode();

        (await agent.PostAsync($"/api/tickets/{id}/resolve", null)).EnsureSuccessStatusCode();
        var ticket = await ReadUntil(agent, id, t => t.Sla?.ResolutionMet is not null);

        ticket.Sla!.ResolutionMet.Should().BeTrue();
        ticket.Sla.ResponseMet.Should().BeNull();
        ticket.Sla.NextDueUtc.Should().Be(ticket.Sla.ResponseDueUtc);
    }

    [Fact]
    public async Task A_cancelled_ticket_has_no_running_deadline()
    {
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);
        var id = await File(requester, "Low");
        await ReadUntil(agent, id, HasDeadlines);

        (await requester.PostAsJsonAsync($"/api/tickets/{id}/cancel", new { reason = "Fixed itself" })).EnsureSuccessStatusCode();
        var ticket = await ReadUntil(agent, id, t => t.Sla?.NextDueUtc is null);

        ticket.Sla!.NextDueUtc.Should().BeNull();
        ticket.Sla.AtRiskFromUtc.Should().BeNull();
        ticket.Sla.ResponseMet.Should().BeNull("a cancelled ticket is not measured");
        ticket.Sla.ResolutionMet.Should().BeNull();
    }

    [Fact]
    public async Task A_priority_change_recomputes_the_deadlines()
    {
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);
        var id = await File(requester, "Low");
        var before = await ReadUntil(agent, id, HasDeadlines);
        before.Sla!.ResponseDueUtc.Should().Be(DueAfter(before.CreatedUtc, TimeSpan.FromHours(24)));

        (await agent.PutAsJsonAsync($"/api/tickets/{id}/priority", new { priority = "Critical" })).EnsureSuccessStatusCode();
        var after = await ReadUntil(agent, id, t => t.Sla?.ResponseDueUtc != before.Sla.ResponseDueUtc);

        // Critical is 1 open hour to respond and 4 to resolve, still counted from when the ticket was filed.
        after.Sla!.ResponseDueUtc.Should().Be(DueAfter(after.CreatedUtc, TimeSpan.FromHours(1)));
        after.Sla.ResolveDueUtc.Should().Be(DueAfter(after.CreatedUtc, TimeSpan.FromHours(4)));
    }

    // Waiting on the requester stops the clock (decision E4): nothing is due while paused, and the requester's answer starts it again.
    [Fact]
    public async Task A_ticket_waiting_on_the_requester_pauses_its_clock_and_their_answer_resumes_it()
    {
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);
        var id = await File(requester, "High");
        await ReadUntil(agent, id, HasDeadlines);
        (await agent.PostAsync($"/api/tickets/{id}/take", null)).EnsureSuccessStatusCode();

        (await agent.PostAsync($"/api/tickets/{id}/pending", null)).EnsureSuccessStatusCode();
        var paused = await ReadUntil(agent, id, t => t.Sla?.Paused == true);
        paused.Sla!.NextDueUtc.Should().BeNull("a paused clock has nothing due");
        paused.Sla.AtRiskFromUtc.Should().BeNull();
        var queue = await agent.GetFromJsonAsync<Page>($"/api/tickets?search={id}&pageSize=100");
        queue!.Items.Single(t => t.Id == id).SlaPaused.Should().BeTrue();

        (await requester.PostAsJsonAsync($"/api/tickets/{id}/replies", new { body = "Here is the answer." })).EnsureSuccessStatusCode();
        var running = await ReadUntil(agent, id, t => t.Sla?.Paused == false);
        running.Sla!.NextDueUtc.Should().NotBeNull("the clock runs again");
        running.Sla.ResponseDueUtc.Should().BeOnOrAfter(paused.Sla.ResponseDueUtc, "the time spent waiting is not held against the team");
    }

    [Fact]
    public async Task A_requester_never_receives_SLA_fields()
    {
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);
        var id = await File(requester, "High");
        await ReadUntil(agent, id, HasDeadlines);

        var detail = await requester.GetFromJsonAsync<TicketView>($"/api/tickets/{id}");
        var queue = await requester.GetFromJsonAsync<Page>($"/api/tickets?search={id}&pageSize=100");

        detail!.Sla.Should().BeNull();
        queue!.Items.Should().NotBeEmpty();
        queue.Items.Should().OnlyContain(t => t.NextDueUtc == null && t.AtRiskFromUtc == null);
    }

    [Fact]
    public async Task The_queue_sorts_by_next_due()
    {
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);
        var word = Guid.NewGuid().ToString("N");
        var low = await File(requester, "Low", $"{word} slow");
        var critical = await File(requester, "Critical", $"{word} urgent");
        var high = await File(requester, "High", $"{word} soon");
        var cancelled = await File(requester, "Normal", $"{word} gone");
        foreach (var id in new[] { low, critical, high, cancelled })
            await ReadUntil(agent, id, HasDeadlines);
        (await requester.PostAsJsonAsync($"/api/tickets/{cancelled}/cancel", new { reason = "Not needed" })).EnsureSuccessStatusCode();
        await ReadUntil(agent, cancelled, t => t.Sla?.NextDueUtc is null);

        var soonestFirst = await agent.GetFromJsonAsync<Page>($"/api/tickets?search={word}&sort=nextDue");
        var latestFirst = await agent.GetFromJsonAsync<Page>($"/api/tickets?search={word}&sort=nextDue&desc=true");

        soonestFirst!.Items.Select(t => t.Id).Should().Equal(critical, high, low, cancelled);
        latestFirst!.Items.Select(t => t.Id).Should().Equal(low, high, critical, cancelled);
    }

    // PostgreSQL keeps whole microseconds, and the time in an event has finer ticks. A clock reloaded from the database has
    // its due time cut to the microsecond, so a reply made exactly at the due time once counted as late. Chosen ticks make it
    // happen every time, not only when the clock's last digit is unlucky.
    [Fact]
    public async Task A_reply_made_exactly_at_the_due_time_is_on_time_after_the_database_round_trip()
    {
        var ticketId = UniqueTicketId();
        // Wednesday 09:00 in Manila, inside open hours, so the 4 open hours end the same day at 13:00.
        var filed = new DateTime(2026, 11, 18, 1, 0, 0, DateTimeKind.Utc).AddTicks(8);
        await DeliverTicketCreatedAsync(ticketId, filed);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<FirstAgentReply>>();
            // High is 4 hours to respond: this reply is exactly at the due time.
            await handler.HandleAsync(new FirstAgentReply(ticketId, filed.AddHours(4)), CancellationToken.None);
        }

        await using var conn = factory.OpenDatabase();
        await Waiting.OutboxesAreDrainedAsync(conn);
        var met = await conn.ExecuteScalarAsync<bool?>("SELECT response_met FROM tickets.ticket_sla WHERE ticket_id = @ticketId", new { ticketId });
        met.Should().BeTrue();
    }

    [Fact]
    public async Task The_seeded_tickets_have_deadlines_too()
    {
        await using var conn = factory.OpenDatabase();
        await Waiting.OutboxesAreDrainedAsync(conn);

        var clocks = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM sla.sla_clock");
        var copies = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM tickets.ticket_sla");

        clocks.Should().BeGreaterThanOrEqualTo(3);
        copies.Should().BeGreaterThanOrEqualTo(3);
    }

    // A message can be delivered late and more than once, and a deadline must not move for either: it is worked out from the
    // time the ticket was filed, never from the moment of delivery.
    [Fact]
    public async Task A_late_redelivery_does_not_move_the_deadline()
    {
        var ticketId = UniqueTicketId();
        var filedTwoDaysAgo = DateTime.UtcNow.AddDays(-2);

        await DeliverTicketCreatedAsync(ticketId, filedTwoDaysAgo);
        await DeliverTicketCreatedAsync(ticketId, filedTwoDaysAgo.AddHours(30));

        await using var conn = factory.OpenDatabase();
        var due = await conn.QuerySingleAsync<DateTime>("SELECT response_due_utc FROM sla.sla_clock WHERE ticket_id = @ticketId", new { ticketId });
        due.Should().BeCloseTo(DueAfter(filedTwoDaysAgo, TimeSpan.FromHours(4)), TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task TicketCreated_delivered_twice_starts_one_clock()
    {
        var ticketId = UniqueTicketId();
        var filed = DateTime.UtcNow;

        // Task.WhenAll on purpose: two deliveries at the same moment is the case where both see "no clock yet".
        await Task.WhenAll(DeliverTicketCreatedAsync(ticketId, filed), DeliverTicketCreatedAsync(ticketId, filed));

        await using var conn = factory.OpenDatabase();
        var clocks = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM sla.sla_clock WHERE ticket_id = @ticketId", new { ticketId });
        clocks.Should().Be(1);
    }

    private async Task DeliverTicketCreatedAsync(long ticketId, DateTime filedUtc)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<TicketCreated>>();
        await handler.HandleAsync(new TicketCreated(ticketId, TicketPriority.High, filedUtc), CancellationToken.None);
    }

    // Far above any number the ticket sequence will reach, and different every run.
    private static long UniqueTicketId()
    {
        return 1_000_000_000L + Random.Shared.NextInt64(1, 1_000_000_000L);
    }
}
