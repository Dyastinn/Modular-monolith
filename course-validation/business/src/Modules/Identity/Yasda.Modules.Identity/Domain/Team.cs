using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Identity.Domain;

// A group of staff who work a set of tickets together. Admins manage teams and who is in them. The domain decides
// every rule; the handlers only tell it what they looked up (is this name taken, is this person staff).
internal sealed class Team : AggregateRoot<TeamId>
{
    public const int NameMaxLength = 100;

    private readonly List<TeamMember> _members = [];

    private Team() { }

    public string Name { get; private set; } = "";
    public bool IsActive { get; private set; }
    public NotifyMode NotifyMode { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public IReadOnlyList<TeamMember> Members => _members;

    public static Team Create(string name, bool nameTaken, DateTime nowUtc)
    {
        return new Team
        {
            Name = CheckedName(name, nameTaken),
            IsActive = true,
            NotifyMode = NotifyMode.LeadsOnly,
            UpdatedUtc = nowUtc,
        };
    }

    public void Rename(string name, bool nameTaken, DateTime nowUtc)
    {
        var checkedName = CheckedName(name, nameTaken);
        if (checkedName == Name) return;

        Name = checkedName;
        UpdatedUtc = nowUtc;
        Raise(new TeamRenamedEvent(Id, checkedName));
    }

    public void SetActive(bool isActive, DateTime nowUtc)
    {
        IsActive = isActive;
        UpdatedUtc = nowUtc;
    }

    // Only staff may be in a team, and Identity is the one who knows who is staff. Adding someone twice changes nothing.
    public void AddMember(UserId user, bool userIsStaff, DateTime nowUtc)
    {
        if (!userIsStaff)
            throw new DomainException(IdentityErrors.TeamMemberNotStaff);
        if (IsMember(user)) return;

        _members.Add(new TeamMember(user));
        UpdatedUtc = nowUtc;
    }

    public void RemoveMember(UserId user, DateTime nowUtc)
    {
        var removed = _members.RemoveAll(m => m.UserId == user);
        if (removed == 0) return;

        UpdatedUtc = nowUtc;
    }

    // Who else hears about unowned work besides the leads. Only members can be selected; the others are ignored.
    public void SetNotify(NotifyMode mode, IReadOnlyCollection<UserId> selected, DateTime nowUtc)
    {
        NotifyMode = mode;
        foreach (var member in _members)
            member.Notify = selected.Contains(member.UserId);
        UpdatedUtc = nowUtc;
    }

    public bool IsMember(UserId user)
    {
        foreach (var member in _members)
        {
            if (member.UserId == user) return true;
        }
        return false;
    }

    private static string CheckedName(string name, bool nameTaken)
    {
        var text = name?.Trim() ?? "";
        if (text.Length == 0 || text.Length > NameMaxLength)
            throw new DomainException(IdentityErrors.TeamNameRequired);
        if (nameTaken)
            throw new DomainException(IdentityErrors.TeamNameTaken);
        return text;
    }
}
