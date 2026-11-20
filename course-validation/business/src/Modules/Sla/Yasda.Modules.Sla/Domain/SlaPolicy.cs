using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Sla.Domain;

// How long a team has for tickets of one priority: to answer the requester, and to resolve the ticket. A row an admin or
// team lead edits on a screen, never a setting in a file. There is one per priority, seeded by the migration, and never deleted.
internal sealed class SlaPolicy
{
    private SlaPolicy() { }

    public Priority Priority { get; private set; }
    public TimeSpan RespondWithin { get; private set; }
    public TimeSpan ResolveWithin { get; private set; }

    // Who changed it last and when, shown beside the numbers.
    public DateTime UpdatedUtc { get; private set; }
    public UserId UpdatedById { get; private set; }
    public string UpdatedByName { get; private set; } = "";

    // The real rows are seeded by the migration, one per priority; Create is how the domain makes one (and how the tests do).
    public static SlaPolicy Create(Priority priority, TimeSpan respondWithin, TimeSpan resolveWithin, UserId by, string byName, DateTime nowUtc)
    {
        var policy = new SlaPolicy { Priority = priority };
        policy.Change(respondWithin, resolveWithin, by, byName, nowUtc);
        return policy;
    }

    public void Change(TimeSpan respondWithin, TimeSpan resolveWithin, UserId by, string byName, DateTime nowUtc)
    {
        if (respondWithin <= TimeSpan.Zero)
            throw new DomainException(SlaErrors.RespondTargetNotPositive);
        if (resolveWithin < respondWithin)
            throw new DomainException(SlaErrors.ResolveBeforeRespond);

        RespondWithin = respondWithin;
        ResolveWithin = resolveWithin;
        UpdatedUtc = nowUtc;
        UpdatedById = by;
        UpdatedByName = byName;
    }
}
