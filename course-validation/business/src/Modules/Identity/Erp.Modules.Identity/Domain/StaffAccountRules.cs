using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Identity.Domain;

// What an administrator may do to a staff account. The rules that stop a company locking itself out.
internal static class StaffAccountRules
{
    public static void EnsureRoleExists(string role)
    {
        if (!Roles.All.Contains(role)) throw new DomainException(IdentityErrors.UnknownRole, $"'{role}' is not a role.");
    }

    public static void EnsureCanChangeRole(long actorId, long targetId, string currentRole, string newRole, int activeAdmins)
    {
        EnsureRoleExists(newRole);
        if (currentRole == newRole) return;
        if (actorId == targetId) throw new DomainException(IdentityErrors.CannotChangeOwnRole, "You can't change your own role. Ask another admin.");
        EnsureNotLastAdmin(currentRole, activeAdmins);
    }

    public static void EnsureCanDisable(long actorId, long targetId, string targetRole, int activeAdmins)
    {
        if (actorId == targetId) throw new DomainException(IdentityErrors.CannotDisableSelf, "You can't disable your own account.");
        EnsureNotLastAdmin(targetRole, activeAdmins);
    }

    private static void EnsureNotLastAdmin(string role, int activeAdmins)
    {
        if (role == Roles.Admin && activeAdmins <= 1)
            throw new DomainException(IdentityErrors.LastAdmin, "This is the last active admin. Make someone else an admin first.");
    }
}
