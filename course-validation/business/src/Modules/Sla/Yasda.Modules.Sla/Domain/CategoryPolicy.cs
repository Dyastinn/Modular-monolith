using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Sla.Domain;

// The one rule about a policy's two numbers, shared by the installation's policies and a category's.
internal static class PolicyTargets
{
    public static void Check(TimeSpan respondWithin, TimeSpan resolveWithin)
    {
        if (respondWithin <= TimeSpan.Zero)
            throw new DomainException(SlaErrors.RespondTargetNotPositive);
        if (resolveWithin < respondWithin)
            throw new DomainException(SlaErrors.ResolveBeforeRespond);
    }
}

// A different SLA for one priority of one category: tickets of that category and priority use these numbers instead of the
// installation's. A row an admin or team lead edits on a screen. Removing it sends the category back to the installation's policy.
internal sealed class CategoryPolicy
{
    private CategoryPolicy() { }

    public CategoryId CategoryId { get; private set; }
    public Priority Priority { get; private set; }
    public TimeSpan RespondWithin { get; private set; }
    public TimeSpan ResolveWithin { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public UserId UpdatedById { get; private set; }
    public string UpdatedByName { get; private set; } = "";

    public static CategoryPolicy Create(CategoryId categoryId, Priority priority, TimeSpan respondWithin, TimeSpan resolveWithin,
        UserId by, string byName, DateTime nowUtc)
    {
        var policy = new CategoryPolicy { CategoryId = categoryId, Priority = priority };
        policy.Change(respondWithin, resolveWithin, by, byName, nowUtc);
        return policy;
    }

    public void Change(TimeSpan respondWithin, TimeSpan resolveWithin, UserId by, string byName, DateTime nowUtc)
    {
        PolicyTargets.Check(respondWithin, resolveWithin);

        RespondWithin = respondWithin;
        ResolveWithin = resolveWithin;
        UpdatedUtc = nowUtc;
        UpdatedById = by;
        UpdatedByName = byName;
    }
}
