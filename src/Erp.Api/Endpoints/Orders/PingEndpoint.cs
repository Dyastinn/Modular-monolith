using Erp.Application.Orders.Ping;

namespace Erp.Api.Endpoints.Orders;

// Throwaway wiring: calls PingHandler directly since the real mediator (ISender/Sender) doesn't
// exist until Phase 2. Replace the direct call with sender.Send(new PingQuery(), ct) there.
public static class PingEndpoint
{
    public static void MapPingEndpoint(this WebApplication app)
    {
        app.MapGet("/orders/ping", async (PingHandler handler, CancellationToken ct) =>
            await handler.Handle(new PingQuery(), ct));
    }
}
