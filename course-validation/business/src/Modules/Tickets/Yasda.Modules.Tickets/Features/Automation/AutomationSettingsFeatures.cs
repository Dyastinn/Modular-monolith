using Dapper;
using FluentValidation;
using Yasda.Modules.Identity.Contracts;
using Yasda.Modules.Tickets.Domain;
using Yasda.Modules.Tickets.Infrastructure;
using Yasda.Shared.BuildingBlocks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Yasda.Modules.Tickets.Features;

internal sealed record AutomationSettingsDto(int? CloseResolvedAfterDays, int? RemindPendingAfterDays, int? ClosePendingAfterDays,
    DateTime UpdatedUtc, long UpdatedById, string UpdatedByName);

internal sealed class GetAutomationSettingsHandler(IDbConnectionFactory connections)
{
    public async Task<AutomationSettingsDto> HandleAsync(CancellationToken ct)
    {
        await using var conn = connections.Create();
        return await conn.QuerySingleAsync<AutomationSettingsDto>(new CommandDefinition("SELECT * FROM tickets.get_automation_settings()", cancellationToken: ct));
    }
}

internal sealed record ChangeAutomationSettingsCommand(int? CloseResolvedAfterDays, int? RemindPendingAfterDays, int? ClosePendingAfterDays);

internal sealed class ChangeAutomationSettingsHandler(TicketsDbContext db, ICurrentUser currentUser, IUserDirectory users, TimeProvider time)
{
    public async Task HandleAsync(ChangeAutomationSettingsCommand cmd, CancellationToken ct)
    {
        var settings = await db.AutomationSettings.SingleAsync(ct);

        var myName = await users.NameOfAsync(currentUser.UserId, ct) ?? currentUser.Username;
        settings.Change(cmd.CloseResolvedAfterDays, cmd.RemindPendingAfterDays, cmd.ClosePendingAfterDays,
            new UserId(currentUser.UserId), myName, time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
    }
}

internal sealed record ChangeAutomationSettingsRequest(int? CloseResolvedAfterDays, int? RemindPendingAfterDays, int? ClosePendingAfterDays);

// Only the shape is checked here: a rule is off (no number) or a number of days from 1 to a year. Whether the numbers fit together
// is the settings' rule (TICKETS-0018).
internal sealed class ChangeAutomationSettingsRequestValidator : AbstractValidator<ChangeAutomationSettingsRequest>
{
    private const int MostDays = 365;

    public ChangeAutomationSettingsRequestValidator()
    {
        RuleFor(x => x.CloseResolvedAfterDays).InclusiveBetween(1, MostDays).When(x => x.CloseResolvedAfterDays is not null);
        RuleFor(x => x.RemindPendingAfterDays).InclusiveBetween(1, MostDays).When(x => x.RemindPendingAfterDays is not null);
        RuleFor(x => x.ClosePendingAfterDays).InclusiveBetween(1, MostDays).When(x => x.ClosePendingAfterDays is not null);
    }
}

internal static class AutomationSettingsEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/automation", async Task<Ok<AutomationSettingsDto>> (GetAutomationSettingsHandler handler, CancellationToken ct) =>
        {
            return TypedResults.Ok(await handler.HandleAsync(ct));
        })
        .RequireAuthorization(policy => policy.RequireRole(Roles.Admin));

        app.MapPut("/automation", async Task<NoContent> (ChangeAutomationSettingsRequest body, ChangeAutomationSettingsHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new ChangeAutomationSettingsCommand(body.CloseResolvedAfterDays, body.RemindPendingAfterDays, body.ClosePendingAfterDays), ct);
            return TypedResults.NoContent();
        })
        .RequireAuthorization(policy => policy.RequireRole(Roles.Admin));
    }
}
