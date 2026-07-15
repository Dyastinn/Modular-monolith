using Erp.Shared.BuildingBlocks;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Shared.Messaging;

// Phase 8 §6: domain-event dispatch contracts. They mention IDomainEvent + AggregateRoot from
// BuildingBlocks, which is why Messaging references BuildingBlocks.

// A reaction to a specific domain event. Zero-to-many handlers may exist per event.
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task Handle(TEvent domainEvent, CancellationToken ct);
}

// Generic TId so the dispatcher works for ANY aggregate, staying inside the module walls.
public interface IDomainEventDispatcher
{
    Task DispatchAsync<TId>(AggregateRoot<TId> agg, CancellationToken ct) where TId : struct;
}

public sealed class DomainEventDispatcher(IServiceProvider sp) : IDomainEventDispatcher
{
    public async Task DispatchAsync<TId>(AggregateRoot<TId> agg, CancellationToken ct) where TId : struct
    {
        foreach (var e in agg.DomainEvents)
        {
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(e.GetType());
            foreach (var handler in sp.GetServices(handlerType))
                await (Task)handlerType.GetMethod("Handle")!.Invoke(handler, new object[] { e, ct })!;
        }
        agg.ClearEvents();
    }
}
