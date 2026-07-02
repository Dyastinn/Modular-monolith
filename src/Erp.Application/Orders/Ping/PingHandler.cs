using Erp.Application.Abstractions.Messaging;

namespace Erp.Application.Orders.Ping;

public sealed class PingHandler : IRequestHandler<PingQuery, string>
{
    public Task<string> Handle(PingQuery request, CancellationToken cancellationToken) =>
        Task.FromResult("pong");
}
