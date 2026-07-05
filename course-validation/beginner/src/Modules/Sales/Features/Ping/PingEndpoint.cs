using Carter;
using Erp.Shared.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features.Ping;

public sealed class PingEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet("/sales/ping",
            async (ISender sender, CancellationToken ct)
                => await sender.Send(new PingQuery(), ct));
}
