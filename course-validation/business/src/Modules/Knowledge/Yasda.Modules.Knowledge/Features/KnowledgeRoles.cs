using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Knowledge.Features;

// Who may do what with articles. Anyone signed in reads the published ones; staff write; KbEditors and Admins review.
internal static class KnowledgeRoles
{
    public static readonly string[] Writers = [Roles.Agent, Roles.TeamLead, Roles.KbEditor, Roles.Admin];
    public static readonly string[] Editors = [Roles.KbEditor, Roles.Admin];

    public static bool IsWriter(string role)
    {
        return Writers.Contains(role);
    }

    public static bool IsEditor(string role)
    {
        return Editors.Contains(role);
    }
}
