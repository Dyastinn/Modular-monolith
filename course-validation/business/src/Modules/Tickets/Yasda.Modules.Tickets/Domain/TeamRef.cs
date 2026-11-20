namespace Yasda.Modules.Tickets.Domain;

// What Tickets knows about a team: the facts Identity told it, at the moment it asked. Tickets keeps the number and a copy of the name.
internal readonly record struct TeamRef(TeamId Id, string Name, bool IsActive);
