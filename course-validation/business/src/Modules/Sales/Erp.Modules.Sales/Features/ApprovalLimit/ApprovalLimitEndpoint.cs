using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

internal sealed record SetApprovalLimitRequest(decimal ApprovalThreshold);

internal sealed class SetApprovalLimitRequestValidator : AbstractValidator<SetApprovalLimitRequest>
{
    public SetApprovalLimitRequestValidator() => RuleFor(x => x.ApprovalThreshold).GreaterThanOrEqualTo(0);
}

internal static class ApprovalLimitEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/sales-settings/approval-limit", async (GetApprovalLimitHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(ct)))
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Readers));

        // Admin only: a manager raising the limit would be choosing which orders they no longer see.
        app.MapPut("/sales-settings/approval-limit", async Task<NoContent> (SetApprovalLimitRequest body, SetApprovalLimitHandler handler, CancellationToken ct) =>
            {
                await handler.HandleAsync(new SetApprovalLimitCommand(body.ApprovalThreshold), ct);
                return TypedResults.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));
    }
}
