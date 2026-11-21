using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets.Contracts;

// What Tickets tells the other modules about a ticket. Plain data only, so Tickets can change its domain without
// breaking the modules that listen. Each carries the time it happened, never the time it was delivered: a delivery
// can come late, and a deadline must not move because of it.
// CategoryId is the ticket's category when it was filed, or null when the requester wasn't sure. Sla may have its own SLA for a category.
public sealed record TicketCreated(long TicketId, TicketPriority Priority, DateTime CreatedUtc, long? CategoryId = null) : IIntegrationEvent;

public sealed record FirstAgentReply(long TicketId, DateTime RepliedUtc) : IIntegrationEvent;

public sealed record TicketResolved(long TicketId, DateTime ResolvedUtc) : IIntegrationEvent;

public sealed record TicketReopened(long TicketId, DateTime ReopenedUtc) : IIntegrationEvent;

public sealed record TicketCancelled(long TicketId, DateTime CancelledUtc) : IIntegrationEvent;

// A team lead changed the ticket's category (null: no category). Sla may have a different SLA for the new one.
public sealed record TicketCategoryChanged(long TicketId, long? CategoryId, DateTime ChangedUtc) : IIntegrationEvent;

public sealed record TicketPriorityChanged(long TicketId, TicketPriority Priority, DateTime ChangedUtc) : IIntegrationEvent;

// The ticket is waiting on the requester, and its SLA clock stops until the requester answers (or the ticket moves on).
public sealed record TicketPending(long TicketId, DateTime PendingUtc) : IIntegrationEvent;

// The ticket is no longer waiting on the requester: its SLA clock runs again.
public sealed record TicketResumed(long TicketId, DateTime ResumedUtc) : IIntegrationEvent;
