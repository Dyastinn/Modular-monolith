using Yasda.Modules.Identity.Contracts;
using Yasda.Modules.Tickets.Domain;
using Yasda.Modules.Tickets.Infrastructure;
using Yasda.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Yasda.Modules.Tickets.Features;

internal sealed record CreateCategoryCommand(string Name, long? TeamId);

internal sealed class CreateCategoryHandler(TicketsDbContext db, ITeamDirectory teams, TimeProvider time)
{
    public async Task<long> HandleAsync(CreateCategoryCommand cmd, CancellationToken ct)
    {
        // The database knows whether the name is taken, and Identity which team this is; the category decides what is allowed.
        var pattern = SearchText.ForLike(cmd.Name);
        var nameTaken = pattern is not null && await db.Categories.AnyAsync(c => EF.Functions.ILike(c.Name, pattern), ct);
        var team = await TeamFacts.FindAsync(teams, cmd.TeamId, ct);

        var category = Category.Create(cmd.Name, nameTaken, team, time.GetUtcNow().UtcDateTime);

        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);
        return category.Id.Value;
    }
}
