using Carter;
using Erp.Shared.BuildingBlocks;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Identity.Features;

internal sealed class SearchSignInAttemptsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet("/sign-in-attempts", async (string? email, bool? failedOnly, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new SearchSignInAttemptsQuery(email, failedOnly ?? false, page ?? 1, pageSize ?? 50), ct)))
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));
}
