using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets.Features;

// Who may do what with tickets, decided once for every endpoint. Anyone signed in may file a ticket and follow
// their own; these roles work on everyone's.
internal static class TicketRoles
{
    public static readonly string[] Handlers = [Roles.Agent, Roles.TeamLead, Roles.Admin];
    public static readonly string[] Leads = [Roles.TeamLead, Roles.Admin];

    public static bool IsHandler(string role)
    {
        return Handlers.Contains(role);
    }
}
