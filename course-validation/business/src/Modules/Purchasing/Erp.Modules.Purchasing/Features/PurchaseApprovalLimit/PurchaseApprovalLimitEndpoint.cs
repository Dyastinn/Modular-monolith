using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Purchasing.Features;

internal sealed record SetPurchaseApprovalLimitRequest(decimal ApprovalThreshold);

internal sealed class SetPurchaseApprovalLimitRequestValidator : AbstractValidator<SetPurchaseApprovalLimitRequest>
{
    public SetPurchaseApprovalLimitRequestValidator() => RuleFor(x => x.ApprovalThreshold).GreaterThanOrEqualTo(0);
}

internal static class PurchaseApprovalLimitEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/purchasing-settings/approval-limit", async (GetPurchaseApprovalLimitHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(ct)))
            .RequireAuthorization(p => p.RequireRole(PurchasingRoles.Readers));

        // Admin only: the limit decides which spending an admin never sees.
        app.MapPut("/purchasing-settings/approval-limit", async Task<NoContent> (SetPurchaseApprovalLimitRequest body, SetPurchaseApprovalLimitHandler handler, CancellationToken ct) =>
            {
                await handler.HandleAsync(new SetPurchaseApprovalLimitCommand(body.ApprovalThreshold), ct);
                return TypedResults.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));
    }
}
