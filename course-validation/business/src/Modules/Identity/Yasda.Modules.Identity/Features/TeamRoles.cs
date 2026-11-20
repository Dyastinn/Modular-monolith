using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Identity.Features;

// Who may do what with teams (decision D6): admins manage teams and who is in them. Staff may read them: the queue's team
// filter and the categories screen need the list of teams.
internal static class TeamRoles
{
    public static readonly string[] Readers = [Roles.Agent, Roles.TeamLead, Roles.Admin];
    public static readonly string[] Managers = [Roles.Admin];
}
