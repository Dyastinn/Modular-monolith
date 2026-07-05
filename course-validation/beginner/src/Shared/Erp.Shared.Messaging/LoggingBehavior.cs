using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Erp.Shared.Messaging;

public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> log)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var name = typeof(TRequest).Name;
        log.LogInformation("Handling {Request}", name);
        var sw = Stopwatch.StartNew();
        var response = await next();      // ← run the next thing in the chain (eventually the handler)
        log.LogInformation("Handled {Request} in {Ms}ms", name, sw.ElapsedMilliseconds);
        return response;
    }
}
