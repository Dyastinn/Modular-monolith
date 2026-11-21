using Yasda.Modules.Sla.Contracts;
using Yasda.Modules.Tickets.Domain;
using Yasda.Modules.Tickets.Infrastructure;
using Yasda.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Yasda.Modules.Tickets.Features;

// Sla computed new deadlines for a ticket: remember them, so the queues can show and sort them. Delivery repeats and
// reorders, so only a newer version is applied. The ticket's own row is never touched.
internal sealed class StoreDeadlines(TicketsDbContext db) : IIntegrationEventHandler<DeadlinesChanged>
{
    public async Task HandleAsync(DeadlinesChanged integrationEvent, CancellationToken ct)
    {
        var ticketId = new TicketId(integrationEvent.TicketId);
        var stored = await db.TicketSlas.SingleOrDefaultAsync(s => s.TicketId == ticketId, ct);
        if (stored is null)
        {
            stored = TicketSla.ForTicket(ticketId);
            db.TicketSlas.Add(stored);
        }

        var applied = stored.Apply(integrationEvent.Version, integrationEvent.ResponseDueUtc, integrationEvent.ResponseMet,
            integrationEvent.ResolveDueUtc, integrationEvent.ResolutionMet, integrationEvent.NextDueUtc, integrationEvent.AtRiskFromUtc, integrationEvent.Paused);
        if (!applied) return;

        await db.SaveChangesAsync(ct);
    }
}
