using Erp.Application.Abstractions.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Infrastructure.Messaging;

internal sealed class RequestHandlerWrapperImpl<TRequest, TResponse> : RequestHandlerWrapper<TResponse>
    where TRequest : IRequest<TResponse>
{
    public override Task<TResponse> Handle(IRequest<TResponse> request, IServiceProvider provider, CancellationToken cancellationToken)
    {
        var typedRequest = (TRequest)request;

        RequestHandlerDelegate<TResponse> handlerDelegate = () =>
            provider.GetRequiredService<IRequestHandler<TRequest, TResponse>>()
                .Handle(typedRequest, cancellationToken);

        // Fold behaviors so the first one registered ends up as the outermost wrapper: reverse the
        // registration order, then each fold wraps the previous "next" one layer further out.
        var pipeline = provider.GetServices<IPipelineBehavior<TRequest, TResponse>>()
            .Reverse()
            .Aggregate(handlerDelegate, (next, behavior) => () => behavior.Handle(typedRequest, next, cancellationToken));

        return pipeline();
    }
}
