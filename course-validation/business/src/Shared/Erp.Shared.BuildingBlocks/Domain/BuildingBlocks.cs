namespace Erp.Shared.BuildingBlocks;

// Base types every module's domain reuses. `where TId : struct` must be repeated on derived classes (CS0453).
public abstract class Entity<TId> where TId : struct
{
    public TId Id { get; protected set; }
}

// What the outbox needs from any aggregate, whatever its id type: the events it recorded, and a way to forget them.
public interface IAggregateRoot
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }
    void ClearEvents();
}

public abstract class AggregateRoot<TId> : Entity<TId>, IAggregateRoot where TId : struct
{
    private readonly List<IDomainEvent> _events = new();
    public IReadOnlyList<IDomainEvent> DomainEvents => _events;
    protected void Raise(IDomainEvent e) => _events.Add(e);
    public void ClearEvents() => _events.Clear();
}

// A fact the domain records ("order approved"), in its own module's words. Saved to the outbox in the same transaction
// as the change, then delivered to that module's IDomainEventHandler<T> classes after the commit. Never public: other
// modules hear about it only through an integration event (IIntegrationEvent) the module chooses to publish.
public interface IDomainEvent;

// A business rule said no. Maps to HTTP 422, with the error's code. The message adds the specifics ("₱5,000 available").
public class DomainException(ErrorCode error, string? message = null) : Exception(message ?? error.Title)
{
    public ErrorCode Error { get; } = error;
}
