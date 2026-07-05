using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Shared.Messaging;

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
        var handler = sp.GetRequiredService<IRequestHandler<TRequest, TResponse>>();
        RequestHandlerDelegate<TResponse> pipeline = () => handler.Handle((TRequest)request, ct);
        var behaviors = sp.GetServices<IPipelineBehavior<TRequest, TResponse>>().Reverse();
        foreach (var behavior in behaviors)
        {
            var next = pipeline;
            pipeline = () => behavior.Handle((TRequest)request, next, ct);
        }
        return pipeline();
    }
}
