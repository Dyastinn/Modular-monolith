namespace Erp.Shared.BuildingBlocks;

// Generic scaffolding every module's domain reuses (Phase 1 §4). Depends on nothing.
// NOTE: AggregateRoot repeats `where TId : struct` — the beginner text omits it, which fails to
// compile (CS0453). This is the corrected form.
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

// Part of the domain vocabulary every module reuses (Phase 11 §6).
public class DomainException(string message) : Exception(message);
