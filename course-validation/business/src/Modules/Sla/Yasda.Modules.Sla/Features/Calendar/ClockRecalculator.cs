using Yasda.Modules.Sla.Domain;
using Yasda.Modules.Sla.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Yasda.Modules.Sla.Features;

// When the calendar or a policy changes, every clock that is still running is worked out again under the new rules (decision
// F10). The caller has changed the calendar or the policy but not saved yet, so the new rules and the new due times are
// saved together, or not at all.
//
// Now, let's be honest about the cost here: this loads every running clock into memory. For a help desk with a few thousand open
// tickets that is fine and keeps the code plain; with hundreds of thousands it would have to move into SQL.
internal sealed class ClockRecalculator(SlaDbContext db)
{
    // The clock's category policy for its priority, or null when its category has none.
    private static CategoryPolicy? FindOwnPolicy(List<CategoryPolicy> categoryPolicies, SlaClock clock)
    {
        if (clock.CategoryId is null) return null;
        foreach (var policy in categoryPolicies)
        {
            if (policy.CategoryId == clock.CategoryId && policy.Priority == clock.Priority) return policy;
        }
        return null;
    }

    public async Task RecalculateAsync(BusinessCalendar calendar, CancellationToken ct)
    {
        var policies = await db.Policies.ToListAsync(ct);
        var categoryPolicies = await db.CategoryPolicies.ToListAsync(ct);
        var running = await db.Clocks
            .Where(c => !c.Cancelled && (c.RespondedUtc == null || c.ResolvedUtc == null))
            .ToListAsync(ct);

        foreach (var clock in running)
        {
            var respond = TimeSpan.Zero;
            var resolve = TimeSpan.Zero;
            var own = FindOwnPolicy(categoryPolicies, clock);
            if (own is not null)
            {
                respond = own.RespondWithin;
                resolve = own.ResolveWithin;
            }
            else
            {
                var policy = policies.Single(p => p.Priority == clock.Priority);
                respond = policy.RespondWithin;
                resolve = policy.ResolveWithin;
            }
            clock.Recalculate(respond, resolve, calendar);
        }
    }
}
