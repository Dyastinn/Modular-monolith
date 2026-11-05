using AwesomeAssertions;
using Erp.Modules.Identity.Domain;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Identity.UnitTests;

public class StaffAccountRulesTests
{
    private const long Me = 3, Someone = 7;

    [Fact]
    public void Nobody_disables_their_own_account() =>
        FluentActions.Invoking(() => StaffAccountRules.EnsureCanDisable(Me, Me, Roles.Admin, activeAdmins: 2))
            .Should().Throw<DomainException>().WithMessage("*your own*");

    [Fact]
    public void The_last_admin_stays()
    {
        FluentActions.Invoking(() => StaffAccountRules.EnsureCanDisable(Me, Someone, Roles.Admin, activeAdmins: 1))
            .Should().Throw<DomainException>().WithMessage("*last active admin*");
        FluentActions.Invoking(() => StaffAccountRules.EnsureCanChangeRole(Me, Someone, Roles.Admin, Roles.SalesRep, activeAdmins: 1))
            .Should().Throw<DomainException>();
        FluentActions.Invoking(() => StaffAccountRules.EnsureCanDisable(Me, Someone, Roles.Admin, activeAdmins: 2))
            .Should().NotThrow();
    }

    [Fact]
    public void Nobody_changes_their_own_role() =>
        FluentActions.Invoking(() => StaffAccountRules.EnsureCanChangeRole(Me, Me, Roles.Admin, Roles.SalesRep, activeAdmins: 3))
            .Should().Throw<DomainException>().WithMessage("*your own role*");

    [Fact]
    public void Only_real_roles_are_accepted() =>
        FluentActions.Invoking(() => StaffAccountRules.EnsureRoleExists("SuperUser")).Should().Throw<DomainException>();
}
