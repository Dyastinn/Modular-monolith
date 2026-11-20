using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Sla.Features;

// Who may do what with SLA policies (decision D6): staff read them, team leads and admins change them.
internal static class SlaRoles
{
    public static readonly string[] Readers = [Roles.Agent, Roles.TeamLead, Roles.Admin];
    public static readonly string[] Editors = [Roles.TeamLead, Roles.Admin];
}
