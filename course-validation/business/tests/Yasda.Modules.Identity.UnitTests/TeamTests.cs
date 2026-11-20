using AwesomeAssertions;
using Yasda.Modules.Identity.Domain;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Identity.UnitTests;

// A team's rules: it needs a name nobody else has, and only staff can be in it.
public class TeamTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc);
    private static readonly UserId Alan = new(3), Lea = new(4), Kira = new(6);

    private static ErrorCode CodeOf(Action act)
    {
        var exception = FluentActions.Invoking(act).Should().Throw<DomainException>().Which;
        return exception.Error;
    }

    private static Team NewTeam()
    {
        return Team.Create("Service desk", nameTaken: false, Now);
    }

    [Fact]
    public void A_new_team_is_active_and_tells_only_its_leads()
    {
        var team = NewTeam();

        team.Name.Should().Be("Service desk");
        team.IsActive.Should().BeTrue();
        team.NotifyMode.Should().Be(NotifyMode.LeadsOnly);
        team.Members.Should().BeEmpty();
    }

    [Fact]
    public void The_name_is_trimmed()
    {
        Team.Create("  Network  ", nameTaken: false, Now).Name.Should().Be("Network");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_team_needs_a_name(string name)
    {
        Action create = () => Team.Create(name, nameTaken: false, Now);

        CodeOf(create).Should().Be(IdentityErrors.TeamNameRequired);
    }

    [Fact]
    public void A_name_of_exactly_100_characters_is_fine_and_101_is_too_long()
    {
        Team.Create(new string('a', 100), nameTaken: false, Now).Name.Should().HaveLength(100);

        Action tooLong = () => Team.Create(new string('a', 101), nameTaken: false, Now);
        CodeOf(tooLong).Should().Be(IdentityErrors.TeamNameRequired);
    }

    [Fact]
    public void A_name_another_team_has_is_refused()
    {
        Action create = () => Team.Create("Network", nameTaken: true, Now);

        CodeOf(create).Should().Be(IdentityErrors.TeamNameTaken);
    }

    [Fact]
    public void Renaming_changes_the_name_and_touches_the_team()
    {
        var team = NewTeam();

        team.Rename("Help desk", nameTaken: false, Now.AddHours(1));

        team.Name.Should().Be("Help desk");
        team.UpdatedUtc.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void Renaming_to_the_same_name_changes_nothing()
    {
        var team = NewTeam();

        team.Rename("  Service desk ", nameTaken: false, Now.AddHours(1));

        team.UpdatedUtc.Should().Be(Now);
        team.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Renaming_raises_one_event_with_the_new_name()
    {
        var team = NewTeam();

        team.Rename(" Help desk ", nameTaken: false, Now.AddHours(1));

        team.DomainEvents.Should().ContainSingle().Which.Should().Be(new TeamRenamedEvent(team.Id, "Help desk"));
    }

    [Fact]
    public void A_refused_rename_raises_nothing()
    {
        var team = NewTeam();

        Action rename = () => team.Rename("Network", nameTaken: true, Now.AddHours(1));

        CodeOf(rename).Should().Be(IdentityErrors.TeamNameTaken);
        team.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Renaming_to_a_name_another_team_has_is_refused_and_leaves_the_name()
    {
        var team = NewTeam();

        Action rename = () => team.Rename("Network", nameTaken: true, Now.AddHours(1));

        CodeOf(rename).Should().Be(IdentityErrors.TeamNameTaken);
        team.Name.Should().Be("Service desk");
    }

    [Fact]
    public void A_team_can_be_switched_off_and_on()
    {
        var team = NewTeam();

        team.SetActive(false, Now.AddHours(1));
        team.IsActive.Should().BeFalse();

        team.SetActive(true, Now.AddHours(2));
        team.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Staff_can_be_added_and_adding_them_twice_changes_nothing()
    {
        var team = NewTeam();

        team.AddMember(Alan, userIsStaff: true, Now.AddHours(1));
        team.AddMember(Alan, userIsStaff: true, Now.AddHours(2));

        team.Members.Should().ContainSingle();
        team.IsMember(Alan).Should().BeTrue();
        team.UpdatedUtc.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void Someone_who_is_not_staff_cannot_join()
    {
        var team = NewTeam();

        Action add = () => team.AddMember(Kira, userIsStaff: false, Now);

        CodeOf(add).Should().Be(IdentityErrors.TeamMemberNotStaff);
        team.Members.Should().BeEmpty();
    }

    [Fact]
    public void A_member_can_be_removed_and_removing_a_stranger_changes_nothing()
    {
        var team = NewTeam();
        team.AddMember(Alan, userIsStaff: true, Now);
        team.AddMember(Lea, userIsStaff: true, Now);

        team.RemoveMember(Alan, Now.AddHours(1));
        team.RemoveMember(Kira, Now.AddHours(2));

        team.IsMember(Alan).Should().BeFalse();
        team.IsMember(Lea).Should().BeTrue();
        team.UpdatedUtc.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void Selecting_who_to_tell_sets_the_mode_and_flags_only_the_selected_members()
    {
        var team = NewTeam();
        team.AddMember(Alan, userIsStaff: true, Now);
        team.AddMember(Lea, userIsStaff: true, Now);

        team.SetNotify(NotifyMode.LeadsAndSelected, [Alan], Now.AddHours(1));

        team.NotifyMode.Should().Be(NotifyMode.LeadsAndSelected);
        team.Members.Single(m => m.UserId == Alan).Notify.Should().BeTrue();
        team.Members.Single(m => m.UserId == Lea).Notify.Should().BeFalse();
    }

    [Fact]
    public void Someone_selected_who_is_not_a_member_is_ignored()
    {
        var team = NewTeam();
        team.AddMember(Alan, userIsStaff: true, Now);

        team.SetNotify(NotifyMode.LeadsAndSelected, [Alan, Kira], Now.AddHours(1));

        team.Members.Should().ContainSingle();
        team.IsMember(Kira).Should().BeFalse();
    }

    [Fact]
    public void Switching_back_clears_the_flags()
    {
        var team = NewTeam();
        team.AddMember(Alan, userIsStaff: true, Now);
        team.SetNotify(NotifyMode.LeadsAndSelected, [Alan], Now);

        team.SetNotify(NotifyMode.LeadsOnly, [], Now.AddHours(1));

        team.Members.Single().Notify.Should().BeFalse();
    }
}
