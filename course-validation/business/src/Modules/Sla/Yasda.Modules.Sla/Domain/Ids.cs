namespace Yasda.Modules.Sla.Domain;

// Sla's own ids. The ticket's number is a plain fact Sla was told; the domain never references Tickets' types.
internal readonly record struct TicketId(long Value);

// The person who last changed a policy: a number from Identity, kept without any link to its table.
internal readonly record struct UserId(long Value);

// Tickets' category, referenced by number only.
internal readonly record struct CategoryId(long Value);

// There is one calendar for the whole organization today, so its id is always 1. It is still a row with an id, so a second
// calendar (a branch with other hours) could be added without changing the tables.
internal readonly record struct CalendarId(int Value);
