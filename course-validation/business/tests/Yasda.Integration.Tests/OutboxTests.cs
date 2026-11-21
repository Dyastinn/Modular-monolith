using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Dapper;
using Yasda.Shared.BuildingBlocks;
using Microsoft.Extensions.DependencyInjection;
using SlaDomain = Yasda.Modules.Sla.Domain;
using TicketsDomain = Yasda.Modules.Tickets.Domain;

namespace Yasda.Integration.Tests;

// The outbox is the promise that an event is never lost: it is saved with the change that raised it, delivered after the
// commit, delivered again if something fails, and left for a person to look at if it never works.
[Collection("Integration")]
public class OutboxTests(CustomWebAppFactory factory)
{
    private sealed record Created(long Id);

    // The outbox stores an event as text and reads it back. A type that doesn't survive that trip would be lost silently.
    [Fact]
    public void Every_event_survives_the_round_trip_through_the_outbox()
    {
        var at = new DateTime(2026, 11, 20, 9, 30, 15, DateTimeKind.Utc).AddTicks(1234567);
        var ticketsId = new TicketsDomain.TicketId(7);
        IDomainEvent[] events =
        [
            new TicketsDomain.TicketCreatedEvent(Guid.NewGuid(), ticketsId, "Printer won't print", new TicketsDomain.TeamId(2), new TicketsDomain.UserId(5),
                TicketsDomain.TicketPriority.High, at),
            new TicketsDomain.TicketCreatedEvent(Guid.NewGuid(), ticketsId, "No team yet", null, new TicketsDomain.UserId(5),
                TicketsDomain.TicketPriority.Low, at),
            new TicketsDomain.TicketAssignedEvent(Guid.NewGuid(), ticketsId, "Printer won't print", new TicketsDomain.UserId(3), new TicketsDomain.UserId(4)),
            new TicketsDomain.ReplyAddedEvent(Guid.NewGuid(), ticketsId, "Printer won't print", new TicketsDomain.UserId(3), true, false, "Try again.",
                new TicketsDomain.UserId(5), new TicketsDomain.UserId(3), new TicketsDomain.TeamId(2)),
            new TicketsDomain.ReplyAddedEvent(Guid.NewGuid(), ticketsId, "No team yet", new TicketsDomain.UserId(5), false, false, "Still broken.",
                new TicketsDomain.UserId(5), null, null),
            new TicketsDomain.FirstAgentReplyEvent(ticketsId, at),
            new TicketsDomain.TicketResolvedEvent(ticketsId, at),
            new TicketsDomain.TicketReopenedEvent(ticketsId, at),
            new TicketsDomain.TicketCancelledEvent(ticketsId, at),
            new TicketsDomain.TicketPriorityChangedEvent(ticketsId, TicketsDomain.TicketPriority.Critical, at),
            new TicketsDomain.TicketPendingEvent(ticketsId, at),
            new TicketsDomain.TicketResumedEvent(ticketsId, at),
            new SlaDomain.DeadlinesChangedEvent(new SlaDomain.TicketId(7), 3, at, true, at.AddHours(20), null, at.AddHours(20), at.AddHours(15), false),
            new SlaDomain.DeadlinesChangedEvent(new SlaDomain.TicketId(8), 1, at, null, at.AddHours(20), false, null, null, true),
        ];

        foreach (var original in events)
        {
            var message = OutboxMessage.From(original, DateTime.UtcNow);
            var readBack = OutboxMessage.Deserialize(message.Type, message.Payload);
            readBack.Should().Be(original, $"{original.GetType().Name} must come back exactly as it went in");
        }
    }

    // A new event is only safe once this test has seen it: every event type in the modules must be in the list above.
    [Fact]
    public void No_event_type_is_left_out_of_the_round_trip_test()
    {
        var tested = new HashSet<Type>
        {
            typeof(TicketsDomain.TicketCreatedEvent), typeof(TicketsDomain.TicketAssignedEvent), typeof(TicketsDomain.ReplyAddedEvent),
            typeof(TicketsDomain.FirstAgentReplyEvent), typeof(TicketsDomain.TicketResolvedEvent),
            typeof(TicketsDomain.TicketReopenedEvent), typeof(TicketsDomain.TicketCancelledEvent), typeof(TicketsDomain.TicketPriorityChangedEvent),
            typeof(TicketsDomain.TicketPendingEvent), typeof(TicketsDomain.TicketResumedEvent),
            typeof(SlaDomain.DeadlinesChangedEvent),
        };
        Type[] anchors = [typeof(TicketsDomain.Ticket), typeof(SlaDomain.SlaClock)];

        var missing = new List<string>();
        foreach (var anchor in anchors)
        {
            foreach (var type in anchor.Assembly.GetTypes())
            {
                var isEvent = typeof(IDomainEvent).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface;
                if (isEvent && !tested.Contains(type)) missing.Add(type.FullName!);
            }
        }

        missing.Should().BeEmpty("add each new event to Every_event_survives_the_round_trip_through_the_outbox");
    }

    // Tickets get their number from a sequence before they are saved, because the event is written to the outbox before
    // PostgreSQL would hand out an identity value; an event raised too early would carry ticket number 0.
    [Fact]
    public async Task The_created_event_carries_the_ticket_number()
    {
        var requester = await factory.ClientFor("requester@yasda.local");
        var response = await requester.PostAsJsonAsync("/api/tickets", new { subject = "Numbered", message = "Please help.", priority = "Low" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await response.Content.ReadFromJsonAsync<Created>())!.Id;

        await using var conn = factory.OpenDatabase();
        var events = await conn.ExecuteScalarAsync<long>(
            """
            SELECT COUNT(*) FROM tickets.outbox_message
            WHERE type LIKE '%TicketCreatedEvent%' AND (payload -> 'TicketId' ->> 'Value')::bigint = @id
            """, new { id });

        events.Should().Be(1);
    }

    // A change that is rolled back must not leave its event behind: the two are saved in one transaction, or not at all.
    [Fact]
    public async Task A_rolled_back_change_writes_no_outbox_row()
    {
        long id;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Yasda.Modules.Tickets.Infrastructure.TicketsDbContext>();
            var number = await db.NextTicketIdAsync(CancellationToken.None);
            id = number.Value;
            await using var transaction = await db.Database.BeginTransactionAsync();
            var ticket = TicketsDomain.Ticket.Create(number, "Never saved", "Rolled back", TicketsDomain.TicketPriority.Low,
                new TicketsDomain.UserId(5), "Rita Cruz", DateTime.UtcNow);
            db.Tickets.Add(ticket);
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using var conn = factory.OpenDatabase();
        var tickets = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM tickets.ticket WHERE id = @id", new { id });
        var events = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM tickets.outbox_message WHERE (payload -> 'TicketId' ->> 'Value')::bigint = @id", new { id });
        tickets.Should().Be(0);
        events.Should().Be(0);
    }

    // An event left in the outbox by a process that stopped before delivering it (a crash, a restart) is picked up by the
    // next dispatcher, however old it is. The row is written the way a stopped process would have left it.
    [Fact]
    public async Task An_event_saved_before_a_crash_is_still_delivered()
    {
        var ticketId = 2_000_000_000L + Random.Shared.NextInt64(1, 1_000_000_000L);
        var left = OutboxMessage.From(new TicketsDomain.TicketCreatedEvent(Guid.NewGuid(), new TicketsDomain.TicketId(ticketId), "Left behind", null,
            new TicketsDomain.UserId(5), TicketsDomain.TicketPriority.Normal, DateTime.UtcNow.AddHours(-3)), DateTime.UtcNow.AddHours(-3));
        await using var conn = factory.OpenDatabase();
        await conn.ExecuteAsync(
            """
            INSERT INTO tickets.outbox_message (id, type, payload, occurred_utc, attempts)
            VALUES (@Id, @Type, @Payload::jsonb, @OccurredUtc, 0)
            """, new { left.Id, left.Type, left.Payload, left.OccurredUtc });

        var clocks = await Waiting.Eventually(
            () => conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM sla.sla_clock WHERE ticket_id = @ticketId", new { ticketId }),
            count => count == 1);

        clocks.Should().Be(1);
        var processed = await conn.ExecuteScalarAsync<DateTime?>("SELECT processed_utc FROM tickets.outbox_message WHERE id = @Id", new { left.Id });
        processed.Should().NotBeNull();
    }

    // A message whose handler keeps failing is tried again on every tick, then left alone with its error: it must neither
    // be lost nor block the messages behind it. Cancelling a ticket Sla has never heard of fails every time.
    [Fact]
    public async Task A_failing_handler_is_retried_then_parked()
    {
        var ticketId = 3_000_000_000L + Random.Shared.NextInt64(1, 1_000_000_000L);
        var cancelled = OutboxMessage.From(new TicketsDomain.TicketCancelledEvent(new TicketsDomain.TicketId(ticketId), DateTime.UtcNow), DateTime.UtcNow);
        await using var conn = factory.OpenDatabase();
        await conn.ExecuteAsync(
            """
            INSERT INTO tickets.outbox_message (id, type, payload, occurred_utc, attempts)
            VALUES (@Id, @Type, @Payload::jsonb, @OccurredUtc, 0)
            """, new { cancelled.Id, cancelled.Type, cancelled.Payload, cancelled.OccurredUtc });

        var attempts = await Waiting.Eventually(
            () => conn.ExecuteScalarAsync<int>("SELECT attempts FROM tickets.outbox_message WHERE id = @Id", new { cancelled.Id }),
            count => count >= 10);

        attempts.Should().Be(10);
        var row = await conn.QuerySingleAsync<(DateTime? Processed, string? Error)>(
            "SELECT processed_utc, last_error FROM tickets.outbox_message WHERE id = @Id", new { cancelled.Id });
        row.Processed.Should().BeNull("a parked message is left for a person to look at");
        row.Error.Should().Contain($"No SLA clock for ticket #{ticketId}");
    }
}
