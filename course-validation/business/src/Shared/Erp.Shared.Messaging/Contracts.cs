namespace Erp.Shared.Messaging;

// Marker: "I am a request that produces a TResponse."
public interface IRequest<out TResponse> { }

// Intent markers so the folder tree screams command-vs-query (they add no behavior):
public interface ICommand<out TResponse> : IRequest<TResponse> { }   // CHANGES state
public interface IQuery<out TResponse> : IRequest<TResponse> { }     // READS state

// A tiny "nothing meaningful to return" type, for commands with no useful result.
public readonly record struct Unit
{
    public static readonly Unit Value = default;
}

// The worker for one request type.
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken ct);
}

public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse> { }
public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse> { }

// "The rest of the pipeline" — used by behaviors.
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

// A wrapper step around every handler (logging, validation, ...).
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
