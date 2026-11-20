using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Identity.Domain;

// Every business rule Identity can refuse with. Add new codes at the end; never renumber or reuse one.
internal static class IdentityErrors
{
    public static readonly ErrorCode UnknownRole = new("IDENTITY-0001", "Unknown role",
        "Choose one of the roles in the list.");
    public static readonly ErrorCode CannotChangeOwnRole = new("IDENTITY-0002", "You can't change your own role",
        "Ask another admin to change it, so no one can promote themselves.");
    public static readonly ErrorCode CannotDisableSelf = new("IDENTITY-0003", "You can't disable your own account",
        "Ask another admin, so the company can't lose its last way in by accident.");
    public static readonly ErrorCode LastAdmin = new("IDENTITY-0004", "This is the last active admin",
        "Make someone else an admin first, then change or disable this account.");
    public static readonly ErrorCode AccountChangeRefused = new("IDENTITY-0005", "The account change was refused",
        "The message says why: usually the password is too weak (12+ characters, upper and lower case, a digit and a symbol) or the user name is taken.");
    public static readonly ErrorCode TeamNameRequired = new("IDENTITY-0006", "A team needs a name",
        "Give the team a name of 1 to 100 characters, such as \"Service desk\".");
    public static readonly ErrorCode TeamNameTaken = new("IDENTITY-0007", "A team with that name already exists",
        "Choose another name, or open the existing team and add people to it.");
    public static readonly ErrorCode TeamMemberNotStaff = new("IDENTITY-0008", "Only staff can be in a team",
        "Choose an active agent, team lead or admin. Requesters and disabled accounts can't be added.");
}

