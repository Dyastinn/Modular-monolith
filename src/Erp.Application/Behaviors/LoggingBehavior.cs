using System.Diagnostics;
using Erp.Application.Abstractions.Messaging;
using Microsoft.Extensions.Logging;

namespace Erp.Application.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var name = typeof(TRequest).Name;
        logger.LogInformation("Handling {Request}", name);
        var stopwatch = Stopwatch.StartNew();
        var response = await next();
        logger.LogInformation("Handled {Request} in {Ms}ms", name, stopwatch.ElapsedMilliseconds);
        return response;
    }
}
