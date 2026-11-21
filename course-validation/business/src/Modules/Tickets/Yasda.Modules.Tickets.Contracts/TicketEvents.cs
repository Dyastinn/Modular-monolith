using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets.Contracts;

// What Tickets tells the other modules about a ticket. Plain data only, so Tickets can change its domain without
// breaking the modules that listen. Each carries the time it happened, never the time it was delivered: a delivery
// can come late, and a deadline must not move because of it.
public sealed record TicketCreated(long TicketId, TicketPriority Priority, DateTime CreatedUtc) : IIntegrationEvent;

public sealed record FirstAgentReply(long TicketId, DateTime RepliedUtc) : IIntegrationEvent;

public sealed record TicketResolved(long TicketId, DateTime ResolvedUtc) : IIntegrationEvent;

public sealed record TicketReopened(long TicketId, DateTime ReopenedUtc) : IIntegrationEvent;

public sealed record TicketCancelled(long TicketId, DateTime CancelledUtc) : IIntegrationEvent;

public sealed record TicketPriorityChanged(long TicketId, TicketPriority Priority, DateTime ChangedUtc) : IIntegrationEvent;

// The ticket is waiting on the requester, and its SLA clock stops until the requester answers (or the ticket moves on).
public sealed record TicketPending(long TicketId, DateTime PendingUtc) : IIntegrationEvent;

// The ticket is no longer waiting on the requester: its SLA clock runs again.
public sealed record TicketResumed(long TicketId, DateTime ResumedUtc) : IIntegrationEvent;
