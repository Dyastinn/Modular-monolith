using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets.Domain;

// What happened to a ticket, in Tickets' own words. Each is saved to the outbox in the same transaction as the change,
// then delivered after the commit. Domain events are internal: other modules hear about them only through the
// integration events the publishing classes in Features/Events send. Each carries its own time, so a late delivery
// can't move a deadline.
internal sealed record TicketCreatedEvent(TicketId TicketId, TicketPriority Priority, DateTime CreatedUtc) : IDomainEvent;

internal sealed record FirstAgentReplyEvent(TicketId TicketId, DateTime RepliedUtc) : IDomainEvent;

internal sealed record TicketResolvedEvent(TicketId TicketId, DateTime ResolvedUtc) : IDomainEvent;

internal sealed record TicketReopenedEvent(TicketId TicketId, DateTime ReopenedUtc) : IDomainEvent;

internal sealed record TicketCancelledEvent(TicketId TicketId, DateTime CancelledUtc) : IDomainEvent;

internal sealed record TicketPriorityChangedEvent(TicketId TicketId, TicketPriority Priority, DateTime ChangedUtc) : IDomainEvent;
