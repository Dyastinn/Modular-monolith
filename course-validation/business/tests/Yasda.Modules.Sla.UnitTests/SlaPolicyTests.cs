using AwesomeAssertions;
using Yasda.Modules.Sla.Domain;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Sla.UnitTests;

// The numbers a team lead edits, and the two things that can't be true of them.
public class SlaPolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc);
    private static readonly UserId Lead = new(4);

    private static ErrorCode CodeOf(Action act)
    {
        var exception = FluentActions.Invoking(act).Should().Throw<DomainException>().Which;
        return exception.Error;
    }

    [Fact]
    public void A_policy_keeps_its_numbers_and_who_set_them()
    {
        var policy = SlaPolicy.Create(Priority.High, TimeSpan.FromHours(4), TimeSpan.FromHours(24), Lead, "Lea Santos", Now);

        policy.RespondWithin.Should().Be(TimeSpan.FromHours(4));
        policy.ResolveWithin.Should().Be(TimeSpan.FromHours(24));
        policy.UpdatedById.Should().Be(Lead);
        policy.UpdatedByName.Should().Be("Lea Santos");
        policy.UpdatedUtc.Should().Be(Now);
    }

    [Fact]
    public void Changing_a_policy_records_who_and_when()
    {
        var policy = SlaPolicy.Create(Priority.High, TimeSpan.FromHours(4), TimeSpan.FromHours(24), Lead, "Lea Santos", Now);

        policy.Change(TimeSpan.FromHours(2), TimeSpan.FromHours(12), new UserId(9), "Ana Reyes", Now.AddDays(1));

        policy.RespondWithin.Should().Be(TimeSpan.FromHours(2));
        policy.UpdatedByName.Should().Be("Ana Reyes");
        policy.UpdatedUtc.Should().Be(Now.AddDays(1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_response_target_that_is_not_above_zero_is_refused(int minutes)
    {
        Action create = () => SlaPolicy.Create(Priority.Low, TimeSpan.FromMinutes(minutes), TimeSpan.FromHours(24), Lead, "Lea Santos", Now);

        CodeOf(create).Should().Be(SlaErrors.RespondTargetNotPositive);
    }

    [Fact]
    public void A_resolve_target_shorter_than_the_response_target_is_refused()
    {
        Action create = () => SlaPolicy.Create(Priority.Low, TimeSpan.FromHours(8), TimeSpan.FromHours(7), Lead, "Lea Santos", Now);

        CodeOf(create).Should().Be(SlaErrors.ResolveBeforeRespond);
    }

    [Fact]
    public void Equal_targets_are_allowed()
    {
        var policy = SlaPolicy.Create(Priority.Low, TimeSpan.FromHours(8), TimeSpan.FromHours(8), Lead, "Lea Santos", Now);

        policy.ResolveWithin.Should().Be(TimeSpan.FromHours(8));
    }

    [Fact]
    public void A_refused_change_leaves_the_policy_as_it_was()
    {
        var policy = SlaPolicy.Create(Priority.High, TimeSpan.FromHours(4), TimeSpan.FromHours(24), Lead, "Lea Santos", Now);

        Action change = () => policy.Change(TimeSpan.FromHours(30), TimeSpan.FromHours(24), new UserId(9), "Ana Reyes", Now.AddDays(1));

        CodeOf(change).Should().Be(SlaErrors.ResolveBeforeRespond);
        policy.RespondWithin.Should().Be(TimeSpan.FromHours(4));
        policy.UpdatedByName.Should().Be("Lea Santos");
    }
}
