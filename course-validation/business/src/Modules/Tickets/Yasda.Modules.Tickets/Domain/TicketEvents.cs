using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets.Domain;

// What happened to a ticket, in Tickets' own words. Each is saved to the outbox in the same transaction as the change,
// then delivered after the commit. Domain events are internal: other modules hear about them only through the
// integration events the publishing classes in Features/Events send. Each carries its own time, so a late delivery
// can't move a deadline.
// EventId names this one happening. Mail is keyed by it, so a redelivered event queues each mail once.
internal sealed record TicketCreatedEvent(Guid EventId, TicketId TicketId, string Subject, TeamId? TeamId, UserId RequesterId,
    TicketPriority Priority, DateTime CreatedUtc) : IDomainEvent;

internal sealed record FirstAgentReplyEvent(TicketId TicketId, DateTime RepliedUtc) : IDomainEvent;

internal sealed record TicketResolvedEvent(TicketId TicketId, DateTime ResolvedUtc) : IDomainEvent;

internal sealed record TicketReopenedEvent(TicketId TicketId, DateTime ReopenedUtc) : IDomainEvent;

internal sealed record TicketCancelledEvent(TicketId TicketId, DateTime CancelledUtc) : IDomainEvent;

internal sealed record TicketPriorityChangedEvent(TicketId TicketId, TicketPriority Priority, DateTime ChangedUtc) : IDomainEvent;

internal sealed record TicketPendingEvent(TicketId TicketId, DateTime PendingUtc) : IDomainEvent;

internal sealed record TicketResumedEvent(TicketId TicketId, DateTime ResumedUtc) : IDomainEvent;

// Someone now has the ticket. Self-assignment (Take, or an agent's reply to an unowned ticket) raises it too, with
// AssignedById equal to AssigneeId; the mail handler skips that case.
internal sealed record TicketAssignedEvent(Guid EventId, TicketId TicketId, string Subject, UserId AssigneeId, UserId AssignedById) : IDomainEvent;

// A message was added to the conversation (internal notes too: the mail handler decides what to do with them).
// TeamId is the ticket's team when the reply was written, for the people to tell when nobody has the ticket.
internal sealed record ReplyAddedEvent(Guid EventId, TicketId TicketId, string Subject, UserId AuthorId, bool AuthorIsStaff,
    bool IsInternal, string Body, UserId RequesterId, UserId? AssigneeId, TeamId? TeamId) : IDomainEvent;
