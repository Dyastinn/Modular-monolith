namespace Yasda.Shared.BuildingBlocks;

// The role names, defined once. A typo in a role string silently breaks an auth check, so nobody types them.
public static class Roles
{
    public const string Admin = "Admin";
    public const string TeamLead = "TeamLead";
    public const string Agent = "Agent";
    public const string KbEditor = "KbEditor";
    public const string Requester = "Requester";

    public static readonly string[] All = [Admin, TeamLead, Agent, KbEditor, Requester];
}
