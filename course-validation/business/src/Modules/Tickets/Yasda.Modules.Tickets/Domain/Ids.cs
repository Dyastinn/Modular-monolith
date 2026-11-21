namespace Yasda.Modules.Tickets.Domain;

internal readonly record struct TicketId(long Value);

// Owned by the Identity module, referenced by id only.
internal readonly record struct UserId(long Value);

internal readonly record struct CategoryId(long Value);

// There is one set of automatic rules for the whole installation, so its id is always 1.
internal readonly record struct AutomationSettingsId(int Value);

// Owned by the Identity module, referenced by id only.
internal readonly record struct TeamId(long Value);

