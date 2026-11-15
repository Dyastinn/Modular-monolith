using System.Security.Claims;
using Erp.Shared.BuildingBlocks;

namespace Erp.Web.Shared;

// How the screens talk about a sales order's pending approvals. The API decides; this only picks buttons and words.
public static class ApprovalText
{
    public const string Manager = "Manager", CreditRelease = "CreditRelease";

    // Mirrors the API's ApprovalNeedsRules: managers approve size, accountants release credit, admins do both.
    public static IReadOnlyList<string> GivenBy(ClaimsPrincipal user) =>
        user.IsInRole(Roles.Admin) ? [Manager, CreditRelease]
        : user.IsInRole(Roles.SalesManager) ? [Manager]
        : user.IsInRole(Roles.Accountant) ? [CreditRelease]
        : [];

    public static bool CanGive(ClaimsPrincipal user, IReadOnlyList<string> pending) => pending.Intersect(GivenBy(user)).Any();

    public static string ActionLabel(ClaimsPrincipal user, IReadOnlyList<string> pending) =>
        pending.Intersect(GivenBy(user)).SequenceEqual([CreditRelease]) ? "Release credit" : "Approve";

    public static string Describe(IReadOnlyList<string> pending) =>
        string.Join(" and ", pending.Select(p => p == CreditRelease ? "accounting to release credit" : "a sales manager's approval"));

    public static string Label(string need) => need == CreditRelease ? "Over credit" : "Above limit";
}
