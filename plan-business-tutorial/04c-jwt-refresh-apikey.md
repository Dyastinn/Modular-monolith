# Phase 4 (Part 3 of 4) — Refresh Tokens & the API-Key Gate

> **Phase 4 is split into four parts** — do them in order:
> 1. [Passwords & access tokens](04-jwt-auth.md)
> 2. [Login, validation & authorization](04b-jwt-login.md)
> 3. **Refresh tokens & the API-key gate** ← you are here
> 4. [Request flow, checkpoint & common mistakes](04d-jwt-wrap-up.md)

> Builds on [Part 2](04b-jwt-login.md): you can already log in, validate tokens, read the caller through `ICurrentUser`, and authorize by role. Two problems remain — a 15-minute access token means re-typing your password constantly (§10 fixes it with refresh tokens), and the anonymous login door faces the raw internet (§11 adds a client gate).

---

## 10. Step 7 — Refresh tokens: staying logged in without staying exposed

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

### The refresh-token service (generate, hash, expiry policy)

`Erp.Shared.Infrastructure/Security/RefreshTokenService.cs` — the random token, its one-way hash, and the
sliding/absolute expiry policy, in one place:

```csharp
public interface IRefreshTokenService
{
    (string RawToken, string TokenHash) Generate();   // a new random token + its hash
    string Hash(string rawToken);                       // hash an incoming token, to look it up
    DateTime SlidingExpiry(DateTime from);              // from + Jwt:RefreshSlidingDays
    DateTime AbsoluteExpiry(DateTime from);             // from + Jwt:RefreshAbsoluteDays
}

public sealed class RefreshTokenService(IConfiguration config) : IRefreshTokenService
{
    private readonly int _sliding  = int.Parse(config["Jwt:RefreshSlidingDays"]  ?? "7");
    private readonly int _absolute = int.Parse(config["Jwt:RefreshAbsoluteDays"] ?? "30");

    public (string, string) Generate()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));   // 256 bits of entropy
        return (raw, Hash(raw));
    }

    // A plain SHA-256 is right HERE: the token is already long and random, so — unlike a password — it needs
    // no salt and no slow hash. We hash only so a database leak yields no usable tokens.
    public string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    public DateTime SlidingExpiry(DateTime from)  => from.AddDays(_sliding);
    public DateTime AbsoluteExpiry(DateTime from) => from.AddDays(_absolute);
}
```

> 🔑 **Why hash the refresh token but not treat it like a password?** Both get hashed so a stolen database is
> useless. But a password is short and human-chosen, so it needs a *slow, salted* hash (PBKDF2, §3). A refresh
> token is 256 random bits — unguessable by construction — so one fast SHA-256 is correct and cheaper. Right
> tool for the actual threat.

> ℹ️ **`IClock`** — the trivial abstraction listed in `Shared.Common` back in [Phase 1](01-solution-setup.md),
> now earning its keep. The login and refresh handlers inject it instead of calling `DateTime.UtcNow` directly,
> so a test can pin "now" and assert an expiry to the second:
> ```csharp
> public interface IClock { DateTime UtcNow { get; } }                                       // Erp.Shared.Common
> public sealed class SystemClock : IClock { public DateTime UtcNow => DateTime.UtcNow; }     // Erp.Shared.Infrastructure
> ```
> Register it once in `AddSharedInfrastructure`: `services.AddSingleton<IClock, SystemClock>();`

### The store (persist, look up, revoke)

The port the handlers use; `Shared.Infrastructure` implements it with Dapper over the [Phase 3](03-database-schema.md)
procs:

```csharp
public sealed record RefreshTokenRecord(
    long Id, string TokenHash, long UserId, Guid FamilyId,
    DateTime ExpiresUtc, DateTime AbsoluteExpiresUtc, DateTime? RevokedUtc);

public interface IRefreshTokenRepository
{
    Task AddAsync(string tokenHash, long userId, Guid familyId,
        DateTime expiresUtc, DateTime absoluteExpiresUtc, IDbTransaction tx, CancellationToken ct);
    Task<RefreshTokenRecord?> GetByHashAsync(string tokenHash, CancellationToken ct);
    Task RevokeAsync(string tokenHash, string? replacedByHash, IDbTransaction tx, CancellationToken ct);
    Task RevokeFamilyAsync(Guid familyId, IDbTransaction tx, CancellationToken ct);
    Task RevokeAllForUserAsync(long userId, IDbTransaction tx, CancellationToken ct);
}
```

Each method is a one-line Dapper call to its matching `usp_RefreshToken_*` proc — the same shape as every
other repository in this course, so the bodies are elided. `GetByHashAsync` reads on `IDbConnectionFactory`
(no transaction); the writes take the handler's transaction, so a rotation is atomic.

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

        var access = jwt.CreateAccessToken(user.Id, user.Username, user.Role);

        // Start a NEW token family for this login, storing the refresh token's HASH (never the raw value).
        var (rawRefresh, refreshHash) = refreshTokens.Generate();
        var now = clock.UtcNow;
        using var conn = factory.Create();
        await conn.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        try
        {
            await store.AddAsync(refreshHash, user.Id, familyId: Guid.NewGuid(),
                expiresUtc: refreshTokens.SlidingExpiry(now), absoluteExpiresUtc: refreshTokens.AbsoluteExpiry(now),
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

The heart of the feature. Read the guards top to bottom; each maps to a real attack or edge case. It's a
host/platform slice, so it lives in `Erp.Api/Auth/Refresh/`:

```csharp
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
            using var conn = factory.Create();
            await conn.OpenAsync(ct);
            using var breach = conn.BeginTransaction();
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

        // Expiry: BOTH the sliding window and the absolute family cap must still be in the future.
        if (now >= token.ExpiresUtc || now >= token.AbsoluteExpiresUtc)
            throw new AuthenticationException("Refresh token expired. Please log in again.");

        // Reload the user fresh — a refresh is the moment to notice a role change or deactivation since login.
        var user = await users.GetByIdAsync(token.UserId, ct);
        if (user is null || !user.IsActive)
            throw new AuthenticationException("Account is no longer active.");

        // ROTATE: a new token in the SAME family; the sliding window resets, the absolute cap is carried over.
        var (rawNew, hashNew) = refreshTokens.Generate();
        using var conn = factory.Create();
        await conn.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        try
        {
            await store.AddAsync(hashNew, user.Id, token.FamilyId,
                refreshTokens.SlidingExpiry(now), token.AbsoluteExpiresUtc, tx, ct);
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

### The endpoints

```csharp
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

        // logout-all acts on the current user, so it DOES require a valid access token.
        app.MapPost("/auth/logout-all", async (ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new LogoutAllCommand(), ct);
            return Results.NoContent();
        }).RequireAuthorization();
    }
}
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

You already built the whole "where does config come from" story in §7. An API key rides the *exact same*
`IConfiguration` pipeline as `Jwt:SigningKey` — it's a secret, so it follows the same rules (User Secrets or
an environment variable; **never** committed). In `appsettings.json`, only the empty placeholder:

```json
{
  "ApiKey": ""   // the expected client key — a real secret, same handling as Jwt:SigningKey (§7)
}
```

```bash
dotnet user-secrets set "ApiKey" "$(openssl rand -base64 32)"   # local dev; prod uses ApiKey env var
```

### Step 2 — the middleware

The check gates the *whole pipeline*, so it's a middleware in the host, not a feature slice —
`Erp.Api/Security/ApiKeyMiddleware.cs`:

```csharp
public sealed class ApiKeyMiddleware(RequestDelegate next, IConfiguration config)
{
    private const string HeaderName = "X-Api-Key";
    // Read the expected key once at startup (middleware is built once).
    private readonly byte[] _expected = Encoding.UTF8.GetBytes(config["ApiKey"] ?? "");

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(HeaderName, out var provided) || !Matches(provided!))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;   // unknown client — stop here
            await context.Response.WriteAsJsonAsync(new { error = "Missing or invalid API key." });
            return;   // short-circuit: never call next() → no endpoint runs
        }

        await next(context);
    }

    // Same fixed-time comparison as the password check (§3): FixedTimeEquals returns false for a length
    // mismatch (no throw), so an attacker can't learn the key by timing our compares.
    private bool Matches(string provided) =>
        _expected.Length > 0 &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), _expected);
}
```

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
curl http://localhost:5xxx/sales/ping?name=world \
     -H "X-Api-Key: <your-api-key>" \
     -H "Authorization: Bearer eyJ..."
# no X-Api-Key at all → 401 before authentication ever runs, even on /auth/login
```

> ❓ **You might be wondering: "Isn't this just JWT again?"** No — and the login hole is the clean proof. JWT
> *cannot* guard `/auth/login`, because login is anonymous by necessity. The API key is the **only** gate you
> can put in front of the login door itself. More broadly: **JWT = which *user*; API key = which *app*.**
> Different question, different answer, and they stack.

> 🧭 **From WebForms.** This is the app-level key you might have checked in `Global.asax`'s `BeginRequest`
> before any page ran — or the `<clientKey>` a partner had to pass into your old SOAP / `[WebMethod]` service
> before you'd answer. Same idea, now expressed as one middleware at the very front of the pipeline.

> ℹ️ **Let the tooling through.** This gate is blunt — it will 401 the **Swagger UI** and any **health-check**
> endpoint too. Simplest fixes: register `UseMiddleware<ApiKeyMiddleware>()` *after* you map Swagger/health, or
> have the middleware wave through a small allow-list of paths (`/swagger`, `/health`) in Development.

### The honest caveat you must not skip

> ⚠️ **A browser frontend cannot keep an API key secret.** If your client is a browser SPA, the key ships in
> the JavaScript bundle and rides on every request — **anyone** can read it in the network tab or devtools in
> about ten seconds. So in a browser, an API key is **not** a wall against a determined attacker; it's a
> **coarse outer filter**. What it genuinely buys you even there is real but limited:
> - it keeps your login door (and the slow §3 hash) from facing the *raw, anonymous* internet;
> - it lets you tell clients apart — issue one key per app and you can **rate-limit or revoke a single client**
>   without touching the others.
>
> What it must **never** do is *replace* user auth. The API key is the outer, coarse gate; **JWT plus the §9
> checks are the real security.** Treat a leaked browser API key as an annoyance to rotate, not a breach.
>
> 🔑 **Where an API key *is* a true secret:** server-to-server callers, a **backend-for-frontend**, a
> mobile/desktop app's own backend, or a trusted partner integration — anywhere the key lives on a machine the
> public can't read. That's when it becomes a genuine security boundary, not just a filter.

> 🔑 **Scaling past one key.** One shared string is fine to learn with. For several clients, store a *set* of
> keys — and, exactly like refresh tokens (§10), store their **hashes**, not the raw values, so a config/DB leak
> yields nothing usable. Look each incoming key up by its SHA-256 hash; the middleware shape above doesn't
> change, only where `_expected` comes from. Rotate keys on a schedule, and immediately if one leaks.

---

> 👉 Next: [Phase 4 (Part 4 of 4) — Request Flow, Checkpoint & Common Mistakes](04d-jwt-wrap-up.md) — trace a full authenticated request end to end, verify it works, and review the pitfalls.
