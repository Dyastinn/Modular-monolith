namespace Erp.Shared.Messaging;

// "I am a request that, when handled, produces a TResponse."
public interface IRequest<out TResponse> { }

// "I am the handler for a specific request type."
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken ct);
}

// A "next step in the chain" — used by behaviors below.
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

// A wrapper step that runs around every handler (logging, validation...).
public interface IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct);
}

// The one thing callers use.
public interface ISender
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default);
}
