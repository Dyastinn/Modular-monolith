using Yasda.Modules.Sla.Domain;
using Yasda.Modules.Sla.Infrastructure;
using Yasda.Modules.Tickets.Contracts;
using Yasda.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Yasda.Modules.Sla.Features;

// What Sla does when Tickets says something happened. The outbox delivers each message at least once, so each class
// must give the right answer when it hears the same thing twice, and the clock itself guards against a message that
// arrives after a later one (see SlaClock). These run in the outbox delivery, which is why none of them sends mail.

internal sealed class StartClockOnTicketCreated(SlaDbContext db) : IIntegrationEventHandler<TicketCreated>
{
    public async Task HandleAsync(TicketCreated integrationEvent, CancellationToken ct)
    {
        var ticketId = new TicketId(integrationEvent.TicketId);
        var alreadyStarted = await db.Clocks.AnyAsync(c => c.Id == ticketId, ct);
        if (alreadyStarted) return;

        var priority = PriorityMapping.From(integrationEvent.Priority);
        var policy = await db.Policies.SingleAsync(p => p.Priority == priority, ct);
        var calendar = await db.GetBusinessCalendarAsync(ct);

        // The due times come from the ticket's own time, never from now: a delivery that comes hours late must not move them.
        var clock = SlaClock.Start(ticketId, priority, integrationEvent.CreatedUtc, policy.RespondWithin, policy.ResolveWithin, calendar);
        db.Clocks.Add(clock);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two deliveries of the same message raced, and the other one started the clock first. That is the answer we wanted.
        }
    }
}

internal sealed class StopResponseOnFirstReply(SlaDbContext db) : IIntegrationEventHandler<FirstAgentReply>
{
    public async Task HandleAsync(FirstAgentReply integrationEvent, CancellationToken ct)
    {
        var clock = await db.GetClockAsync(integrationEvent.TicketId, ct);
        var calendar = await db.GetBusinessCalendarAsync(ct);
        clock.RecordResponse(integrationEvent.RepliedUtc, calendar);
        await db.SaveChangesAsync(ct);
    }
}

internal sealed class StopResolveOnTicketResolved(SlaDbContext db) : IIntegrationEventHandler<TicketResolved>
{
    public async Task HandleAsync(TicketResolved integrationEvent, CancellationToken ct)
    {
        var clock = await db.GetClockAsync(integrationEvent.TicketId, ct);
        var calendar = await db.GetBusinessCalendarAsync(ct);
        clock.RecordResolution(integrationEvent.ResolvedUtc, calendar);
        await db.SaveChangesAsync(ct);
    }
}

internal sealed class RestartResolveOnTicketReopened(SlaDbContext db) : IIntegrationEventHandler<TicketReopened>
{
    public async Task HandleAsync(TicketReopened integrationEvent, CancellationToken ct)
    {
        var clock = await db.GetClockAsync(integrationEvent.TicketId, ct);
        clock.Reopen(integrationEvent.ReopenedUtc);
        await db.SaveChangesAsync(ct);
    }
}

internal sealed class PauseClockOnTicketPending(SlaDbContext db) : IIntegrationEventHandler<TicketPending>
{
    public async Task HandleAsync(TicketPending integrationEvent, CancellationToken ct)
    {
        var clock = await db.GetClockAsync(integrationEvent.TicketId, ct);
        clock.Pause(integrationEvent.PendingUtc);
        await db.SaveChangesAsync(ct);
    }
}

internal sealed class ResumeClockOnTicketResumed(SlaDbContext db) : IIntegrationEventHandler<TicketResumed>
{
    public async Task HandleAsync(TicketResumed integrationEvent, CancellationToken ct)
    {
        var clock = await db.GetClockAsync(integrationEvent.TicketId, ct);
        var calendar = await db.GetBusinessCalendarAsync(ct);
        clock.Resume(integrationEvent.ResumedUtc, calendar);
        await db.SaveChangesAsync(ct);
    }
}

internal sealed class StopClockOnTicketCancelled(SlaDbContext db) : IIntegrationEventHandler<TicketCancelled>
{
    public async Task HandleAsync(TicketCancelled integrationEvent, CancellationToken ct)
    {
        var clock = await db.GetClockAsync(integrationEvent.TicketId, ct);
        clock.Cancel();
        await db.SaveChangesAsync(ct);
    }
}

internal sealed class ChangeTargetsOnPriorityChanged(SlaDbContext db) : IIntegrationEventHandler<TicketPriorityChanged>
{
    public async Task HandleAsync(TicketPriorityChanged integrationEvent, CancellationToken ct)
    {
        var clock = await db.GetClockAsync(integrationEvent.TicketId, ct);
        var priority = PriorityMapping.From(integrationEvent.Priority);

        // A priority change uses the policy as it is now (decision D5); only new tickets keep an edited policy's old numbers.
        var policy = await db.Policies.SingleAsync(p => p.Priority == priority, ct);
        var calendar = await db.GetBusinessCalendarAsync(ct);
        clock.ChangeTargets(priority, integrationEvent.ChangedUtc, policy.RespondWithin, policy.ResolveWithin, calendar);
        await db.SaveChangesAsync(ct);
    }
}
