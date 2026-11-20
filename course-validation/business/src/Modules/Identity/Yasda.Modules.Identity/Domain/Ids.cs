namespace Yasda.Modules.Identity.Domain;

internal readonly record struct TeamId(long Value);

// An account's number, as a team knows it.
internal readonly record struct UserId(long Value);
