using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Identity.Contracts;

// Identity's service window: what other modules may ask about people, and nothing more.
public interface IUserDirectory
{
    // The account with this email (any case), or null. For the development seed today; inbound email later.
    Task<long?> IdOfAsync(string email, CancellationToken ct);

    // The name to show for a user, or null if there's no such user.
    Task<string?> NameOfAsync(long userId, CancellationToken ct);

    // An active account that may handle tickets (Agent, TeamLead or Admin), or null.
    Task<StaffMember?> FindStaffAsync(long userId, CancellationToken ct);
}

public sealed record StaffMember(long Id, string FullName, string Role);

// What other modules may ask about teams. Teams belong to Identity; Tickets keeps only a team's number and a copy of its name.
public interface ITeamDirectory
{
    // The team with this number, or null.
    Task<TeamInfo?> FindTeamAsync(long teamId, CancellationToken ct);

    // The number of the team with this name (any case), or null. For the development seed, as IdOfAsync is for people.
    Task<long?> TeamIdOfAsync(string name, CancellationToken ct);

    Task<bool> IsMemberAsync(long teamId, long userId, CancellationToken ct);

    // The teams this person is a member of.
    Task<IReadOnlyList<long>> TeamIdsOfAsync(long userId, CancellationToken ct);

    // Who hears about work nobody has taken yet (decision D9): the team's leads always, plus the members its notify mode
    // adds. For work with no team, every active team lead. Only active accounts. Identity decides the list; the caller
    // only asks for it.
    Task<IReadOnlyList<long>> NotifyListAsync(long? teamId, CancellationToken ct);
}

public sealed record TeamInfo(long Id, string Name, bool IsActive);

// A team was renamed. Tickets keeps a copy of each team's name, so it hears about this. The name is a convenience: a
// listener that needs the truth asks ITeamDirectory, since two renames can be delivered in either order.
public sealed record TeamRenamed(long TeamId, string Name) : IIntegrationEvent;

