namespace Erp.Shared.BuildingBlocks;

public abstract class Entity<TId> where TId : struct   // TId is the strongly-typed ID
{
    public TId Id { get; protected set; }
}

public abstract class AggregateRoot<TId> : Entity<TId> where TId : struct
{
    private readonly List<IDomainEvent> _events = new();
    public IReadOnlyList<IDomainEvent> DomainEvents => _events;
    protected void Raise(IDomainEvent e) => _events.Add(e);   // record "something happened"
    public void ClearEvents() => _events.Clear();
}

public interface IDomainEvent { }   // a marker; the actual events are records

public sealed class DomainException(string message) : Exception(message);
