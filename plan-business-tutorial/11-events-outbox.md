# Lesson 11 — Events and the Outbox: Reacting, Reliably

Good day, developer! Until now, each feature did its job and stopped. But in a real business, one thing
leads to another: when an order is approved, it goes in the audit log; when it ships, an invoice must be
issued; when a customer pays, their balance must drop. Today we teach the system to *react*, and to do it reliably, even if the
server crashes at the worst possible moment. About two hours. Are you ready? Let's begin!

## What we'll build

- **Domain events**: the order records "I was approved", as a fact.
- A **transactional outbox**: that fact is saved in the same transaction as the approval itself.
- A **background dispatcher** that delivers each saved fact to its handlers, after the commit.
- Our first event handler: an audit log line for every approval.

## Why we build it this way

### The obvious ways, and why they hurt

We want "when an order is approved, write an audit entry". Here are the three obvious ways to do it:

| Approach | What goes wrong |
|---|---|
| Write the audit entry inside the approve handler | Every new reaction means editing the handler. Approving starts to know about auditing, invoicing, emailing... |
| Publish the event *before* saving | The reactions run, then the save fails. You've reacted to something that never happened |
| Publish the event *after* saving | The save commits, then the server crashes before publishing. The approval happened, and the reaction is lost forever |

That third one is the sneaky one, because it works every time you test it.

### The fix: an outbox

Save the event **in the same transaction** as the change, as a row in an `outbox_message` table. Now the
approval and "an approval happened" commit together, or not at all. Then, separately, a background worker
reads the outbox, delivers each event to its handlers, and marks it done.

To remember it easily: it's the office out-tray. You don't run each letter to the post office the moment you
write it. You drop it in the tray as part of finishing the job, and the mail run picks the tray up. If you go
home sick, the letters are still in the tray tomorrow.

```mermaid
sequenceDiagram
  participant H as Approve handler
  participant DB as PostgreSQL
  participant W as Outbox dispatcher
  participant A as Audit handler
  H->>DB: BEGIN; UPDATE order; UPDATE customer; INSERT outbox_message; COMMIT
  loop every few seconds
    W->>DB: take waiting messages
    W->>A: deliver SalesOrderApproved
    W->>DB: mark processed
  end
```

Now, let's be honest about the trade-off. The dispatcher delivers each event **at least once**. If the server
dies after the audit handler runs but before the message is marked done, the handler will see that event
again after the restart. So every handler must be **idempotent**: running it twice must be as good as running
it once. Writing a log line twice is harmless. Charging a card twice is not, so a handler like that must check
whether it already did the work.

## 1. Teach the aggregate to record events

The pieces live in BuildingBlocks. First, the marker for an event. It's a Mediator **notification**, which
is like a command, except it can have any number of handlers, including none:

```csharp
// add to src/Shared/Erp.Shared.BuildingBlocks/Domain/BuildingBlocks.cs  (and `using Mediator;` at the top)

// A fact the domain records ("order approved"). Saved to the outbox in the same transaction as the change, then
// delivered to INotificationHandler<T> classes by the outbox dispatcher after the commit.
public interface IDomainEvent : INotification { }

// What the outbox needs from any aggregate, whatever its id type: the events it recorded, and a way to forget them.
public interface IAggregateRoot
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }
    void ClearEvents();
}
```

Then the aggregate base class learns to keep a list. Before:

```csharp
public abstract class AggregateRoot<TId> : Entity<TId> where TId : struct;
```

After:

```csharp
public abstract class AggregateRoot<TId> : Entity<TId>, IAggregateRoot where TId : struct
{
    private readonly List<IDomainEvent> _events = new();
    public IReadOnlyList<IDomainEvent> DomainEvents => _events;
    protected void Raise(IDomainEvent e) => _events.Add(e);
    public void ClearEvents() => _events.Clear();
}
```

`Raise` is `protected`: only the aggregate itself can record a fact about itself. Why the extra
`IAggregateRoot` interface? `AggregateRoot<TId>` is generic, and the outbox needs to find every aggregate,
whatever its id type. A plain interface lets it ask for all of them at once.

In `SalesDbContext`, tell EF the event list isn't a column: inside the `SalesOrder` configuration, add
`e.Ignore(x => x.DomainEvents);`.

## 2. The event and its first handler

At the bottom of `Domain/SalesOrder.cs`, next to the order it describes:

```csharp
// Raise an event only in the change that adds its first handler.
internal sealed record SalesOrderApproved(SalesOrderId Id, CustomerId CustomerId, Money Total, UserId? ApprovedBy) : IDomainEvent;
```

In `SalesOrder.MarkApproved`, add one line at the end:

```csharp
Raise(new SalesOrderApproved(Id, CustomerId, Total, approver));
```

Because both auto-approval and manager approval go through `MarkApproved`, every approval raises it.

```csharp
// src/Modules/Sales/Erp.Modules.Sales/Infrastructure/EventHandlers/SalesOrderApprovedAuditHandler.cs
using Erp.Modules.Sales.Domain;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Erp.Modules.Sales.Infrastructure;

// Delivered by the outbox, so it can run more than once for the same approval. Logging twice is harmless.
internal sealed class SalesOrderApprovedAuditHandler(ILogger<SalesOrderApprovedAuditHandler> log)
    : INotificationHandler<SalesOrderApproved>
{
    public ValueTask Handle(SalesOrderApproved e, CancellationToken ct)
    {
        log.LogInformation("Sales order {OrderId} approved by {Approver} for ₱{Total:N2}",
            e.Id.Value, e.ApprovedBy?.Value.ToString() ?? "auto-approval", e.Total.Amount);
        return ValueTask.CompletedTask;
    }
}
```

The Mediator generator finds the handler on its own; there's nothing to register.

Notice the comment on the event: *raise an event only in the change that adds its first handler.* If you
raise events nobody listens to, the generator warns you (`MSG0005: message without any registered handler`).
Keep that warning on. It's how you notice an event someone forgot to handle.

## 3. The outbox message

```csharp
// src/Shared/Erp.Shared.BuildingBlocks/Messaging/OutboxMessage.cs
using System.Text.Json;

namespace Erp.Shared.BuildingBlocks;

// A domain event waiting in a module's outbox table, saved in the same transaction as the change that raised it.
public sealed class OutboxMessage
{
    public Guid Id { get; private set; }
    public string Type { get; private set; } = default!;
    public string Payload { get; private set; } = default!;
    public DateTime OccurredUtc { get; private set; }
    public DateTime? ProcessedUtc { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }

    private OutboxMessage() { }

    public static OutboxMessage From(IDomainEvent domainEvent, DateTime nowUtc)
    {
        var type = domainEvent.GetType();
        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            // No assembly version, so messages written before an upgrade still load after it.
            Type = $"{type.FullName}, {type.Assembly.GetName().Name}",
            Payload = JsonSerializer.Serialize(domainEvent, type),
            OccurredUtc = nowUtc
        };
    }

    public static IDomainEvent Deserialize(string type, string payload)
    {
        var eventType = System.Type.GetType(type, throwOnError: true)!;
        return (IDomainEvent)JsonSerializer.Deserialize(payload, eventType)!;
    }
}
```

An event is stored as two strings: its **type name**, so we know what to rebuild, and its data as **JSON**.
`ProcessedUtc` stays empty until it's delivered; `Attempts` and `LastError` record failures.

## 4. A test that catches a real bug

Before going further, let's prove an event survives the trip into JSON and back. Add this to the unit tests:

```csharp
public class OutboxSerializationTests
{
    [Fact]
    public void An_event_survives_the_round_trip_through_the_outbox()
    {
        var original = new SalesOrderApproved(new SalesOrderId(42), new CustomerId(7), new Money(60_000.50m), new UserId(2));

        var message = OutboxMessage.From(original, Make.Now);
        var restored = OutboxMessage.Deserialize(message.Type, message.Payload);

        restored.Should().Be(original);
    }
}
```

Run `dotnet test`. It **fails**:

```
Expected restored to be ... Total = Money { Amount = 60000.50M } ...
but found ...                  Total = Money { Amount = 0M } ...
```

The money came back as ₱0! Here's why. `Money` is a struct, and every struct also has a hidden, empty
constructor. When the JSON serializer rebuilds a `Money`, it uses that empty one, then tries to set `Amount`,
and `Amount` has no setter. So it stays 0, silently. In production, every approval event would have reported
a ₱0 total.

The fix is to tell the serializer which constructor to use. In `Money.cs`, add `using
System.Text.Json.Serialization;` and mark the real constructor:

```csharp
[JsonConstructor]   // without it, JSON (the outbox) builds the struct empty and the amount comes back as 0
public Money(decimal amount) =>
    Amount = amount >= 0
        ? Math.Round(amount, 2, MidpointRounding.AwayFromZero)
        : throw new DomainException("An amount of money cannot be negative.");
```

Do the same in `Quantity.cs` (`[JsonConstructor]   // see Money`). Run the tests again: green. And as a
bonus, the validating constructor now runs on the way back in too, so a corrupted payload can't create
negative money.

While you're in the tests, add one line to `Small_order_is_approved_automatically_and_charged`, to prove the
event is raised:

```csharp
order.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<SalesOrderApproved>();
```

## 5. Write events into the outbox, in the same save

Shared Infrastructure needs EF Core now, to hook into saving:

```bash
dotnet add src/Shared/Erp.Shared.Infrastructure package Microsoft.EntityFrameworkCore --version 10.0.14
```

```csharp
// src/Shared/Erp.Shared.Infrastructure/Outbox/OutboxInterceptor.cs
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Erp.Shared.Infrastructure;

// Turns the aggregates' domain events into outbox rows inside the same SaveChanges, so they commit together.
public sealed class OutboxInterceptor(IClock clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        MoveEventsToOutbox(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        MoveEventsToOutbox(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    private void MoveEventsToOutbox(DbContext? db)
    {
        if (db is null) return;

        var aggregates = db.ChangeTracker.Entries<IAggregateRoot>()
            .Select(e => e.Entity)
            .Where(a => a.DomainEvents.Count > 0)
            .ToList();

        var now = clock.UtcNow;
        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
                db.Set<OutboxMessage>().Add(OutboxMessage.From(domainEvent, now));
            aggregate.ClearEvents();
        }
    }
}
```

An **interceptor** is a hook EF Core calls at certain moments. `SavingChanges` runs just before EF writes
anything. We find every tracked aggregate with events, turn each event into an `OutboxMessage`, and add it to
the same save. So when the order's `UPDATE` commits, its outbox row commits with it. Not one handler has to
remember to publish anything; recording the event in the domain is enough.

Register it once, in `AddSharedInfrastructure`:

```csharp
services.AddSingleton<IInterceptor, OutboxInterceptor>();
```

Then each module's DbContext picks it up. In `SalesModule`, `IdentityModule` and `InventoryModule`, add
`.AddInterceptors(sp.GetServices<IInterceptor>())` to the end of the `Configure(...)` call, and add `using
Microsoft.EntityFrameworkCore.Diagnostics;`. For example:

```csharp
services.AddDbContext<SalesDbContext>((sp, o) =>
    SalesDbContext.Configure(o, sp.GetRequiredService<ISharedConnection>().Connection)
        .AddInterceptors(sp.GetServices<IInterceptor>()));
```

Each module keeps its own outbox, in its own schema. In each module's `OnModelCreating` (Sales, Identity,
Inventory), right after `HasDefaultSchema`:

```csharp
b.Entity<OutboxMessage>(e =>
{
    e.ToTable("outbox_message");
    e.HasKey(x => x.Id);
    e.Property(x => x.Type).HasMaxLength(500);
    e.Property(x => x.Payload).HasColumnType("jsonb");
    e.HasIndex(x => x.OccurredUtc).HasFilter("processed_utc IS NULL");
});
```

`jsonb` is PostgreSQL's binary JSON type: you can query inside it later if you need to. The **filtered
index** only covers messages not yet processed, which is exactly what the dispatcher asks for, so finding
waiting messages stays fast even when the table holds millions of delivered ones.

## 6. The dispatcher

The dispatcher needs to know which schemas have an outbox. Every module's database knows its own schema, so
let's add it to the interface. In `IModuleDatabase`, add `string Schema { get; }`, and in each module's
database class, return the context's constant: `public string Schema => SalesDbContext.Schema;` (and the
same for Identity and Inventory).

```csharp
// src/Shared/Erp.Shared.Infrastructure/Outbox/OutboxDispatcher.cs
using System.Text.RegularExpressions;
using Dapper;
using Erp.Shared.BuildingBlocks;
using Mediator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Erp.Shared.Infrastructure;

public sealed class OutboxOptions
{
    public int PollIntervalMs { get; set; } = 2000;
    public int BatchSize { get; set; } = 50;
    public int MaxAttempts { get; set; } = 10;
}

// Delivers outbox events after commit. At-least-once: a handler may see an event twice, so handlers must be
// idempotent. FOR UPDATE SKIP LOCKED keeps several app instances from taking the same message. After
// MaxAttempts failures a message is left, with its last_error, for a person to look at.
public sealed partial class OutboxDispatcher(
    IServiceScopeFactory scopes,
    NpgsqlDataSource dataSource,
    IConfiguration config,
    IClock clock,
    ILogger<OutboxDispatcher> log) : BackgroundService
{
    private readonly OutboxOptions _options = config.GetSection("Outbox").Get<OutboxOptions>() ?? new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string[] schemas;
        await using (var scope = scopes.CreateAsyncScope())
            schemas = scope.ServiceProvider.GetServices<IModuleDatabase>().Select(m => m.Schema).ToArray();

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.PollIntervalMs));
        do
        {
            foreach (var schema in schemas)
            {
                try
                {
                    // A full batch means more may be waiting.
                    while (await DispatchBatchAsync(schema, stoppingToken) == _options.BatchSize) { }
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    log.LogError(ex, "Outbox dispatch for schema {Schema} failed; will retry next tick", schema);
                }
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private sealed record Row(Guid Id, string Type, string Payload);

    public async Task<int> DispatchBatchAsync(string schema, CancellationToken ct)
    {
        if (!SafeIdentifier().IsMatch(schema)) throw new ArgumentException($"Bad schema name '{schema}'.");
        var table = $"{schema}.outbox_message";

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var batch = (await conn.QueryAsync<Row>(
            $"""
            SELECT id, type, payload FROM {table}
            WHERE processed_utc IS NULL AND attempts < @MaxAttempts
            ORDER BY occurred_utc
            LIMIT @BatchSize
            FOR UPDATE SKIP LOCKED
            """,
            new { _options.MaxAttempts, _options.BatchSize }, tx)).ToList();

        foreach (var message in batch)
        {
            try
            {
                var domainEvent = OutboxMessage.Deserialize(message.Type, message.Payload);
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IPublisher>().Publish(domainEvent, ct);

                await conn.ExecuteAsync($"UPDATE {table} SET processed_utc = @Now WHERE id = @Id",
                    new { Now = clock.UtcNow, message.Id }, tx);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                await conn.ExecuteAsync($"UPDATE {table} SET attempts = attempts + 1, last_error = @Error WHERE id = @Id",
                    new { Error = ex.ToString(), message.Id }, tx);
                log.LogError(ex, "Outbox message {MessageId} ({Type}) failed", message.Id, message.Type);
            }
        }

        await tx.CommitAsync(ct);
        return batch.Count;
    }

    [GeneratedRegex("^[a-z_][a-z0-9_]*$")]
    private static partial Regex SafeIdentifier();
}
```

This is the longest file in the course, so let's walk it carefully:

| Piece | What it does |
|---|---|
| `BackgroundService` | ASP.NET Core starts it with the app and stops it with the app; `ExecuteAsync` runs for the app's whole life |
| `PeriodicTimer` | Wakes up every 2 seconds (configurable) and checks each module's outbox |
| `FOR UPDATE SKIP LOCKED` | Takes a batch of waiting messages and locks them. If you run two copies of the app, the second skips the locked rows instead of delivering them twice |
| `IPublisher.Publish` | Mediator's way to deliver a notification to all its handlers. Each event gets a fresh scope, like a fresh request |
| On success | `processed_utc` is set: delivered |
| On failure | `attempts` goes up and the error is kept. After 10 failures it stops retrying, and the message waits for a person |
| `SafeIdentifier` | The schema name is pasted into the SQL (table names can't be parameters), so we only allow plain lowercase names |

Register it in `AddSharedInfrastructure`:

```csharp
services.AddHostedService<OutboxDispatcher>();
```

And add its settings to `appsettings.json`:

```json
"Outbox": { "PollIntervalMs": 2000, "BatchSize": 50, "MaxAttempts": 10 }
```

Finally, give each module its outbox table:

```bash
for m in Identity Sales Inventory; do
  dotnet ef migrations add AddOutbox --project src/Modules/$m/Erp.Modules.$m \
    --startup-project src/Modules/$m/Erp.Modules.$m --output-dir Infrastructure/Migrations
done
```

## Mistakes to avoid

- Publishing events yourself, before or after `SaveChangesAsync`. Let the outbox do it, inside the save.
- Writing a handler that isn't safe to run twice.
- Raising events nobody handles "for later". The generator's warning is there to catch exactly that.
- Putting a value object in an event without `[JsonConstructor]`. It may come back empty, silently.
- Letting one module read another module's outbox. Each schema has its own.

## Let's run it!

Run `dotnet test` first: the round-trip test passes now. Then restart the app and approve something. The
quickest is a small order, which is approved on submit. Click through it in the app, or (with a `$TOKEN`
from lesson 06):

```bash
ID=$(curl -s -X POST http://localhost:5264/api/sales-orders -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"customerId":1,"lines":[{"itemId":1,"quantity":1}]}' | jq .id)
curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:5264/api/sales-orders/$ID/submit -H "Authorization: Bearer $TOKEN"
```

Within two seconds, the API's console shows:

```
info: Erp.Modules.Sales.Infrastructure.SalesOrderApprovedAuditHandler[0]
      Sales order 15 approved by auto-approval for ₱25.00
```

And the outbox row shows it was delivered:

```bash
PGPASSWORD=erp_dev_only "/c/Program Files/PostgreSQL/18/bin/psql.exe" -h localhost -U postgres -d erp \
  -c "SELECT type, payload, processed_utc, attempts FROM sales.outbox_message ORDER BY occurred_utc DESC LIMIT 1;"
```

## Let's check!

1. Why is "publish the event right after saving" not good enough?
2. What exactly does the outbox write, and in which transaction?
3. What does "at least once" mean, and what does it demand from every handler?
4. What does `FOR UPDATE SKIP LOCKED` protect against?
5. Why did `Money` come back as ₱0, and what fixed it?
6. Why does each module have its own outbox table?

And that's where this lesson ends. The system now reacts to what happens, and a crash can't make it forget.
Next time, we prove all of it with tests: the rules, the races, the module walls, and the screens. Good
luck, developer!

---

← [Lesson 10 — Update an order](10-update-order.md) · Next: [Lesson 12 — Testing](12-testing.md) →
