using Erp.Shared.BuildingBlocks;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Shared.Messaging;

// Phase 8 §6: domain-event dispatch contracts (reference IDomainEvent + AggregateRoot from BuildingBlocks,
// hence Messaging -> BuildingBlocks reference).
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task Handle(TEvent domainEvent, CancellationToken ct);
}

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
