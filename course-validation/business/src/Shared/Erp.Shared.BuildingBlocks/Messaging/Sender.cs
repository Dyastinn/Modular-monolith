using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Shared.BuildingBlocks;

// The hand-rolled mediator (Phase 2, Option A). Finds the handler for a request type, wraps it in the
// registered behaviors, and runs it. One wrapper is cached per concrete request type.
public sealed class Sender(IServiceProvider provider) : ISender
{
    private static readonly ConcurrentDictionary<Type, RequestHandlerWrapper> _wrappers = new();

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
    {
        var wrapper = _wrappers.GetOrAdd(
            request.GetType(),
            static requestType =>
            {
                var responseType = typeof(TResponse);
                var wrapperType = typeof(RequestHandlerWrapperImpl<,>)
                    .MakeGenericType(requestType, responseType);
                return (RequestHandlerWrapper)Activator.CreateInstance(wrapperType)!;
            });

        return ((RequestHandlerWrapperImpl<TResponse>)wrapper).Handle(request, provider, ct);
    }
}

// Non-generic base so we can store different closed types in one dictionary.
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
        // 1) Resolve the one handler for this request type.
        var handler = sp.GetRequiredService<IRequestHandler<TRequest, TResponse>>();

        // 2) The innermost call is the handler itself.
        RequestHandlerDelegate<TResponse> pipeline = () => handler.Handle((TRequest)request, ct);

        // 3) Wrap it in behaviors, OUTERMOST LAST so the first-registered runs first.
        var behaviors = sp.GetServices<IPipelineBehavior<TRequest, TResponse>>().Reverse();
        foreach (var behavior in behaviors)
        {
            var next = pipeline;
            pipeline = () => behavior.Handle((TRequest)request, next, ct);
        }

        // 4) Run the whole nested chain.
        return pipeline();
    }
}
