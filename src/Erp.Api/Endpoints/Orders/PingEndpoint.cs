using Erp.Application.Abstractions.Messaging;
using Erp.Application.Orders.Ping;

namespace Erp.Api.Endpoints.Orders;

public static class PingEndpoint
{
    public static void MapPingEndpoint(this WebApplication app)
    {
        app.MapGet("/orders/ping", async (ISender sender, CancellationToken ct) =>
            await sender.Send(new PingQuery(), ct));
    }
}
