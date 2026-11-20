namespace Yasda.Modules.Sla.Domain;

// Sla's own ids. The ticket's number is a plain fact Sla was told; the domain never references Tickets' types.
internal readonly record struct TicketId(long Value);

// The person who last changed a policy: a number from Identity, kept without any link to its table.
internal readonly record struct UserId(long Value);
