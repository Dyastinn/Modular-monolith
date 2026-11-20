using AwesomeAssertions;
using Yasda.Modules.Tickets.Domain;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets.UnitTests;

// A category decides which team a new ticket goes to. It needs a name nobody else has, and it can't send tickets to a team that
// has been switched off.
public class CategoryTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc);
    private static readonly TeamRef ServiceDesk = new(new TeamId(1), "Service desk", IsActive: true);
    private static readonly TeamRef Network = new(new TeamId(2), "Network", IsActive: true);
    private static readonly TeamRef Closed = new(new TeamId(3), "Old team", IsActive: false);

    private static ErrorCode CodeOf(Action act)
    {
        var exception = FluentActions.Invoking(act).Should().Throw<DomainException>().Which;
        return exception.Error;
    }

    [Fact]
    public void A_new_category_is_active_and_routes_to_its_team()
    {
        var category = Category.Create("  Hardware ", nameTaken: false, ServiceDesk, Now);

        category.Name.Should().Be("Hardware");
        category.IsActive.Should().BeTrue();
        category.TeamId.Should().Be(new TeamId(1));
        category.TeamName.Should().Be("Service desk");
    }

    [Fact]
    public void A_category_with_no_team_leaves_its_tickets_unrouted()
    {
        var category = Category.Create("Other", nameTaken: false, team: null, Now);

        category.TeamId.Should().BeNull();
        category.TeamName.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_category_needs_a_name(string name)
    {
        Action create = () => Category.Create(name, nameTaken: false, ServiceDesk, Now);

        CodeOf(create).Should().Be(TicketsErrors.CategoryNameRequired);
    }

    [Fact]
    public void A_name_of_exactly_100_characters_is_fine_and_101_is_too_long()
    {
        Category.Create(new string('a', 100), nameTaken: false, ServiceDesk, Now).Name.Should().HaveLength(100);

        Action tooLong = () => Category.Create(new string('a', 101), nameTaken: false, ServiceDesk, Now);
        CodeOf(tooLong).Should().Be(TicketsErrors.CategoryNameRequired);
    }

    [Fact]
    public void A_name_another_category_has_is_refused()
    {
        Action create = () => Category.Create("Hardware", nameTaken: true, ServiceDesk, Now);

        CodeOf(create).Should().Be(TicketsErrors.CategoryNameTaken);
    }

    [Fact]
    public void A_new_category_cannot_route_to_an_inactive_team()
    {
        Action create = () => Category.Create("Old stuff", nameTaken: false, Closed, Now);

        CodeOf(create).Should().Be(TicketsErrors.TeamInactive);
    }

    [Fact]
    public void Updating_changes_the_name_the_team_and_whether_it_is_active()
    {
        var category = Category.Create("Hardware", nameTaken: false, ServiceDesk, Now);

        category.Update("Equipment", nameTaken: false, Network, isActive: false, Now.AddHours(1));

        category.Name.Should().Be("Equipment");
        category.TeamId.Should().Be(new TeamId(2));
        category.TeamName.Should().Be("Network");
        category.IsActive.Should().BeFalse();
        category.UpdatedUtc.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void Updating_can_take_the_team_away()
    {
        var category = Category.Create("Hardware", nameTaken: false, ServiceDesk, Now);

        category.Update("Hardware", nameTaken: false, team: null, isActive: true, Now.AddHours(1));

        category.TeamId.Should().BeNull();
        category.TeamName.Should().BeNull();
    }

    [Fact]
    public void A_category_cannot_be_moved_to_an_inactive_team_and_stays_as_it_was()
    {
        var category = Category.Create("Hardware", nameTaken: false, ServiceDesk, Now);

        Action move = () => category.Update("Hardware", nameTaken: false, Closed, isActive: true, Now.AddHours(1));

        CodeOf(move).Should().Be(TicketsErrors.TeamInactive);
        category.TeamId.Should().Be(new TeamId(1));
    }

    [Fact]
    public void A_category_already_pointing_at_a_team_can_be_renamed_after_the_team_goes_inactive()
    {
        var category = Category.Create("Legacy", nameTaken: false, new TeamRef(new TeamId(3), "Old team", IsActive: true), Now);

        category.Update("Legacy hardware", nameTaken: false, Closed, isActive: true, Now.AddHours(1));

        category.Name.Should().Be("Legacy hardware");
        category.TeamId.Should().Be(new TeamId(3));
    }

    [Fact]
    public void A_refused_rename_changes_nothing()
    {
        var category = Category.Create("Hardware", nameTaken: false, ServiceDesk, Now);

        Action rename = () => category.Update("Software", nameTaken: true, ServiceDesk, isActive: false, Now.AddHours(1));

        CodeOf(rename).Should().Be(TicketsErrors.CategoryNameTaken);
        category.Name.Should().Be("Hardware");
        category.IsActive.Should().BeTrue();
    }
}
