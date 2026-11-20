namespace Yasda.Modules.Tickets.Domain;

internal readonly record struct TicketId(long Value);

// Owned by the Identity module, referenced by id only.
internal readonly record struct UserId(long Value);

internal readonly record struct CategoryId(long Value);

// Owned by the Identity module, referenced by id only.
internal readonly record struct TeamId(long Value);

