namespace Yasda.Modules.Identity.Domain;

// One person in a team. Notify is used when the team's mode is LeadsAndSelected.
internal sealed class TeamMember
{
    private TeamMember() { }

    internal TeamMember(UserId userId)
    {
        UserId = userId;
    }

    public UserId UserId { get; private set; }
    public bool Notify { get; internal set; }
}
