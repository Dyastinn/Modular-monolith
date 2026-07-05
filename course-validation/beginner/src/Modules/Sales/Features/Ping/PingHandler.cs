using Erp.Shared.Messaging;

namespace Erp.Modules.Sales.Features.Ping;

public sealed record PingQuery : IRequest<string>;

public sealed class PingHandler : IRequestHandler<PingQuery, string>
{
    public Task<string> Handle(PingQuery request, CancellationToken ct)
        => Task.FromResult("pong");
}
