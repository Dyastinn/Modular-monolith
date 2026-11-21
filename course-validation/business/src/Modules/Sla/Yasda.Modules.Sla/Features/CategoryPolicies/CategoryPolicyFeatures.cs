using Dapper;
using FluentValidation;
using Yasda.Modules.Identity.Contracts;
using Yasda.Modules.Sla.Domain;
using Yasda.Modules.Sla.Infrastructure;
using Yasda.Modules.Tickets.Contracts;
using Yasda.Shared.BuildingBlocks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Yasda.Modules.Sla.Features;

internal sealed record CategoryPolicyDto(long CategoryId, string Priority, int RespondMinutes, int ResolveMinutes, DateTime UpdatedUtc, long UpdatedById, string UpdatedByName);

internal sealed class GetCategoryPoliciesHandler(IDbConnectionFactory connections)
{
    public async Task<List<CategoryPolicyDto>> HandleAsync(CancellationToken ct)
    {
        await using var conn = connections.Create();
        var rows = await conn.QueryAsync<CategoryPolicyDto>(new CommandDefinition("SELECT * FROM sla.get_category_policies()", cancellationToken: ct));
        return rows.ToList();
    }
}

internal sealed record ChangeCategoryPolicyCommand(long CategoryId, Priority Priority, int RespondMinutes, int ResolveMinutes);

// Saving a category's own numbers moves the running tickets of that category and priority (decision F10), in the same save.
internal sealed class ChangeCategoryPolicyHandler(SlaDbContext db, ICurrentUser currentUser, IUserDirectory users, ICategoryDirectory categories,
    TimeProvider time, ClockRecalculator recalculator)
{
    // false when there is no such category.
    public async Task<bool> HandleAsync(ChangeCategoryPolicyCommand cmd, CancellationToken ct)
    {
        if (!await categories.ExistsAsync(cmd.CategoryId, ct)) return false;

        var categoryId = new CategoryId(cmd.CategoryId);
        var myName = await users.NameOfAsync(currentUser.UserId, ct) ?? currentUser.Username;
        var by = new UserId(currentUser.UserId);
        var now = time.GetUtcNow().UtcDateTime;
        var respond = TimeSpan.FromMinutes(cmd.RespondMinutes);
        var resolve = TimeSpan.FromMinutes(cmd.ResolveMinutes);

        var own = await db.CategoryPolicies.SingleOrDefaultAsync(p => p.CategoryId == categoryId && p.Priority == cmd.Priority, ct);
        if (own is null) db.CategoryPolicies.Add(CategoryPolicy.Create(categoryId, cmd.Priority, respond, resolve, by, myName, now));
        else own.Change(respond, resolve, by, myName, now);

        await db.SaveChangesAsync(ct);
        await recalculator.RecalculateAsync(await db.GetBusinessCalendarAsync(ct), ct);
        await db.SaveChangesAsync(ct);
        return true;
    }
}

internal sealed record RemoveCategoryPolicyCommand(long CategoryId, Priority Priority);

// Removing it sends that category and priority back to the installation's policy. Removing one that isn't there changes nothing.
internal sealed class RemoveCategoryPolicyHandler(SlaDbContext db, ClockRecalculator recalculator)
{
    public async Task HandleAsync(RemoveCategoryPolicyCommand cmd, CancellationToken ct)
    {
        var categoryId = new CategoryId(cmd.CategoryId);
        var own = await db.CategoryPolicies.SingleOrDefaultAsync(p => p.CategoryId == categoryId && p.Priority == cmd.Priority, ct);
        if (own is null) return;

        db.CategoryPolicies.Remove(own);
        await db.SaveChangesAsync(ct);
        await recalculator.RecalculateAsync(await db.GetBusinessCalendarAsync(ct), ct);
        await db.SaveChangesAsync(ct);
    }
}

internal sealed record ChangeCategoryPolicyRequest(int RespondMinutes, int ResolveMinutes);

// Only the shape is checked here (no longer than a year), like a policy. Whether the numbers make sense is the policy's rule.
internal sealed class ChangeCategoryPolicyRequestValidator : AbstractValidator<ChangeCategoryPolicyRequest>
{
    private const int MinutesInAYear = 525_600;

    public ChangeCategoryPolicyRequestValidator()
    {
        RuleFor(x => x.RespondMinutes).LessThanOrEqualTo(MinutesInAYear);
        RuleFor(x => x.ResolveMinutes).LessThanOrEqualTo(MinutesInAYear);
    }
}

internal static class CategoryPolicyEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/sla/category-policies", async Task<Ok<List<CategoryPolicyDto>>> (GetCategoryPoliciesHandler handler, CancellationToken ct) =>
        {
            return TypedResults.Ok(await handler.HandleAsync(ct));
        })
        .RequireAuthorization(policy => policy.RequireRole(SlaRoles.Readers));

        // The category and priority are in the address: a priority that is none, or a category that doesn't exist, is a 404.
        app.MapPut("/sla/category-policies/{categoryId:long}/{priority}",
            async Task<Results<NoContent, NotFound>> (long categoryId, string priority, ChangeCategoryPolicyRequest body, ChangeCategoryPolicyHandler handler, CancellationToken ct) =>
        {
            var known = Enum.TryParse<Priority>(priority, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed);
            if (!known) return TypedResults.NotFound();

            var found = await handler.HandleAsync(new ChangeCategoryPolicyCommand(categoryId, parsed, body.RespondMinutes, body.ResolveMinutes), ct);
            if (!found) return TypedResults.NotFound();
            return TypedResults.NoContent();
        })
        .RequireAuthorization(policy => policy.RequireRole(SlaRoles.Editors));

        app.MapDelete("/sla/category-policies/{categoryId:long}/{priority}",
            async Task<Results<NoContent, NotFound>> (long categoryId, string priority, RemoveCategoryPolicyHandler handler, CancellationToken ct) =>
        {
            var known = Enum.TryParse<Priority>(priority, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed);
            if (!known) return TypedResults.NotFound();

            await handler.HandleAsync(new RemoveCategoryPolicyCommand(categoryId, parsed), ct);
            return TypedResults.NoContent();
        })
        .RequireAuthorization(policy => policy.RequireRole(SlaRoles.Editors));
    }
}
