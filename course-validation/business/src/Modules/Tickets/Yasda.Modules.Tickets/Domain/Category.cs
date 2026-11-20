using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets.Domain;

// A kind of request ("Hardware", "Access") that decides which team a new ticket goes to: routing is the category's team.
// A category with no team leaves its tickets unrouted, for a team lead to sort. Team leads and admins manage categories.
internal sealed class Category : AggregateRoot<CategoryId>
{
    public const int NameMaxLength = 100;

    private Category() { }

    public string Name { get; private set; } = "";
    public TeamId? TeamId { get; private set; }
    public string? TeamName { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime UpdatedUtc { get; private set; }

    public static Category Create(string name, bool nameTaken, TeamRef? team, DateTime nowUtc)
    {
        var category = new Category
        {
            Name = CheckedName(name, nameTaken),
            IsActive = true,
            UpdatedUtc = nowUtc,
        };
        category.RouteTo(team);
        return category;
    }

    public void Update(string name, bool nameTaken, TeamRef? team, bool isActive, DateTime nowUtc)
    {
        var checkedName = CheckedName(name, nameTaken);
        EnsureTeamAllowed(team);

        Name = checkedName;
        IsActive = isActive;
        RouteTo(team);
        UpdatedUtc = nowUtc;
    }

    // New routing to an inactive team is refused. A category that already points at the team keeps pointing at it, so an
    // inactive team doesn't stop someone renaming the category.
    private void EnsureTeamAllowed(TeamRef? team)
    {
        if (team is null) return;
        if (team.Value.IsActive) return;
        if (TeamId == team.Value.Id) return;

        throw new DomainException(TicketsErrors.TeamInactive);
    }

    private void RouteTo(TeamRef? team)
    {
        EnsureTeamAllowed(team);
        if (team is null)
        {
            TeamId = null;
            TeamName = null;
            return;
        }

        TeamId = team.Value.Id;
        TeamName = team.Value.Name;
    }

    private static string CheckedName(string name, bool nameTaken)
    {
        var text = name?.Trim() ?? "";
        if (text.Length == 0 || text.Length > NameMaxLength)
            throw new DomainException(TicketsErrors.CategoryNameRequired);
        if (nameTaken)
            throw new DomainException(TicketsErrors.CategoryNameTaken);
        return text;
    }
}
