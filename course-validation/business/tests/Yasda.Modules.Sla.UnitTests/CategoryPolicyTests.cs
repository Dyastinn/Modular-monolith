using AwesomeAssertions;
using Yasda.Modules.Sla.Domain;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Sla.UnitTests;

public class CategoryPolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 21, 9, 0, 0, DateTimeKind.Utc);
    private static readonly UserId Lead = new(2);

    private static ErrorCode CodeOf(Action act)
    {
        return act.Should().Throw<DomainException>().Which.Error;
    }

    [Fact]
    public void A_category_policy_keeps_its_numbers_and_who_set_them()
    {
        var policy = CategoryPolicy.Create(new CategoryId(9), Priority.High, TimeSpan.FromHours(1), TimeSpan.FromHours(8), Lead, "Lena Torres", Now);

        policy.CategoryId.Should().Be(new CategoryId(9));
        policy.Priority.Should().Be(Priority.High);
        policy.RespondWithin.Should().Be(TimeSpan.FromHours(1));
        policy.ResolveWithin.Should().Be(TimeSpan.FromHours(8));
        policy.UpdatedByName.Should().Be("Lena Torres");
    }

    [Fact]
    public void It_follows_the_same_rule_as_the_installations_policy()
    {
        CodeOf(() => CategoryPolicy.Create(new CategoryId(9), Priority.High, TimeSpan.Zero, TimeSpan.FromHours(8), Lead, "Lena", Now))
            .Should().Be(SlaErrors.RespondTargetNotPositive);
        CodeOf(() => CategoryPolicy.Create(new CategoryId(9), Priority.High, TimeSpan.FromHours(8), TimeSpan.FromHours(1), Lead, "Lena", Now))
            .Should().Be(SlaErrors.ResolveBeforeRespond);
    }

    [Fact]
    public void A_refused_change_leaves_the_numbers_as_they_were()
    {
        var policy = CategoryPolicy.Create(new CategoryId(9), Priority.High, TimeSpan.FromHours(1), TimeSpan.FromHours(8), Lead, "Lena", Now);

        CodeOf(() => policy.Change(TimeSpan.FromHours(5), TimeSpan.FromHours(2), Lead, "Lena", Now.AddHours(1)));

        policy.RespondWithin.Should().Be(TimeSpan.FromHours(1));
        policy.UpdatedUtc.Should().Be(Now);
    }
}
