using System.Collections.Concurrent;
using Erp.Application.Abstractions.Messaging;

namespace Erp.Infrastructure.Messaging;

// Approach A from the plan: resolve a small non-generic wrapper per concrete request type via
// reflection once, then cache it. This is the same technique MediatR uses; dispatch after the
// first call for a given request type is a dictionary lookup, not reflection.
//
// A source-generated mediator (e.g. martinothamar/Mediator) moves this resolution to compile time
// and avoids reflection entirely. That's a fine drop-in replacement if dispatch ever shows up in a
// profile — not needed for this project's scale, so we don't pre-optimize for it.
public sealed class Sender(IServiceProvider provider) : ISender
{
    private static readonly ConcurrentDictionary<Type, RequestHandlerBase> Wrappers = new();

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        var wrapper = (RequestHandlerWrapper<TResponse>)Wrappers.GetOrAdd(
            request.GetType(),
            static requestType => (RequestHandlerBase)Activator.CreateInstance(
                typeof(RequestHandlerWrapperImpl<,>).MakeGenericType(requestType, typeof(TResponse)))!);

        return wrapper.Handle(request, provider, cancellationToken);
    }
}
