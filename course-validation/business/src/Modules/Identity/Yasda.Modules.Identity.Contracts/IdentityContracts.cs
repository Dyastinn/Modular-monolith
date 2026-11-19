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
