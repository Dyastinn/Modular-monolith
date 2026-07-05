using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Shared.Messaging;

public sealed class Sender(IServiceProvider provider) : ISender
{
    // A cache: one small "wrapper" per request type, so we only do the tricky reflection once per type.
    private static readonly ConcurrentDictionary<Type, RequestHandlerWrapper> _wrappers = new();

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
    {
        var wrapper = _wrappers.GetOrAdd(
            request.GetType(),
            static requestType =>
            {
                var wrapperType = typeof(RequestHandlerWrapperImpl<,>)
                    .MakeGenericType(requestType, typeof(TResponse));
                return (RequestHandlerWrapper)Activator.CreateInstance(wrapperType)!;
            });

        return ((RequestHandlerWrapperImpl<TResponse>)wrapper).Handle(request, provider, ct);
    }
}

// Two small helper base classes so we can keep differently-typed wrappers in one dictionary.
internal abstract class RequestHandlerWrapper { }

internal abstract class RequestHandlerWrapperImpl<TResponse> : RequestHandlerWrapper
{
    public abstract Task<TResponse> Handle(IRequest<TResponse> request, IServiceProvider sp, CancellationToken ct);
}

internal sealed class RequestHandlerWrapperImpl<TRequest, TResponse>
    : RequestHandlerWrapperImpl<TResponse>
    where TRequest : IRequest<TResponse>
{
    public override Task<TResponse> Handle(IRequest<TResponse> request, IServiceProvider sp, CancellationToken ct)
    {
        // 1) find the ONE handler registered for this request type
        var handler = sp.GetRequiredService<IRequestHandler<TRequest, TResponse>>();
        // 2) the innermost step is the handler itself
        RequestHandlerDelegate<TResponse> pipeline = () => handler.Handle((TRequest)request, ct);
        // 3) wrap it in the behaviors (reversed, so the FIRST-registered ends up OUTERMOST)
        foreach (var behavior in sp.GetServices<IPipelineBehavior<TRequest, TResponse>>().Reverse())
        {
            var next = pipeline;
            pipeline = () => behavior.Handle((TRequest)request, next, ct);
        }
        // 4) run the whole nested chain and return its result
        return pipeline();
    }
}
