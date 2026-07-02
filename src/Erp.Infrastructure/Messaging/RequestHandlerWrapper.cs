using Erp.Application.Abstractions.Messaging;

namespace Erp.Infrastructure.Messaging;

internal abstract class RequestHandlerWrapper<TResponse> : RequestHandlerBase
{
    public abstract Task<TResponse> Handle(IRequest<TResponse> request, IServiceProvider provider, CancellationToken cancellationToken);
}
