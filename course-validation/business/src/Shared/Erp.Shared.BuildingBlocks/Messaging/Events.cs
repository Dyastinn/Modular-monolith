namespace Erp.Shared.BuildingBlocks;

// Runs after the change that raised the event has committed. At-least-once: it may run twice, so it must be idempotent.
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken ct);
}

// A fact one module tells the others, declared in its .Contracts project. Plain data only (ids, amounts, text), so the
// publishing module can change its domain without breaking the modules that listen.
public interface IIntegrationEvent;

public interface IIntegrationEventHandler<in TEvent> where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken ct);
}

// Hands an integration event to every module that handles it. A module calls it from one of its domain event handlers,
// so it runs inside the outbox delivery: if any handler fails, the whole delivery is retried.
public interface IIntegrationEventPublisher
{
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken ct) where TEvent : IIntegrationEvent;
}
