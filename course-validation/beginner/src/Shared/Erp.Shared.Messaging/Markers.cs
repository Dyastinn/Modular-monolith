namespace Erp.Shared.Messaging;

// Request markers — is this request a change or a read?
public interface ICommand<TResponse> : IRequest<TResponse> { }  // "I change something"
public interface IQuery<TResponse> : IRequest<TResponse> { }    // "I only read"

// Matching handler markers — read better than IRequestHandler and let us target behaviors
// at just commands (e.g. "wrap every ICommandHandler in a transaction") later if we want.
public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse> { }
public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse> { }
