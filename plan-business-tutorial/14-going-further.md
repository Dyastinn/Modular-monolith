# Phase 14 — Going Further: Production Hardening, Docker & Redis (Optional)

> 🚫 **This phase is outside the course.** The base build stopped, on purpose, at a *correct* module — not a
> *deployed* one. Everything below is for when **you** take this design to production in your own project.
> None of it is part of the tutorial's deliverable, and you can close the tab at Phase 13 with a complete,
> working module. Read on only if you want the map for the last mile.

---

> ⏱️ **No fixed time — this is a menu, not a sitting.** Phases 1–12 were "type this through, start to finish."
> This one isn't. Take the items you have a genuine need for and leave the rest until you do.

Two rules carry over from the whole tutorial, and they matter *more* here, not less:

1. **Nothing here changes the base architecture.** Modules stay islands, the domain stays pure, and every
   rule stays where [Phase 13's table](13-wrap-up-checklist.md) put it. Production hardening wraps *around*
   the build — it never rewrites it.
2. **Adopt each thing only when you feel the pain it solves.** Same discipline the mediator was introduced
   with: show the pain first, reach for the tool second. "We might need it later" is not a reason.

## 1. Finish the production-readiness checklist (no new technology)

[Phase 13 §3](13-wrap-up-checklist.md) listed the gaps. Start with the ones that need nothing but .NET 10 and
the stack you already have — they're the difference between "passes tests" and "safe to deploy," and none of
them is exotic.

### Secrets out of config, validated at startup

Move `Jwt:SigningKey` and the connection string to user-secrets (dev) and environment variables or a vault
(prod). Then make a missing or too-short key fail the *deploy*, not the first login an hour later:

```csharp
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection("Jwt"))
    .Validate(o => o.SigningKey?.Length >= 32, "Jwt:SigningKey must be at least 32 characters")
    .ValidateOnStart();   // app refuses to boot with a bad key, instead of throwing on request #1
```

### HTTPS, always

```csharp
app.UseHsts();                 // browsers: "this host is HTTPS-only" (leave off in Development)
app.UseHttpsRedirection();     // bounce any plain-HTTP request to HTTPS
```

A JWT sent over plain HTTP is a festival wristband anyone on the wire can photocopy — the signature stops
forgery, not copying. TLS is what stops the copy.

### Rate-limit the login

Brute-forcing `/auth/login` is the first thing an attacker tries. ASP.NET Core has a built-in limiter — no
dependency:

```csharp
builder.Services.AddRateLimiter(o =>
{
    o.AddFixedWindowLimiter("login", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 5;     // 5 attempts per window
        opt.QueueLimit = 0;      // over the limit → reject, don't queue
    });
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});
app.UseRateLimiter();
// on the login endpoint: .RequireRateLimiting("login")
```

> ⚠️ **Two footguns.** A single unpartitioned limiter throttles *everyone together* — one busy client can use
> up the whole window for all users. Partition by client IP or username so the limit is per-caller
> (`PartitionedRateLimiter.Create(...)`). And the counters live **in memory**: run two instances behind a load
> balancer and "5 per minute" quietly becomes "10." Fixing that needs a shared store — which is one of the two
> honest reasons Redis shows up (§3).

### A health check that actually checks the database

The `/health` from [Phase 1](01-solution-setup.md) only proves the *process* is alive. Prove the *database*
is reachable, because that's what "can this instance serve requests?" really means:

```csharp
public sealed class DatabaseHealthCheck(IDbConnectionFactory factory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext ctx, CancellationToken ct = default)
    {
        try
        {
            using var conn = factory.Create();
            await ((SqlConnection)conn).OpenAsync(ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) { return HealthCheckResult.Unhealthy("Database unreachable", ex); }
    }
}
// builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("db");
// app.MapHealthChecks("/health");
```

Load balancers and orchestrators poll this to decide whether to route traffic to an instance. "The app
started" and "the app can serve a request" are different claims — a health check should assert the second.

### Structured logging to a real sink

`ILogger` is already wired through the `LoggingBehavior` and the audit handlers — you don't swap frameworks,
you point a provider at wherever you aggregate logs. Here's the quiet payoff you already banked: because you
wrote `log.LogInformation("← {Request} in {Ms}ms", name, ms)` with **placeholders** instead of a
string-interpolated `$"← {name} in {ms}ms"`, `Request` and `Ms` survive as *queryable fields*, not flattened
text. That was structured logging the whole time; a sink just makes it searchable.

### Refresh tokens — already in the base course

> ✅ **Now built in [Phase 4 §10](04c-jwt-refresh-apikey.md).** Short access tokens backed by rotating refresh tokens, with
> reuse detection and revoke-all-sessions. Two hardening extras were left as flagged follow-ups: delivering the
> refresh token as an `HttpOnly` cookie for browser clients (versus the response body), and a periodic cleanup
> of revoked/expired rows (exercise #7 below).

### Row-level authorization

The rule [Phase 10](10-queries-search.md) flagged: a SalesRep should see and act on only their own customers'
orders, a Manager on all. It's *data-dependent*, so it lives in the handler/query — pass `ICurrentUser` in and
add a `CreatedByUserId = @CallerId` filter for non-managers. Same shape as extension exercise #5. This is the
first thing a real multi-user deployment needs.

## 2. Docker — environment parity, not architecture

> 🚫 **Still out of scope, and it was right to skip.** Docker doesn't make the *module* better; it makes
> *running it* reproducible. You add it when "works on my machine" starts costing real time: onboarding a
> teammate, matching CI to prod, or just wanting a throwaway SQL Server you didn't have to install.

The base build assumes LocalDB and `dotnet run`. Fine for one developer on Windows — until a colleague on a
Mac clones it, discovers LocalDB doesn't exist there, and loses a morning to "install SQL Server." A container
image is the same environment on every machine. Two separate decisions:

**The database, for local dev — the quick win, and it touches no code:**

```yaml
# docker-compose.yml — a dev convenience, NOT part of the app
services:
  sql:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_SA_PASSWORD: "Your_strong_Passw0rd"
    ports: [ "1433:1433" ]
```

`docker compose up -d`, point the connection string at `Server=localhost,1433;...`, run your
[Phase 3](03-database-schema.md) scripts against it. Anyone gets an identical SQL Server in one command.

**The API, for deployment — a multi-stage build (compile on the SDK image, ship on the smaller runtime image):**

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Erp.Api -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Erp.Api.dll"]
```

The connection string and signing key arrive as **environment variables** — `ConnectionStrings__ErpDb`,
`Jwt__SigningKey` (the double underscore is how .NET config reads a nested key from the environment). Which is
exactly *why* §1 moved secrets out of `appsettings.json`: a shared image must never bake them in.

> ⚠️ **Honest cost.** Docker is another tool to learn and another layer to debug, and a container is not a
> security boundary on its own. It buys reproducibility, not correctness. Solo dev on one machine where
> everything already runs? You don't need it yet.

> 📎 **The first place Docker genuinely earns its keep here: tests.** **Testcontainers** (`Testcontainers.MsSql`)
> spins up a real SQL Server container *per integration-test run* and disposes it after — replacing the
> hand-managed `Erp_Test` LocalDB fixture from [Phase 12](12-testing.md) with a disposable, identical database
> on any laptop or CI runner. Your concurrency test now proves the same thing everywhere.

## 3. Redis — a shared store for when one process isn't enough

> 🚫 **The most-overused tool on this list.** Reach for it last, and only with a measurement in hand. "Add a
> cache" is the reflex that turns a simple, correct system into a fast, *wrong* one — stale reads, invalidation
> bugs, and a brand-new thing that can be down. Two real reasons it appears, and they're different problems:

**1. A read is measurably hot.** You *profiled* — not guessed — and one query dominates, over data that's read
constantly and changed rarely (the item catalog, a customer's credit terms). `IDistributedCache` is the .NET
abstraction; Redis is one implementation behind it:

```csharp
builder.Services.AddStackExchangeRedisCache(o =>
    o.Configuration = builder.Configuration.GetConnectionString("Redis"));
```

```csharp
// in a query handler — cache-aside: check the cache, fall back to SQL, backfill.
var cached = await cache.GetStringAsync(key, ct);
if (cached is not null) return JsonSerializer.Deserialize<ItemDto>(cached)!;

var dto = await LoadFromSql(...);
await cache.SetStringAsync(key, JsonSerializer.Serialize(dto),
    new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) }, ct);
return dto;
```

> 🔑 **The hard part is invalidation, not reading.** The moment an item's price changes, the cached copy is a
> lie. You either evict the key on every write that touches it (`cache.RemoveAsync(key)` in the command
> handler) or accept a staleness window and confirm that's tolerable *for that data*. A price, slightly stale,
> is usually fine. The inventory `Reserved`/`OnHand` numbers are **never** cacheable — those are the exact
> values that must be read live under concurrency ([Phase 7](07-submit-reserve-concurrency.md)). Cache them and
> you reintroduce the oversell bug you worked to kill.

**2. You scaled past one instance and need shared state.** The §1 rate limiter keeps its counters in memory;
two instances behind a load balancer means two separate counters. A shared store gives them one. Same story
for anything that assumes a single process — distributed rate limiting, a shared data-protection key ring,
output caching across instances.

> ⚠️ **Honest cost.** Redis is one more service to run, secure, monitor, and keep up — and now your app has a
> new failure mode. Design so a cache miss *or* a full Redis outage degrades to a **slower correct answer**
> (go to SQL), never a wrong one and never an outage. If removing the cache breaks correctness, it was never a
> cache — it was a database you forgot to treat like one.

## 4. More extension exercises

[Phase 13 §5](13-wrap-up-checklist.md) had six. Here are more, ordered roughly by how far they push past the
base build. Same rule: try it *before* reading how.

7. **Refresh-token hardening** — the two follow-ups [Phase 4 §10](04c-jwt-refresh-apikey.md) flagged: deliver the refresh
   token to a browser client as an `HttpOnly; Secure; SameSite` cookie instead of the response body, and add a
   scheduled cleanup (`DELETE … WHERE RevokedUtc < …`) so revoked/expired rows don't accumulate. (Reinforces:
   the XSS threat model, and that housekeeping is separate from correctness.)
8. **Per-IP login rate limit** — partition the §1 limiter by client IP so one attacker can't lock everyone
   out. Then notice it only holds per-instance, and reason through what §3 would take to make it global.
   (Reinforces: why in-process state and horizontal scale conflict.)
9. **Dockerized integration tests** — swap the [Phase 12](12-testing.md) LocalDB fixture for
   `Testcontainers.MsSql`. The reservation-race test now runs identically on any CI runner. (Reinforces:
   reproducible environments; the fixture seam you already built.)
10. **Cache the item catalog** — put `IDistributedCache` in front of the item/category read and evict on the
    (rare) item write. Prove the invalidation by changing a price and re-reading. (Reinforces: cache-aside, and
    that invalidation — not caching — is the hard part.)
11. **Reliable cross-system reaction (the outbox)** — the one promise [Phase 8](08-approval-cancellation.md)'s
    after-commit dispatch can't make: if the process dies between commit and the side effect, the event is
    lost. Write each domain event to an `Outbox` table *in the same transaction* as the state change, and drain
    it with a background reader. (Reinforces: "after commit, best-effort" and "guaranteed delivery" are
    different guarantees.)
12. **A second business module** — build `Purchasing` (buy stock from suppliers, raising `OnHand`) as its own
    module with its own schema, reusing the shared kernel untouched. If you can add it without editing Sales,
    the walls are real. (Reinforces: the whole point — the third module should be as easy as the second.)

## 5. Where to stop

You can ship the base build. Adding §1 makes it *deployable*; §2 and §3 make it *operable at scale* — and you
only need scale when you actually have scale. The failure mode of senior engineers isn't shipping too little
infrastructure; it's shipping infrastructure for problems they don't have yet. Every item here has a "learn it
when…" trigger. Wait for the trigger.

The module you already built is the part that's hard to get right. The rest is plumbing — attach it when the
plumbing is the problem, and not before.

---

## ✅ Check Yourself

1. Which production gaps can you close with *no* new technology — and why is that most of the list?
2. What does Docker actually buy you, and what does it explicitly *not*?
3. Name a value in this system you must **never** cache, and say why.
4. Why does an in-memory rate limiter break when you add a second instance, and what fixes it?
5. What can an outbox guarantee that Phase 8's after-commit dispatch cannot?

← Back to [Phase 13 — Wrap-up](13-wrap-up-checklist.md) · or [the tutorial overview](00-START-HERE.md).
