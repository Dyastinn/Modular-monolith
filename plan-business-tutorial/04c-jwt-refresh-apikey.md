# Phase 4 (Part 3 of 5) — Refresh Tokens & the API-Key Gate

> **Phase 4 is split into five parts** — do them in order:
> 1. [Passwords & access tokens](04-jwt-auth.md)
> 2. [Login, validation & authorization](04b-jwt-login.md)
> 3. **Refresh tokens & the API-key gate** ← you are here
> 4. [Changing & resetting a password](04d-jwt-change-password.md)
> 5. [Request flow, end-to-end checkpoint & common mistakes](04e-jwt-wrap-up.md)

> Builds on [Part 2](04b-jwt-login.md): you can already log in, validate tokens, read the caller through `ICurrentUser`, and authorize by role. Two problems remain — a 15-minute access token means re-typing your password constantly (§10 fixes it with refresh tokens), and the anonymous login door faces the raw internet (§11 adds a client gate).

---

## 10. Step 8 — Refresh tokens: staying logged in without staying exposed

A 15-minute access token is safe — steal it and it's dead by the time you use it — but on its own it means
re-typing your password every 15 minutes. The fix is a second, longer-lived credential: the **refresh token**.
The client keeps it, and when the access token expires, trades it at `POST /auth/refresh` for a new one. No
re-login.

At this point you're juggling **two** tokens, and beginners mix them up constantly. Here's the whole
distinction on one screen — the *why* for every row is spelled out in the rest of this section:

| | **Access token** (the JWT) | **Refresh token** |
|---|---|---|
| **Its job** | Prove who you are on each request | Get a fresh access token when the old one expires |
| **Lifetime** | Short — 15 min | Long — days |
| **Sent with** | *Every* API request (`Authorization: Bearer …`) | *Only* to `POST /auth/refresh` |
| **Shape** | A signed JWT — claims baked in, readable | An opaque random string — no claims, means nothing on its own |
| **Server keeps a copy?** | No (stateless — it just checks the signature) | Yes — but only its **hash**, so it can be revoked |
| **If it's stolen** | Expires in ≤15 min, limited damage | The real prize — defended by rotation + reuse detection (below) |

> 💡 **The short version:** the **access token** is the wristband you flash at every stage. The **refresh
> token** is the ticket stub you swap at the gate for tomorrow's wristband. You use the access token
> constantly; you use the refresh token rarely, and guard it far more carefully.

But now the *refresh* token is the juicy target: it's long-lived, and whoever holds it can mint access tokens.
Two defenses, and you need both:

- **Rotation.** Every refresh *consumes* the token and issues a new one. A refresh token is single-use.
- **Reuse detection.** Because each token is single-use, seeing an *already-spent* one presented again means
  two parties hold copies — the real user and a thief. That's the signal. Revoke the entire **family** (every
  token descended from that login) and force a fresh login. Whoever refreshed first, the other is now locked
  out — and if it was the thief, the user's next refresh trips the same wire.

> 💡 The wristband analogy still fits: the access token is the wristband (glance and go). The refresh token is
> the **ticket stub** you swap at the gate for tomorrow's wristband — and the gate tears the stub as it hands
> you the new one. Present a stub that's already torn and security knows it was copied.

### A family's whole life, in one trace

The word **family** does a lot of work above, so pin it down before the code piles on: a *family* is **every
refresh token descended from a single login**, tied together by one shared `FamilyId` (a `Guid` minted at
login). Rotation keeps you *in* the same family — `A1` becomes `A2` becomes `A3`, all still `F1`. The **only**
thing that ever starts a *new* family is a fresh login. So "revoke the family" means "end that one login
session, every token it ever spawned," in a single call.

Here's one family — Alice's `F1` — living a normal day, getting copied by a thief, and dying the moment the
two copies diverge. Read the right column as the state of the `RefreshToken` rows *after* each event:

| When | Event | Family `F1` after |
|---|---|---|
| Mon 09:00 | Alice logs in — a new family `F1` opens | `A1` live |
| Mon 09:16 | Alice refreshes `A1` (mint `A2`, revoke `A1`) | `A1` spent → `A2` live |
| Mon 09:20 | A thief copies Alice's *current* token `A2` | unchanged — **a copy is invisible to the server** |
| Tue 08:05 | Alice refreshes `A2` first (mint `A3`, revoke `A2`) | `A2` spent → `A3` live |
| Tue 10:30 | Thief replays `A2` — but it's **already spent** | reuse detected → **every row in `F1` revoked** |
| Tue 10:31 | Alice's own `A3` refresh now fails too | family is dead; both must log in again |

The thief is locked out; Alice simply logs in again with her **password** — which the thief never had. One
poisoned session ends cleanly, and nothing else the user owns is touched.

> 🔑 **You never catch the theft in the act — and you don't need to.** Nothing fires at 09:20 when the token is
> copied; the server literally cannot see a copy being made. What springs the trap is **rotation forcing the
> two copies to diverge**: whoever refreshes first (Alice, here) spends `A2` and leaves the other party holding
> a stub that's now revoked. The next time that spent stub is presented — from *either* hand — the reuse check
> at the top of the handler fires. Detection is *inevitable*, not clever: single-use tokens guarantee that two
> copies can't both stay valid.

### The refresh-token service (generate, hash, expiry policy)

`Erp.Shared.Infrastructure/Security/RefreshTokenService.cs` — the random token, its one-way hash, and the
sliding-window policy, in one place. Notice there's **no** absolute-expiry method: the hard cap isn't a span
this service computes, it's the user's `PasswordExpiresUtc`, which the handler reads from the database and
passes in:

```csharp
// src/Shared/Erp.Shared.Infrastructure/Security/RefreshTokenService.cs
using System.Security.Cryptography;   // RandomNumberGenerator, SHA256
using System.Text;                    // Encoding
using Microsoft.Extensions.Configuration;

namespace Erp.Shared.Infrastructure;

public interface IRefreshTokenService
{
    (string RawToken, string TokenHash) Generate();          // a new random token + its hash
    string Hash(string rawToken);                            // hash an incoming token, to look it up
    DateTime SlidingExpiry(DateTime from, DateTime cap);     // from + Jwt:RefreshSlidingDays, but never past cap
    // No AbsoluteExpiry(from): the absolute cap is the user's PasswordExpiresUtc (from the DB), not a config span.
}

public sealed class RefreshTokenService(IConfiguration config) : IRefreshTokenService
{
    private readonly int _sliding = int.Parse(config["Jwt:RefreshSlidingDays"] ?? "7");

    public (string, string) Generate()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));   // 256 bits of entropy
        return (raw, Hash(raw));
    }

    // A plain SHA-256 is right HERE: the token is already long and random, so — unlike a password — it needs
    // no salt and no slow hash. We hash only so a database leak yields no usable tokens.
    public string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    // The sliding window from 'from', but CLAMPED so it never runs past the password-expiry cap.
    // A refresh token must never outlive the password, and this also keeps the DB's
    // CK_RefreshToken_Expiry constraint (AbsoluteExpiresUtc >= ExpiresUtc) satisfied.
    public DateTime SlidingExpiry(DateTime from, DateTime cap)
    {
        var sliding = from.AddDays(_sliding);
        return sliding < cap ? sliding : cap;
    }
}
```

> 🔑 **Why hash the refresh token but not treat it like a password?** Both get hashed so a stolen database is
> useless. But a password is short and human-chosen, so it needs a *slow, salted* hash (PBKDF2, §3). A refresh
> token is 256 random bits — unguessable by construction — so one fast SHA-256 is correct and cheaper. Right
> tool for the actual threat.

> 🔑 **Why the absolute cap moved out of config and into the database.** The old design used a fixed
> `Jwt:RefreshAbsoluteDays` (say, 30). But the real rule the business wants is *"a session may not outlive the
> password."* Passwords are governed by a **database-driven policy** — they expire 90 days after the last
> change — so the only correct source for a refresh token's hard cap is that same `PasswordExpiresUtc` column.
> A configured span would be a *second* copy of the policy that could silently drift out of sync (the
> "split-brain" [Phase 3](03-database-schema.md) warns about). One policy, one home: the database.

> ℹ️ **`IClock`** — [Phase 1](01-solution-setup.md)'s folder map named this as one of `Shared.Common`'s types
> but left it unbuilt; this is where we actually create it. The login and refresh handlers inject it instead of
> calling `DateTime.UtcNow` directly, so a test can pin "now" and assert an expiry to the second. Two files,
> the same interface-in-`Common` / implementation-in-`Infrastructure` split as `IUserRepository`:
> ```csharp
> // Erp.Shared.Common/IClock.cs
> public interface IClock { DateTime UtcNow { get; } }
>
> // Erp.Shared.Infrastructure/SystemClock.cs
> public sealed class SystemClock : IClock { public DateTime UtcNow => DateTime.UtcNow; }
> ```
> Register it once in `AddSharedInfrastructure`: `services.AddSingleton<IClock, SystemClock>();`

### The store (persist, look up, revoke)

The port lives in `Erp.Shared.Common/Security/IRefreshTokenRepository.cs` — the same home as `IUserRepository`
from [Phase 4b](04b-jwt-login.md); `Shared.Infrastructure` implements it with Dapper over the
[Phase 3](03-database-schema.md) procs:

```csharp
// src/Shared/Erp.Shared.Common/Security/IRefreshTokenRepository.cs
using System.Data;   // IDbTransaction

namespace Erp.Shared.Common;

public sealed record RefreshTokenRecord(
    long Id, string TokenHash, long UserId, Guid FamilyId, Guid SecurityStamp,
    DateTime ExpiresUtc, DateTime AbsoluteExpiresUtc, DateTime? RevokedUtc);

public interface IRefreshTokenRepository
{
    Task AddAsync(string tokenHash, long userId, Guid familyId, Guid securityStamp,
        DateTime expiresUtc, DateTime absoluteExpiresUtc, IDbTransaction tx, CancellationToken ct);
    Task<RefreshTokenRecord?> GetByHashAsync(string tokenHash, CancellationToken ct);
    Task RevokeAsync(string tokenHash, string? replacedByHash, IDbTransaction tx, CancellationToken ct);
    Task RevokeFamilyAsync(Guid familyId, IDbTransaction tx, CancellationToken ct);
    Task RevokeAllForUserAsync(long userId, IDbTransaction tx, CancellationToken ct);
}
```

`AddAsync` now also takes `securityStamp` — the value the caller snapshots from the user at mint time —
which maps straight to the `@SecurityStamp` parameter on `usp_RefreshToken_Insert`.

Each method is a one-line Dapper call to its matching `usp_RefreshToken_*` proc — the same shape as
`UserRepository` in [Phase 4b](04b-jwt-login.md). The one distinction to notice: `GetByHashAsync` is a pure
read, so it opens its **own** connection through `IDbConnectionFactory` (no transaction — [Phase 3b](03b-data-access.md));
every write instead runs on the handler's **transaction** (`tx.Connection!`, `transaction: tx`), which is what
lets a rotation add-the-new and revoke-the-old atomically.

`Erp.Shared.Infrastructure/Identity/RefreshTokenRepository.cs`:

```csharp
using System.Data;              // CommandType, IDbTransaction
using Dapper;                   // ExecuteAsync, QuerySingleOrDefaultAsync, CommandDefinition
using Erp.Shared.Common;        // RefreshTokenRecord, IRefreshTokenRepository
using Erp.Shared.Common.Data;   // IDbConnectionFactory

namespace Erp.Shared.Infrastructure;

public sealed class RefreshTokenRepository(IDbConnectionFactory factory) : IRefreshTokenRepository
{
    // WRITE — runs on the handler's transaction (rotation must be atomic with the revoke).
    public async Task AddAsync(string tokenHash, long userId, Guid familyId, Guid securityStamp,
        DateTime expiresUtc, DateTime absoluteExpiresUtc, IDbTransaction tx, CancellationToken ct)
    {
        await tx.Connection!.ExecuteAsync(new CommandDefinition(
            "[identity].usp_RefreshToken_Insert",
            new { TokenHash = tokenHash, UserId = userId, FamilyId = familyId, SecurityStamp = securityStamp,
                  ExpiresUtc = expiresUtc, AbsoluteExpiresUtc = absoluteExpiresUtc },
            transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }

    // READ — pure lookup, no transaction (Phase 3b), so it opens its own connection.
    public async Task<RefreshTokenRecord?> GetByHashAsync(string tokenHash, CancellationToken ct)
    {
        using var conn = factory.Create();

        return await conn.QuerySingleOrDefaultAsync<RefreshTokenRecord>(new CommandDefinition(
            "[identity].usp_RefreshToken_GetByHash",
            new { TokenHash = tokenHash },
            commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }

    // WRITE — revoke one token, recording what replaced it (the rotation grace-window caveat below).
    public async Task RevokeAsync(string tokenHash, string? replacedByHash, IDbTransaction tx, CancellationToken ct)
    {
        await tx.Connection!.ExecuteAsync(new CommandDefinition(
            "[identity].usp_RefreshToken_Revoke",
            new { TokenHash = tokenHash, ReplacedByHash = replacedByHash },
            transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }

    // WRITE — burn a whole lineage (reuse-detection breach, or logout).
    public async Task RevokeFamilyAsync(Guid familyId, IDbTransaction tx, CancellationToken ct)
    {
        await tx.Connection!.ExecuteAsync(new CommandDefinition(
            "[identity].usp_RefreshToken_RevokeFamily",
            new { FamilyId = familyId },
            transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }

    // WRITE — logout-all: every outstanding token for one user.
    public async Task RevokeAllForUserAsync(long userId, IDbTransaction tx, CancellationToken ct)
    {
        await tx.Connection!.ExecuteAsync(new CommandDefinition(
            "[identity].usp_RefreshToken_RevokeAllForUser",
            new { UserId = userId },
            transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }
}
```

The parameter object on each call maps 1:1 to that proc's `@`-parameters from [Phase 3](03-database-schema.md),
and `RefreshTokenRecord`'s properties line up with the `usp_RefreshToken_GetByHash` `SELECT` list, so Dapper
maps the row with no aliases. `[identity]` stays **bracketed** in every call — `IDENTITY` is a reserved keyword,
same note as the user procs.

Register both the service and the store in the composition root **now**, so this phase's checkpoint can actually
log in and refresh:

```csharp
// AddSharedInfrastructure() — the auth platform, alongside IUserRepository / IClock (Phase 4b)
services.AddSingleton<IRefreshTokenService, RefreshTokenService>();     // generate / hash / sliding expiry
services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();  // one Dapper proc call per method
```

([Phase 11](11-cross-cutting.md) recaps the whole registration block; these are the two lines this section adds.)

### Reminder — the transaction pattern

Refresh tokens are where the [Phase 3b](03b-data-access.md) transaction pattern earns its keep. A refresh
writes *two* rows in one breath — add the new token, revoke the old — and they must land **both or neither**,
so the login and refresh handlers wrap their writes in a transaction, the same moves you learned there:

```csharp
using var conn = factory.Create();
await conn.OpenAsync(ct);
using var tx = conn.BeginTransaction();
try
{
    await store.AddAsync(..., tx, ct);   // run each write ON that transaction
    tx.Commit();                          // seal it
}
catch
{
    tx.Rollback();                        // any failure → both rows vanish
    throw;
}
```

Remember **commit on success, roll back on throw**: if a guard throws before `tx.Commit()`, the `catch` rolls
everything back — and the `using` is the backstop if anything slips past it ([Phase 3b §6](03b-data-access.md)).

### Back to login — now issue a refresh token

Remember the access-token-only `LoginHandler` from §5? Every piece it was waiting on now exists — the
service, the store, `IClock`, and the transaction — so we return and finish it. Two changes: `LoginResult`
grows a `RefreshToken`, and the handler generates one, stores its **hash** in a brand-new token family, and
hands the **raw** token back to the client:

```csharp
// Extended from §5: now carries the refresh token too. Returned by BOTH login and refresh.
public sealed record LoginResult(string AccessToken, string RefreshToken, string Username, string Role);

public sealed class LoginHandler(
    IUserRepository users,
    IPasswordHasher hasher,
    IJwtTokenService jwt,
    IRefreshTokenService refreshTokens,   // generates + hashes the random refresh token
    IRefreshTokenRepository store,        // persists it
    IDbConnectionFactory factory,
    IClock clock) : ICommandHandler<LoginCommand, LoginResult>
{
    public async Task<LoginResult> Handle(LoginCommand cmd, CancellationToken ct)
    {
        var user = await users.GetByUsernameAsync(cmd.Username, ct);

        // Same generic error whether the user is missing OR the password is wrong.
        if (user is null || !user.IsActive || !hasher.Verify(cmd.Password, user.PasswordHash))
            throw new AuthenticationException("Invalid username or password.");

        var now = clock.UtcNow;

        // The DB-driven password policy also governs login: an expired password can't mint a session.
        if (now >= user.PasswordExpiresUtc)
            throw new AuthenticationException("Your password has expired. Please reset it before signing in.");

        var access = jwt.CreateAccessToken(user.Id, user.Username, user.Role);

        // Start a NEW token family for this login, storing the refresh token's HASH (never the raw value).
        // The token carries the user's SecurityStamp, and its hard cap IS the password-expiry date.
        var (rawRefresh, refreshHash) = refreshTokens.Generate();
        using var conn = factory.Create();
        await conn.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        try
        {
            await store.AddAsync(refreshHash, user.Id, familyId: Guid.NewGuid(),
                securityStamp: user.SecurityStamp,
                expiresUtc: refreshTokens.SlidingExpiry(now, user.PasswordExpiresUtc),
                absoluteExpiresUtc: user.PasswordExpiresUtc,   // hard cap comes from the DB, not config
                tx, ct);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        return new LoginResult(access, rawRefresh, user.Username, user.Role);   // client keeps the RAW refresh token
    }
}
```

Now every dependency traces back to something already built: `IUserRepository` (§5), `IPasswordHasher` (§3),
`IJwtTokenService` (§4), `IRefreshTokenService` + `IRefreshTokenRepository` (this section), `IClock` (recalled
earlier in this section), and `IDbConnectionFactory` (from [Phase 3b](03b-data-access.md), recalled just
above). No forward references — every piece was defined before it's used here.

### `POST /auth/refresh` — rotate, and catch the thief

The heart of the feature. Before the code, hold the *shape* in your head: the handler is a **ladder of
guards**, and the **order is deliberate** — the cheap in-memory checks and the theft trap run *before* the
database round-trip that reloads the user, so an attacker replaying a stolen token is turned away without
costing you a user lookup. Read it top to bottom:

1. **Look it up.** Hash the presented token, fetch its row. Unknown hash → reject (nothing to rotate).
2. **Reuse check.** Is that row *already* revoked? Then a spent token is being replayed — burn the whole family
   and stop. (This is the trap from the trace above.)
3. **Expiry check.** Both clocks must still be in the future: the sliding window (`ExpiresUtc`) *and* the
   password-expiry hard cap (`AbsoluteExpiresUtc`).
4. **Reload the user.** A refresh is the moment to notice a role change or a deactivation since login.
5. **Stamp check.** Does the user's current `SecurityStamp` still match the one baked into the token? A password
   change since mint voids the whole lineage.
6. **Rotate.** Mint a new token in the *same* family, revoke the old one, commit both together, hand back a
   fresh access + refresh pair.

Each rung maps to a real attack or edge case; the code below is just those six steps in order. It's a
host/platform slice, so it lives in `Erp.Api/Auth/Refresh/`:

```csharp
// src/Erp.Api/Auth/Refresh/RefreshHandler.cs
using System.Security.Authentication;   // AuthenticationException
using Erp.Shared.Common;                // IUserRepository, IRefreshTokenRepository, IClock
using Erp.Shared.Common.Data;           // IDbConnectionFactory
using Erp.Shared.Infrastructure;        // IJwtTokenService, IRefreshTokenService
using Erp.Shared.Messaging;             // ICommand, ICommandHandler

namespace Erp.Api.Auth;

public sealed record RefreshCommand(string RefreshToken) : ICommand<LoginResult>;

public sealed class RefreshHandler(
    IRefreshTokenService refreshTokens,
    IRefreshTokenRepository store,
    IUserRepository users,
    IJwtTokenService jwt,
    IDbConnectionFactory factory,
    IClock clock) : ICommandHandler<RefreshCommand, LoginResult>
{
    public async Task<LoginResult> Handle(RefreshCommand cmd, CancellationToken ct)
    {
        var presentedHash = refreshTokens.Hash(cmd.RefreshToken);
        var token = await store.GetByHashAsync(presentedHash, ct)
            ?? throw new AuthenticationException("Invalid refresh token.");   // unknown token

        var now = clock.UtcNow;

        // REUSE DETECTION: this token was already rotated away, yet here it is again → someone copied it.
        // Burn the whole family so neither the thief nor the victim keeps using the lineage.
        if (token.RevokedUtc is not null)
        {
            // Note the names: breachConn / breach. The rotation below opens its own conn / tx, and C# won't
            // let you reuse the name `conn` in two nested scopes of one method (error CS0136).
            using var breachConn = factory.Create();
            await breachConn.OpenAsync(ct);
            using var breach = breachConn.BeginTransaction();
            try
            {
                await store.RevokeFamilyAsync(token.FamilyId, breach, ct);
                breach.Commit();
            }
            catch
            {
                breach.Rollback();
                throw;
            }
            throw new AuthenticationException("Refresh token reuse detected — session revoked. Please log in again.");
        }

        // Expiry: BOTH the sliding window and the absolute cap (the password-expiry date) must be in the future.
        if (now >= token.ExpiresUtc || now >= token.AbsoluteExpiresUtc)
            throw new AuthenticationException("Refresh token expired. Please log in again.");

        // Reload the user fresh — a refresh is the moment to notice a role change or deactivation since login.
        var user = await users.GetByIdAsync(token.UserId, ct);
        if (user is null || !user.IsActive)
            throw new AuthenticationException("Account is no longer active.");

        // PASSWORD-CHANGE DETECTION: the stamp we snapshotted at mint no longer matches the user's current one,
        // so the password was changed since this token was issued → every token in this lineage is now void.
        if (token.SecurityStamp != user.SecurityStamp)
            throw new AuthenticationException("Credentials changed. Please log in again.");

        // ROTATE: a new token in the SAME family. The sliding window resets (clamped to the password-expiry
        // cap); the hard cap is the user's CURRENT PasswordExpiresUtc — unchanged here, since the stamp matched.
        var (rawNew, hashNew) = refreshTokens.Generate();
        using var conn = factory.Create();
        await conn.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        try
        {
            await store.AddAsync(hashNew, user.Id, token.FamilyId, user.SecurityStamp,
                refreshTokens.SlidingExpiry(now, user.PasswordExpiresUtc), user.PasswordExpiresUtc, tx, ct);
            await store.RevokeAsync(presentedHash, replacedByHash: hashNew, tx, ct);   // old one spent
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        var access = jwt.CreateAccessToken(user.Id, user.Username, user.Role);
        return new LoginResult(access, rawNew, user.Username, user.Role);
    }
}
```

> 🔑 **Reuse detection is what makes a long-lived credential safe.** Rotation alone limits a stolen token to
> one use — but that one use might be the attacker's. Detection closes it: because the legitimate client also
> rotates, one of the two copies eventually presents an already-spent token, and *that* presentation reveals
> the theft and kills the family. You don't have to catch the theft in the act; you only need the two copies to
> diverge, which rotation guarantees.

## Practice

Before continuing, implement the endpoint yourself.

Create the following file:

```text
Erp.Api/
└── Auth/
    └── Refresh/
        └── RefreshEndpoint.cs
```

The endpoint should expose:

```http
POST /auth/refresh
```

Without looking ahead, try to build it using what you've already learned.

Your endpoint should:

- Accept a refresh token from the request body, as a small `RefreshRequest` record.
- Build a `RefreshCommand` from it.
- Send the command using `ISender`.
- Return the `LoginResult` from the handler.
- Allow anonymous callers — think about why, before you read the next section.

You already have everything needed:

- `RefreshCommand` and `RefreshHandler` (just above)
- `LoginResult` (from [Part 2](04b-jwt-login.md), now carrying a refresh token)
- `ISender` — the mediator interface from [Phase 2](02-mediator-own-vs-free.md)
- Carter's `ICarterModule`, auto-discovered by `app.MapCarter()`

> ❓ **Where does the `401` come from?** You don't write it. `RefreshHandler` throws
> `AuthenticationException` on every failure path, and [Phase 11](11-cross-cutting.md)'s global exception
> handler is what maps that exception to a `401`. The endpoint stays a two-line adapter — that's the point.

The request flow should look like this:

```text
POST /auth/refresh
        │
        ▼
RefreshEndpoint
        │
        ▼
RefreshCommand
        │
        ▼
RefreshHandler
        │
        ▼
Validate refresh token
Detect token reuse
Check expiry
Verify security stamp
Rotate refresh token
Generate new access token
        │
        ▼
Return LoginResult
```

If you can implement `RefreshEndpoint.cs` without referring to the next section, you've successfully connected
the HTTP edge to your vertical slice through the mediator.

## Answer Key

Your implementation should look similar to the following:

```csharp
// src/Erp.Api/Auth/Refresh/RefreshEndpoint.cs
using Carter;
using Erp.Shared.Messaging;              // ISender
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Api.Auth;

// The wire shape — a JSON body of { "refreshToken": "..." }. Kept separate from RefreshCommand so the
// HTTP contract and the mediator contract can evolve independently.
public sealed record RefreshRequest(string RefreshToken);

public sealed class RefreshEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/auth/refresh", async (RefreshRequest body, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new RefreshCommand(body.RefreshToken), ct)))
        .AllowAnonymous();   // the ACCESS token is expired by now — the refresh token IS the credential
}
```

We'll add the two **logout** routes to this same Carter module in a moment, once their handlers exist.

### Why is `AllowAnonymous()` required?

A user's access token may already be expired when they call the refresh endpoint. If the endpoint required authentication, ASP.NET Core would reject the request before your `RefreshHandler` ever had a chance to validate the refresh token.

Instead, the refresh token itself acts as the credential. The handler performs all of the security checks:

- Verifies the refresh token exists.
- Detects token reuse.
- Checks sliding and absolute expiration.
- Ensures the account is still active.
- Verifies the user's security stamp hasn't changed.
- Rotates the refresh token.
- Issues a brand-new access token.

In other words, **`/auth/refresh` authenticates using the refresh token, not the access token**, which is why the endpoint must allow anonymous requests.

### The two password-driven safeguards, in one place

The refresh handler enforces two rules that both trace back to the **database's password policy**, not to
anything configured in the app. They're easy to blur together, so here they are side by side:

| Safeguard | What it checks | What triggers it | Effect |
|---|---|---|---|
| **Security stamp** | `token.SecurityStamp == user.SecurityStamp` | A **password change** rotates the user's stamp (`NEWID()`) | Every outstanding refresh token, on every device, fails its next refresh — an **instant, global logout** |
| **Password-expiry cap** | `now < token.AbsoluteExpiresUtc` (= the user's `PasswordExpiresUtc`) | The **90-day** password lifetime elapsing | A refresh token can never outlive the password it was minted under |

> 💡 **Why the stamp beats "just revoke all tokens on password change."** You *could* run
> `usp_RefreshToken_RevokeAllForUser` inside the change-password proc's transaction. The stamp is better for a
> beginner build for two reasons: (1) it's **one column compare**, not a table sweep, and it can't miss a
> token that's mid-flight in another request; (2) it's **self-healing** — even a token that somehow dodged a
> revoke sweep still fails, because it's the *value* that no longer agrees. The password change doesn't have to
> hunt tokens down; it just changes the fact they were all vouching for.

> ❓ **"Where does the stamp actually change?"** In `usp_AppUser_ChangePassword`
> ([Phase 3](03-database-schema.md)): the same atomic write that stores the new hash also does
> `SecurityStamp = NEWID()` and `PasswordExpiresUtc = DATEADD(day, 90, …)`. That one proc is the single origin
> of *both* safeguards. The refresh handler is purely a **reader** of the consequences — it never sets a stamp
> or an expiry itself, which is exactly why the policy stays in one home.

### Logout and logout-all

Logging out is just *revoking* — one device, or every device:

```csharp
// Erp.Api/Auth/Logout — anonymous: the access token may already be expired, and that's fine here.
public sealed record LogoutCommand(string RefreshToken) : ICommand<Unit>;
public sealed class LogoutHandler(IRefreshTokenService refreshTokens, IRefreshTokenRepository store, IDbConnectionFactory factory)
    : ICommandHandler<LogoutCommand, Unit>
{
    public async Task<Unit> Handle(LogoutCommand cmd, CancellationToken ct)
    {
        var token = await store.GetByHashAsync(refreshTokens.Hash(cmd.RefreshToken), ct);
        if (token is not null)   // unknown/already-gone token → nothing to do; logout is idempotent
        {
            using var conn = factory.Create();
            await conn.OpenAsync(ct);
            using var tx = conn.BeginTransaction();
            try
            {
                await store.RevokeFamilyAsync(token.FamilyId, tx, ct);   // this whole session out
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
        return Unit.Value;
    }
}

// "Log me out everywhere" — needs a valid ACCESS token, so it acts on the authenticated caller.
public sealed record LogoutAllCommand : ICommand<Unit>;
public sealed class LogoutAllHandler(ICurrentUser currentUser, IRefreshTokenRepository store, IDbConnectionFactory factory)
    : ICommandHandler<LogoutAllCommand, Unit>
{
    public async Task<Unit> Handle(LogoutAllCommand cmd, CancellationToken ct)
    {
        using var conn = factory.Create();
        await conn.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        try
        {
            await store.RevokeAllForUserAsync(currentUser.UserId, tx, ct);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
        return Unit.Value;
    }
}
```

### The endpoints — all three routes in one Carter module

Now grow the `RefreshEndpoint` you wrote above. All three routes are about the refresh token, so they share a
module; `RefreshRequest` is reused for the two that take one in the body:

```csharp
// src/Erp.Api/Auth/Refresh/RefreshEndpoint.cs — the finished version
public sealed record RefreshRequest(string RefreshToken);

public sealed class RefreshEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // Both AllowAnonymous: the ACCESS token is likely expired here — the refresh token IS the credential.
        app.MapPost("/auth/refresh", async (RefreshRequest body, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new RefreshCommand(body.RefreshToken), ct))).AllowAnonymous();

        app.MapPost("/auth/logout", async (RefreshRequest body, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new LogoutCommand(body.RefreshToken), ct);
            return Results.NoContent();
        }).AllowAnonymous();

        // logout-all acts on the current user, so it DOES require a valid access token —
        // it's the one route here that needs to know WHO you are (it reads ICurrentUser).
        app.MapPost("/auth/logout-all", async (ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new LogoutAllCommand(), ct);
            return Results.NoContent();
        }).RequireAuthorization();
    }
}
```

### Register this part's handlers

Same trap as [Part 2](04b-jwt-login.md): these slices live in the **host** assembly, so no module scan finds
them. Three more lines in `Program.cs`, next to the login registration:

```csharp
builder.Services.AddScoped<IRequestHandler<RefreshCommand, LoginResult>, RefreshHandler>();
builder.Services.AddScoped<IRequestHandler<LogoutCommand, Unit>, LogoutHandler>();
builder.Services.AddScoped<IRequestHandler<LogoutAllCommand, Unit>, LogoutAllHandler>();
```

> ⚠️ **Honest caveats — don't skip these.**
> - **Rotation can false-positive on a retry.** If the client fires a refresh, it succeeds server-side, but
>   the *response* is lost (dropped connection), and the client retries the same token — now revoked — we nuke
>   the family. Strict rotation errs toward security. If that hurts real users, soften it with a short grace
>   window (a just-revoked token may re-rotate to its *recorded* replacement, via `ReplacedByHash`) or an
>   idempotency key. Understand the tradeoff before loosening it.
> - **Where the client stores the refresh token matters.** In a browser SPA, a refresh token in JS-readable
>   storage is exposed to XSS. The safer delivery is an `HttpOnly; Secure; SameSite` cookie the browser sends
>   automatically and scripts can't read. This API-first tutorial returns it in the response body for
>   simplicity; for a browser client, prefer the cookie.
> - **Revoked/expired rows accumulate.** A periodic cleanup (a scheduled job or a `DELETE … WHERE RevokedUtc <
>   …` proc) keeps the table small. Housekeeping, not correctness — noted, out of scope here.
> - **An expired password would be a dead end — so the next part fixes it.** Once `PasswordExpiresUtc` passes,
>   refresh fails *and* login rejects the password (both by design — the DB policy governs both). The escape
>   hatch is a **change/reset-password** flow over `usp_AppUser_ChangePassword` (which rotates the stamp and
>   pushes the expiry). We build exactly that next, in [Part 4](04d-jwt-change-password.md) — including the
>   subtle reason it must be anonymous. (A full **forgot-password** path — an emailed reset token for users who
>   *don't* know the old password — stays a [Phase 13](13-wrap-up-checklist.md) extension.)

## 11. A second gate — requiring an API key from the frontend

Everything from §3 to §10 answered **one** question: *who is the **user**?* An API key answers a **different**
one: *which **client application** is calling at all?* Those are orthogonal, and real systems often want both
gates — so we add the second one now.

### First, feel the hole

Look back at the login endpoint (§6): it's `.AllowAnonymous()`. It **has** to be — you can't require a token
to get a token (chicken-and-egg). So the JWT gate protects everything *except the one door everybody walks
through first*. Right now **anything on the internet** — `curl`, a scraper, a competitor's app, a botnet —
can hammer `/auth/login`, probe every route, and burn your password-check CPU (§3 made that check
*deliberately* slow, remember). The user gate can't help here: at the front door there's no user *yet* to
identify. You need a gate that fires **before** authentication and asks a blunter question: *"is this even one
of our known clients?"*

> 💡 **Analogy.** The JWT wristband (§2) is per-**person**. The API key is the **vendor pass painted on the
> delivery truck** — it says *which company's* truck is at the loading dock, no matter who's driving. Two
> gates, two jobs: the truck needs the vendor pass to reach the dock (**client** gate), and the driver still
> needs a wristband to go inside (**user** gate). Neither one alone is enough.

An **API key** is just a long random string your API issues to each trusted client (your web app, your mobile
app, a partner integration). The client sends it on **every** request in a header — conventionally
`X-Api-Key` — and the server compares it to the key(s) it expects. No match → the request never reaches an
endpoint.

### Step 1 — the key is just another secret

You already built the whole "where does config come from" story in §7. API keys ride the *exact same*
`IConfiguration` pipeline as `Jwt:SigningKey` — they're secrets, so they follow the same rules (User Secrets
or environment variables; **never** committed).

Although we're only building one website and one mobile app, it's already worth thinking like a production
system: **issue one API key per client application**, and store them as a **map of client name → key** rather
than a bare list.

Why a map and not a list? Because a list can only answer "is this key valid?" A map also answers "*which
client* is this?" — and that second answer is what lets you log per client, rate-limit one client without
touching the others, and revoke the mobile app's key at 2am without knocking your website offline. The name
costs you nothing and buys you all of that.

During development you could place them in `appsettings.json`:

```json
{
  "ApiKeys": {
    "web": "web-live-abc123",
    "mobile": "mobile-live-xyz789"
  }
}
```

But just like the JWT signing key, these should **not** live in source control.

For local development, use User Secrets:

```bash
dotnet user-secrets set "ApiKeys:web"    "web-live-abc123"
dotnet user-secrets set "ApiKeys:mobile" "mobile-live-xyz789"
```

For production, use environment variables instead:

```text
ApiKeys__web=web-live-abc123
ApiKeys__mobile=mobile-live-xyz789
```

Notice the double underscore (`__`). ASP.NET Core automatically translates that into the same nested
structure as the JSON above — `__` is how you say "child key" in an environment variable name.

We're only registering two clients here, but the same approach scales to dozens without changing any
application code: add a line to config, hand the new client its key.

### Step 2 — the middleware

The check gates the *whole pipeline*, so it's a middleware in the host, not a feature slice —
`Erp.Api/Security/ApiKeyMiddleware.cs`:

```csharp
// src/Erp.Api/Security/ApiKeyMiddleware.cs
using System.Security.Cryptography;
using System.Text;

namespace Erp.Api.Security;

public sealed class ApiKeyMiddleware(RequestDelegate next, IConfiguration config)
{
    private const string HeaderName = "X-Api-Key";

    // Read the configured "ApiKeys" map (client name → key) once at startup, pre-encoded for comparison.
    private readonly (string Client, byte[] Key)[] _keys =
        config.GetSection("ApiKeys").Get<Dictionary<string, string>>()
            ?.Where(kv => !string.IsNullOrEmpty(kv.Value))
             .Select(kv => (kv.Key, Encoding.UTF8.GetBytes(kv.Value)))
             .ToArray()
        ?? [];

    public async Task InvokeAsync(HttpContext context)
    {
        // Let CORS preflight through: a browser NEVER sends custom headers (X-Api-Key) on an OPTIONS
        // preflight, so gating it here would 401 the preflight and the real request would never be sent.
        // Also wave through Swagger UI and the health check so the app stays browsable in dev.
        if (HttpMethods.IsOptions(context.Request.Method) || IsExempt(context.Request.Path))
        {
            await next(context);
            return;
        }

        context.Request.Headers.TryGetValue(HeaderName, out var provided);
        var client = Match(provided!);

        if (client is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;   // unknown client — stop here
            await context.Response.WriteAsJsonAsync(new { error = "Missing or invalid API key." });
            return;   // short-circuit: never call next() → no endpoint runs
        }

        // Remember WHICH client this is, so downstream (logging, rate limiting) can tell them apart.
        context.Items["ClientId"] = client;
        await next(context);
    }

    private static bool IsExempt(PathString path) =>
        path.StartsWithSegments("/health") || path.StartsWithSegments("/swagger");

    // Return the client name whose key matches, or null. Same fixed-time comparison as the §3 password
    // check: FixedTimeEquals returns false on a length mismatch (no throw), so timing can't leak a key.
    private string? Match(string provided)
    {
        if (string.IsNullOrEmpty(provided)) return null;
        var bytes = Encoding.UTF8.GetBytes(provided);
        foreach (var (client, key) in _keys)
        {
            if (CryptographicOperations.FixedTimeEquals(bytes, key))
                return client;
        }
        return null;
    }
}
```

Three details worth slowing down for:

- **`Match` returns the client's *name*, not a `bool`.** That's the whole reason for the map. Once we know
  the request came from `"mobile"`, we stash it in `context.Items["ClientId"]` — a per-request scratchpad that
  any later middleware or endpoint can read. Nothing uses it yet; it's the hook that makes per-client logging
  and rate limiting possible later without touching this file again.
- **The `IsOptions` escape hatch is not laziness.** Before a browser sends a cross-origin request that carries
  a custom header, it first sends an `OPTIONS` *preflight* — and that preflight deliberately carries **no**
  custom headers, so it can't carry `X-Api-Key` either. Gate the preflight and it 401s, the browser gives up,
  and the real request is never sent at all. You'd see a CORS error and spend an hour blaming CORS.
- **`/health` and `/swagger` are exempt** so you can still open the app in a browser (which can't set a
  custom header by hand) and so a load balancer can poll health without holding a secret.

> ⚠️ **An API key is not authentication.** It identifies the *client app*, not the *user*, and every copy of
> your mobile app ships the same key — anyone can extract it. It raises the cost of casual abuse and lets you
> revoke a client; it does **not** prove who is calling. The JWT does that. Two different jobs, which is
> exactly why we run both gates.

### Step 3 — wire it *first*

Order is everything (§7). The API key gate goes in **front of** authentication — reject unknown clients before
you spend a cycle validating tokens:

```csharp
app.UseMiddleware<ApiKeyMiddleware>();   // 0) is this even one of our known client apps?
app.UseAuthentication();                 // 1) who is the user?
app.UseAuthorization();                  // 2) are they allowed to do this?
```

Now the client sends **both** headers on every call — the API key (which app) *and*, once logged in, the
bearer token (which user):

```bash
curl "http://localhost:5234/sales/ping?name=world" \
     -H "X-Api-Key: <your-api-key>" \
     -H "Authorization: Bearer eyJ..."
# no X-Api-Key at all → 401 before authentication ever runs, even on /auth/login
```

In our ERP, the website and mobile app each have their own API key.

Website:

```http
GET /sales
X-Api-Key: web-live-abc123
Authorization: Bearer eyJ...
```

Mobile:

```http
GET /sales
X-Api-Key: mobile-live-xyz789
Authorization: Bearer eyJ...
```

Notice something important.

Both requests could belong to **the exact same employee**.

The API key answers:

> "Which client application is calling?"

The JWT answers:

> "Which user is calling?"

Those are two different identities.

Having separate API keys also means you can revoke or rate-limit **only the mobile application** without
affecting the website, or vice versa.

> ❓ **You might be wondering: "Isn't this just JWT again?"** No — and the login hole is the clean proof. JWT
> *cannot* guard `/auth/login`, because login is anonymous by necessity. The API key is the **only** gate you
> can put in front of the login door itself. More broadly: **JWT = which *user*; API key = which *app*.**
> Different question, different answer, and they stack.

> 🧭 **From WebForms.** This is the app-level key you might have checked in `Global.asax`'s `BeginRequest`
> before any page ran — or the `<clientKey>` a partner had to pass into your old SOAP / `[WebMethod]` service
> before you'd answer. Same idea, now expressed as one middleware at the very front of the pipeline.

> ⚠️ **The gate will break Swagger's "Try it out" — and the fix isn't the one you'd guess.** `IsExempt` lets
> the `/swagger` *page itself* load, so it looks like everything is fine. But when you hit **Try it out**, the
> page fires a request at `/sales-orders`, and *that* path is not exempt. You get
> `401 {"error":"Missing or invalid API key."}` from a UI that never asked you for a key.
>
> Don't widen the exemption — that would punch a hole straight through the gate you just built. Instead, teach
> Swagger to **send the header**, the same way it already knows to send the bearer token. Add a second security
> definition next to the `Bearer` one from [Part 2](04b-jwt-login.md):
>
> ```csharp
> // Program.cs, inside AddSwaggerGen(options => { ... })
> options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
> {
>     Name = "X-Api-Key",
>     Type = SecuritySchemeType.ApiKey,
>     In = ParameterLocation.Header,
>     Description = "The client application's API key (Phase 4 §11)."
> });
> options.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
> {
>     { new OpenApiSecuritySchemeReference("ApiKey", doc, null), new List<string>() }
> });
> ```
>
> Now Swagger's **Authorize** button shows two boxes — paste the API key in one, the JWT in the other — and
> every "Try it out" carries both headers. This is the honest version: the tooling adapts to the security
> model, not the other way around.

### The honest caveat you must not skip

> ⚠️ **A browser frontend cannot keep an API key secret.** If your client is a browser SPA, the key ships in
> the JavaScript bundle and rides on every request — **anyone** can read it in the network tab or devtools in
> about ten seconds.
>
> A **mobile app raises the bar, but it cannot guarantee secrecy forever either.** A determined attacker can
> eventually extract the key by reverse engineering the app or inspecting it on a compromised device.
>
> So an API key should **identify the client application**, not replace authentication. In a browser it's a
> coarse outer filter; in a mobile app it's a somewhat stronger one—but neither should ever be considered the
> real security boundary.
>
> What it genuinely buys you is still valuable:
> - it keeps your login door (and the slow §3 hash) from facing the *raw, anonymous* internet;
> - it lets you tell clients apart — issue one key per app and you can **rate-limit or revoke a single client**
>   without touching the others.
>
> What it must **never** do is replace user auth. The API key is the outer, coarse gate; **the JWT plus the
> §10 rotation and reuse checks are the real security.** Treat a leaked API key as something to rotate, not as
> a catastrophic breach.
>
> 🔑 **Where an API key *is* a true secret:** server-to-server callers, a **backend-for-frontend**, a
> mobile/desktop app's own backend, or a trusted partner integration — anywhere the key lives on a machine the
> public can't read. That's when it becomes a genuine security boundary rather than simply identifying a client
> application.

> 🔑 **Scaling further.** The map above already handles a handful of clients. Past that, move the keys out of
> config and into a table — and, exactly like refresh tokens (§10), store their **hashes**, not the raw values,
> so a config or database leak yields nothing usable. Look each incoming key up by its SHA-256 hash. The
> middleware's shape doesn't change at all; only where `_keys` is loaded from. Rotate keys on a schedule, and
> immediately if one leaks.

---

## Checkpoint — the gate, the rotation, and the trap for a thief ✅

Part 3 added two independent things, so prove them in that order: first the **API-key gate** (which now sits
in front of everything), then **rotation and reuse detection**.

Run the app, and set both variables — from here on, *every* call carries the key:

```bash
BASE=http://localhost:5234
KEY=web-live-abc123        # ← whichever key you actually configured in §11
```

**1) The gate fires before login itself.** This is the whole point of an app-level key: it guards the
anonymous door that a JWT structurally cannot.

```bash
curl -i -X POST "$BASE/auth/login" -H "Content-Type: application/json" \
     -d '{"username":"rep","password":"Password123!"}'
# → 401 Unauthorized   { "error": "Missing or invalid API key." }
#   We never reached LoginHandler. A wrong key looks identical.
```

This 401 is written by the middleware itself, so — unlike the handler-thrown ones — it is **already correct
today**, before Phase 11.

**2) Log in with the key, and notice what's new in the response.**

```bash
curl -s -X POST "$BASE/auth/login" -H "X-Api-Key: $KEY" \
     -H "Content-Type: application/json" \
     -d '{"username":"rep","password":"Password123!"}'
# → {"accessToken":"eyJ...","refreshToken":"3Yb2...","username":"rep","role":"SalesRep"}
```

A `refreshToken` now sits beside the access token — §10 extended `LoginResult` to four members. Save both:

```bash
ACCESS=<paste accessToken>
REFRESH=<paste refreshToken>
```

**3) Rotation: spend the refresh token, get a brand-new pair.**

```bash
curl -s -X POST "$BASE/auth/refresh" -H "X-Api-Key: $KEY" \
     -H "Content-Type: application/json" \
     -d "{\"refreshToken\":\"$REFRESH\"}"
# → 200 OK, a DIFFERENT accessToken and a DIFFERENT refreshToken
```

Compare the two refresh tokens: they must not match. That difference *is* the rotation. Save the new one as
`REFRESH2`.

**4) Reuse detection: replay the token you just spent.** This is the moment the design earns its keep —
pretend you're a thief who copied `REFRESH` before the real client used it.

```bash
curl -i -X POST "$BASE/auth/refresh" -H "X-Api-Key: $KEY" \
     -H "Content-Type: application/json" \
     -d "{\"refreshToken\":\"$REFRESH\"}"
# → "Refresh token reuse detected — session revoked. Please log in again."
```

Now the part people miss. `REFRESH2` — the *honest* client's perfectly good token, which you never replayed —
is dead too:

```bash
curl -i -X POST "$BASE/auth/refresh" -H "X-Api-Key: $KEY" \
     -H "Content-Type: application/json" \
     -d "{\"refreshToken\":\"$REFRESH2\"}"
# → "Refresh token reuse detected — session revoked. Please log in again."
```

Trace *why* it reports reuse rather than "invalid": revoking the family stamped `RevokedUtc` on **every**
token in the lineage, `REFRESH2` included. So when it arrives, the handler finds a row whose `RevokedUtc` is
set and takes the very same reuse branch. (You'd only see `Invalid refresh token.` for a token the database
has never heard of — a typo, or one from a different login.)

That is deliberate, and it's the trade-off worth understanding: when two copies of one lineage exist, the
server **cannot tell the thief from the victim**, so it burns the family and makes both log in again. A
brief annoyance for the real user; a dead end for the thief.

> ⚠️ Steps 3 and 4's failures are thrown `AuthenticationException`s, so **today they arrive as a 500** with
> the right message. [Phase 11](11-cross-cutting.md) turns them into 401s. Read the message, not the status —
> the same caveat as [Part 2](04b-jwt-login.md).

**5) Logout revokes the session.** That family is now burned, so log in again to get a fresh pair — call its
refresh token `REFRESH3` — and hand it to logout:

```bash
curl -i -X POST "$BASE/auth/logout" -H "X-Api-Key: $KEY" \
     -H "Content-Type: application/json" -d "{\"refreshToken\":\"$REFRESH3\"}"
# → 204 No Content

# Refreshing with it now fails: logout revoked the whole family.
curl -i -X POST "$BASE/auth/refresh" -H "X-Api-Key: $KEY" \
     -H "Content-Type: application/json" -d "{\"refreshToken\":\"$REFRESH3\"}"
# → "Refresh token reuse detected — session revoked. Please log in again."
```

> ℹ️ Logout is **idempotent**: hand it a token the database doesn't know, or one already revoked, and it still
> returns `204`. Logging out twice is not an error, and a logout endpoint that leaked "that token doesn't
> exist" would be a small oracle for an attacker.

### You're done when

| # | You proved | Expected |
|---|---|---|
| 1 | The API-key gate runs before authentication | no/bad key, even on `/auth/login` → **401** `Missing or invalid API key.` |
| 2 | Login now issues a **pair** | **200** + `accessToken` **and** `refreshToken` |
| 3 | Refresh **rotates** | **200**, with a *different* refresh token than you sent |
| 4 | Reuse burns the whole family | replaying a spent token fails — **and so does the rotated one** |
| 5 | Logout revokes | **204**, after which that token can't refresh |

If all five rows match, the session machinery is real: short access tokens, rotating refresh tokens, replay
detection, and an outer gate that knows which *app* is calling.

---

> 👉 Next: [Phase 4 (Part 4 of 5) — Changing & Resetting a Password](04d-jwt-change-password.md) — expose the change-password proc as a slice, and see why it must be anonymous to rescue an expired login.
