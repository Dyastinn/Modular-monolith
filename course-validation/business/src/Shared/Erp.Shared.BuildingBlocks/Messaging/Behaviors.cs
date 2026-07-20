using System.Diagnostics;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Erp.Shared.BuildingBlocks;

// Cross-cutting behaviors shared by every module (Phase 11 §3-4). Registered once by AddMessaging,
// LoggingBehavior OUTERMOST so it logs even requests that fail validation.
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> log)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var name = typeof(TRequest).Name;
        log.LogInformation("→ {Request}", name);
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await next();
            log.LogInformation("← {Request} in {Ms}ms", name, sw.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "✗ {Request} failed in {Ms}ms", name, sw.ElapsedMilliseconds);
            throw;   // rethrow — logging observes, it doesn't swallow
        }
    }
}

public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (validators.Any())
        {
            var context = new ValidationContext<TRequest>(request);
            var failures = validators
                .Select(v => v.Validate(context))
                .SelectMany(r => r.Errors)
                .Where(f => f is not null)
                .ToList();

            if (failures.Count != 0)
                throw new ValidationException(failures);   // stop here; the handler never runs
        }
        return await next();
    }
}
