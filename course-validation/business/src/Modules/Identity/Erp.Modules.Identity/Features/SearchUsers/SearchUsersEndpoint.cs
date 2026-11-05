using Carter;
using Erp.Shared.BuildingBlocks;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Identity.Features;

internal sealed class SearchUsersEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/users", async (string? search, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new SearchUsersQuery(search, page ?? 1, pageSize ?? 20), ct)))
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));

        app.MapGet("/users/directory", async (ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetUserDirectoryQuery(), ct)))
            .RequireAuthorization();
    }
}
