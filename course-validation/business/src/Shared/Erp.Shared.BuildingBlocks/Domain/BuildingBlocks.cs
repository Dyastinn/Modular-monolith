using Mediator;

namespace Erp.Shared.BuildingBlocks;

// Base types every module's domain reuses. `where TId : struct` must be repeated on derived classes (CS0453).
public abstract class Entity<TId> where TId : struct
{
    public TId Id { get; protected set; }
}

public abstract class AggregateRoot<TId> : Entity<TId> where TId : struct
{
    private readonly List<IDomainEvent> _events = new();
    public IReadOnlyList<IDomainEvent> DomainEvents => _events;
    protected void Raise(IDomainEvent e) => _events.Add(e);
    public void ClearEvents() => _events.Clear();
}

// A fact the domain records ("order approved"). It's a Mediator notification, so in-module reactions are
// ordinary INotificationHandler<T> classes.
public interface IDomainEvent : INotification { }

// A business rule said no. Maps to HTTP 422.
public class DomainException(string message) : Exception(message);
