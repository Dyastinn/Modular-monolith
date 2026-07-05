namespace Erp.Shared.BuildingBlocks;

// Business plan never shows these (defers to beginner Phase 3). Using the CORRECTED version
// (AggregateRoot repeats `where TId : struct`; the beginner text omits it -> CS0453).
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

public interface IDomainEvent { }

public sealed class DomainException(string message) : Exception(message);
