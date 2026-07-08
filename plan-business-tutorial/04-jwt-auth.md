# Phase 4 — JWT Authentication & Authorization

> This is a **new topic** not covered in the earlier plans, so we go slowly and assume nothing. By the
> end you can log in, receive a token, refresh it safely, and protect endpoints — including "only a Manager
> may approve."

---

> ⏱️ **Time for this phase:** ~2.5–3 hr. This is a **new topic** — hashing, tokens, claims, middleware order,
> the `MapInboundClaims` gotcha, and refresh-token rotation with reuse detection (§9). Go slow; rushing auth is
> how subtle security bugs sneak in.

## 1. What we're building

- A `POST /auth/login` endpoint: give it a username + password, get back a **short-lived access token and a
  refresh token**.
- Token *validation* wired into ASP.NET Core so protected endpoints reject anyone without a valid token.
- **Refresh tokens** (§9): `POST /auth/refresh` trades a refresh token for a fresh access token, **rotating**
  it each time and **detecting replay** of a stolen one; plus `POST /auth/logout` and `POST /auth/logout-all`
  to revoke sessions.
- **Role-based authorization** so specific endpoints require specific roles (Manager to approve).
- An `ICurrentUser` abstraction so our handlers can ask "who is calling, and what's their role?" without
  depending on ASP.NET directly.

## 2. Why it's designed this way

### What even *is* a JWT?

A **JWT** (JSON Web Token) is a small, digitally-signed string that says "this is user *rep*, role
*SalesRep*, and this claim is signed by the server so it can't be forged." The client sends it on every
request in an `Authorization: Bearer <token>` header. The server checks the signature — if it's valid,
it trusts the claims inside *without hitting the database again*.

> 💡 **Analogy:** a JWT is like a **festival wristband**. At the gate (login) they check your ID once
> and give you a tamper-proof wristband. After that, every stage just glances at the wristband — they
> don't re-verify your ID. The signature is what makes it tamper-proof: you can't draw your own
> wristband because you don't have the festival's special ink (the signing key).

#### Why bother? The problem a token solves

The fastest way to *get* why tokens exist is to try living without them first.

**Attempt 1 — just send the password on every request.** Every click re-sends username + password. Three
things break:

- 🔓 Your raw password is on the wire over and over — every request is another chance to leak it.
- 🐌 The server has to run the slow password hash-check (§3) *every single time*. That slowness is a feature
  when you log in, a tax when it's on every request.
- 🗄️ The browser has to stash your actual password somewhere to replay it. Now the most sensitive secret you
  own is sitting in client storage. Yuck.

**Attempt 2 — log in once, carry a token.** You log in **one** time. The server does the slow password check
**once**, then hands back a small signed token that says *"this is user 42, role SalesRep."* After that, the
client sends the **token** — not the password — and the server just checks the signature: a fast bit of math,
no database, no re-hashing.

That's the entire purpose, in one line:

> 🔑 **A token lets the server re-establish *who you are* on every request — fast, and without re-checking
> your password or touching the database.**

> ❓ **You might be wondering: "Does the server remember me between requests?"** No — and that's the whole
> point. This API is **stateless**: it keeps *no* server-side session, no "who's logged in" table in memory,
> nothing to look up. Everything it needs to trust you rides *inside the token you just handed it*, and the
> server rebuilds your identity from scratch on every request. Two things fall out of that:
> - 📈 **It scales sideways for free.** Because no server instance holds session state, *any* instance can
>   serve *any* request. Put ten behind a load balancer and it just works — no shared session store to sync
>   (this is a big part of why we don't reach for something like Redis here).
> - ⚖️ **The tradeoff: you can't instantly "forget" someone.** Since the server isn't tracking sessions, a
>   valid token stays valid until it *expires* — there's no session to delete to log them out early. That's
>   exactly why access tokens are short-lived, and why §9's refresh tokens (which the server *does* track, by
>   hash) exist to give back the revoke button.

The catch is obvious: if the client is just waving a slip of paper that says "I'm an Admin," what stops
someone from writing their own? Nothing — *unless* the paper is tamper-proof. That's what the structure below
buys you.

#### What a token actually looks like (readable, but not forgeable)

A JWT is just **three chunks of text joined by dots** — `header.payload.signature`:

```
eyJhbGciOiJIUzI1NiJ9  .  eyJzdWIiOiI0MiIsInJvbGUiOiJTYWxlc1JlcCJ9  .  3Rk...signature...
└──── header ────┘        └──────────── payload (your claims) ─────────┘   └── signature ──┘
```

- **header** — says how it's signed (e.g. HMAC-SHA256).
- **payload** — the **claims**: the actual facts, like `sub` (user id) and `role`. Decode it and you'd read
  `{ "sub": "42", "role": "SalesRep" }`.
- **signature** — the header + payload run through the secret **signing key**. This is the tamper-proof seal.

Two things surprise people here, and both matter:

1. **A JWT is *not* encrypted — it's only signed.** Anyone can paste the payload into a base64 decoder and
   read your role and user id. So never put a secret (a password, a credit-card number) in a claim. The
   payload is public; treat it that way.
2. **You still can't *forge* one.** Change a single character of the payload — say `SalesRep` → `Admin` — and
   the signature no longer matches, because the attacker can't recompute it without the server's signing key.
   The server recomputes the signature on every request (§7) and rejects any token where it doesn't line up.
   That's the whole security model: *readable, but not changeable.*

Two words people confuse:
- **Authentication** = "*who are you?*" (login, proving identity). The wristband proves it.
- **Authorization** = "*are you allowed to do this?*" (does your role permit approving orders?).

### Why an `ICurrentUser` interface?

Our handlers live in **modules**, whose Domain must stay **free of ASP.NET** (keep-the-domain-pure, from
[beginner Phase 0](../plan-beginner-friendly/01-phase-0-orientation.md)). But handlers need to know the
caller — e.g. "record *who* created this order," "is the caller a Manager?" So the **shared kernel**
*declares* a small interface, and `Shared.Infrastructure` (which may touch ASP.NET) *implements* it:

```csharp
// Erp.Shared.Common/Security/ICurrentUser.cs   (an interface any module may depend on)
public interface ICurrentUser
{
    long UserId { get; }
    string Username { get; }
    string Role { get; }
    bool IsInRole(string role);
}
```

This is the same trick as `ISalesOrderRepository`: the code that *needs* something names it as an
interface; `Shared.Infrastructure` supplies *how*. Authentication is a cross-cutting **platform** concern,
so its interface lives in `Shared`, not in a business module.

## 3. Step 1 — Hashing passwords (never store plain text)

A password must never be stored as-is. We store a one-way **hash** — you can check a password against it,
but you can't reverse it back to the password. .NET has a good built-in tool for this,
`Rfc2898DeriveBytes` (PBKDF2). Put this in `Erp.Shared.Infrastructure/Security`:

`Erp.Shared.Infrastructure/Security/PasswordHasher.cs`:

```csharp
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string storedHash);
}

public sealed class PasswordHasher : IPasswordHasher
{
    // Iterations = the "work factor": how many times PBKDF2 re-hashes. Higher = slower to crack (and to
    // check). 600,000 is the current OWASP floor for PBKDF2-HMAC-SHA256 (as of 2025) — tune it up over time
    // as hardware gets faster. Because we store the iteration count *inside* each hash (see Hash below),
    // raising this later doesn't break existing passwords: old hashes still verify with their old count.
    private const int SaltSize = 16, KeySize = 32, Iterations = 600_000;

    public string Hash(string password)
    {
        // A random "salt" so two identical passwords produce different hashes.
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] key  = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public bool Verify(string password, string storedHash)
    {
        var parts = storedHash.Split('.', 3);
        var iterations = int.Parse(parts[0]);
        var salt = Convert.FromBase64String(parts[1]);
        var key  = Convert.FromBase64String(parts[2]);
        var attempt = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, key.Length);
        // Constant-time compare avoids leaking info via timing.
        return CryptographicOperations.FixedTimeEquals(attempt, key);
    }
}
```

> ❓ **You might be wondering: "What's a salt, and why random?"** If two users both pick `Password123`,
> a plain hash would be *identical* for both — so an attacker who cracks one cracks both, and can
> pre-compute hashes ("rainbow tables"). A random **salt** mixed in makes every hash unique even for the
> same password, defeating that. We store the salt alongside the hash (it's not secret; its job is just
> to be unique).

To generate the seed hashes from [Phase 3](03-database-schema.md), run `Hash("Password123!")` once and
paste the results into `003_seed.sql`.

## 4. Step 2 — Creating the access token (`IJwtTokenService`)

> 📦 **Packages & config for this phase — add these first.** JWT is where these are first used, so (per the
> "install only when needed" rule from [Phase 1](01-solution-setup.md)) this is where we add them:
> ```bash
> dotnet add src/Shared/Erp.Shared.Infrastructure package System.IdentityModel.Tokens.Jwt
> dotnet add src/Erp.Api package Microsoft.AspNetCore.Authentication.JwtBearer
> ```
> With Central Package Management on, each `dotnet add` also writes a `<PackageVersion>` line into
> `Directory.Packages.props` (known-good for .NET 10: `System.IdentityModel.Tokens.Jwt` `8.19.1`,
> `Microsoft.AspNetCore.Authentication.JwtBearer` `10.0.9`).
>
> Now add the **`Jwt` settings** these read. In `appsettings.json` (no secrets committed):
> ```json
> {
>   "Jwt": {
>     "Issuer": "Erp.Api",
>     "Audience": "Erp.Client",
>     "SigningKey": "",
>     "AccessTokenMinutes": 15,   // short: a stolen access token dies fast (§9's refresh tokens make this practical)
>     "RefreshSlidingDays": 7,    // inactivity timeout — resets on each use
>     "RefreshAbsoluteDays": 30   // hard cap — an active session still re-logs-in eventually
>   }
> }
> ```
> And in `appsettings.Development.json` (local dev only — the real key is never committed):
> ```json
> { "Jwt": { "SigningKey": "dev-only-super-secret-key-change-me-32chars-min" } }
> ```
> 🔐 The `SigningKey` is a secret: **user-secrets** in dev
> (`dotnet user-secrets set "Jwt:SigningKey" "..."`), env vars / a vault in prod. Committing one is a real
> incident. Minimum 32 characters for the signing algorithm.

This mints the **access token** — the short-lived one (15 minutes, from the `Jwt:AccessTokenMinutes` setting
above) that the client sends on every request. It's deliberately brief because a stolen access token then
expires fast; §9's refresh token is what keeps the user from re-typing a password every 15 minutes.
`Erp.Shared.Infrastructure/Security/JwtTokenService.cs`:

```csharp
public interface IJwtTokenService
{
    string CreateAccessToken(long userId, string username, string role);
}

public sealed class JwtTokenService(IConfiguration config) : IJwtTokenService
{
    public string CreateAccessToken(long userId, string username, string role)
    {
        var jwt = config.GetSection("Jwt");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["SigningKey"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // Claims = the facts baked into the token.
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),   // subject = user id
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role),                            // ← powers role authorization
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: jwt["Issuer"],
            audience: jwt["Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(int.Parse(jwt["AccessTokenMinutes"]!)),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

The key line for authorization is `new Claim(ClaimTypes.Role, role)` — it stamps the user's role into
the wristband. Later, `[Authorize(Roles = "SalesManager")]` reads exactly this.

> ❓ **You might be wondering: "Is one `role` claim all a token can carry?"** Not at all — a claim is just a
> key/value *fact*, and you can bake in as many as you need. A single `role` string is the simplest useful
> model (and all this course needs), but larger systems often carry richer authorization data right in the
> token. For example:
>
> ```json
> {
>   "sub": "42",
>   "name": "rep",
>   "role": ["SalesRep", "SalesManager"],                                 // more than one role at once
>   "permissions": ["orders:read", "orders:approve", "customers:read"],   // fine-grained, not just a role
>   "tenant": "acme-corp",                                                // which org this user belongs to
>   "region": "APAC"                                                      // scope a user to a slice of data
> }
> ```
>
> With **permissions** in the token, an endpoint can demand a *capability* (`orders:approve`) instead of a
> role — so you can change which roles hold which powers in one place, without editing every endpoint's role
> list. A `tenant` or `region` claim lets a handler scope queries to the caller's slice of the data. The
> tradeoff: every extra claim makes the token **bigger** (it rides on *every* request) and **staler** (claims
> only refresh when a new token is minted — §9 — so a permission revoked at 10:00 lingers until the current
> token expires).
>
> 🔑 **We deliberately keep it to one `role` claim** in this course because our rules are simple and it keeps
> the authorization story easy to follow. Reach for permission/multi-role/tenant claims when your real rules
> outgrow a single role — the mechanism is identical, just more claims in the same `claims` array above.

> 🧭 **From WebForms — the "module / view / button" permission grid.** In a classic WebForms ERP your
> permissions probably lived in a grid: *per module, which pages may this user open, and which buttons may they
> click?* That maps straight onto claims too — here it is keyed by our actual modules (`SALES`, `INVENTORY`):
>
> ```json
> {
>   "sub": "42",
>   "name": "rep",
>   "role": "SalesRep",
>   "access": [
>     {
>       "moduleId": "SALES",
>       "views":   ["orders", "customers"],          // screens this user may open
>       "buttons": ["order:create", "order:cancel"]  // actions this user may click
>     },
>     {
>       "moduleId": "INVENTORY",
>       "views":   ["stock"],
>       "buttons": []                                 // read-only: may view, no actions
>     }
>   ]
> }
> ```
>
> A front-end client reads `views` to decide which menu items to render and `buttons` to show or grey out each
> action — the same muscle memory as toggling `Panel.Visible` or `btnApprove.Enabled` in code-behind, except
> the truth now travels in the token instead of a server-side `Session["Permissions"]`.
>
> 🔑 **But hiding a button is convenience, not security.** A front end can be bypassed — anyone can call
> `POST /sales-orders/{id}/approve` directly with a valid token. So the `buttons`/`views` claims only tidy the
> *UI*; the **server still enforces the real check** on the endpoint or in the handler (§10). Never let a
> hidden button be your only guard.
>
> ⚠️ **And watch the size — this is where the token tradeoff bites hardest.** A full module/view/button matrix
> can be *hundreds* of entries, and it rides on **every** request. The usual fix: keep only a compact **role or
> permission summary** in the token (what the server authorizes against, §10), and load the detailed UI matrix
> **once** after login from an endpoint like `GET /me/permissions`. Rule of thumb: put in the token what the
> *server* needs to authorize each call; fetch separately what the *UI* needs to render. Don't turn the
> wristband into a filing cabinet.

> ℹ️ **A note on the API surface.** `JwtSecurityToken`/`JwtSecurityTokenHandler` (from
> `System.IdentityModel.Tokens.Jwt`) is the long-standing, fully-supported way to create tokens and is
> perfectly fine here. The *newer* recommended type is `JsonWebTokenHandler` (from
> `Microsoft.IdentityModel.JsonWebTokens`), which is faster and the direction Microsoft is steering. If you
> start a fresh project, prefer it; we use the classic handler because it reads more simply for a first
> encounter. Either way, the `MapInboundClaims = false` fix below still applies.

## 5. Step 3 — The Login use case (a vertical slice)

Login is a **query-ish command** — it reads a user and returns a token. We model it as a command since it
"produces" a credential. Because authentication is a host/platform concern (not a business module), the
Login slice lives with the host: `Erp.Api/Auth/Login/` — two files, same convention as every feature
(`LoginHandler.cs` holds the command + result + validator + handler; `LoginEndpoint.cs` holds the Carter
endpoint). `LoginHandler.cs`:

```csharp
public sealed record LoginCommand(string Username, string Password) : ICommand<LoginResult>;

// Returned by BOTH login and refresh (§9): a fresh access token + the refresh token to store for next time.
public sealed record LoginResult(string AccessToken, string RefreshToken, string Username, string Role);

public sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.Username).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class LoginHandler(
    IUserRepository users,
    IPasswordHasher hasher,
    IJwtTokenService jwt,
    IRefreshTokenService refreshTokens,     // generates + hashes the random refresh token (§9)
    IRefreshTokenRepository store,          // persists it
    IUnitOfWork uow,
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
        await using var tx = await uow.BeginAsync(ct);
        await store.AddAsync(refreshHash, user.Id, familyId: Guid.NewGuid(),
            expiresUtc: refreshTokens.SlidingExpiry(now), absoluteExpiresUtc: refreshTokens.AbsoluteExpiry(now),
            tx.Transaction, ct);
        await tx.CommitAsync(ct);

        return new LoginResult(access, rawRefresh, user.Username, user.Role);   // client keeps the RAW refresh token
    }
}
```

> ℹ️ **What are `IUserRepository` and `AppUser`?** Just like the Sales repositories, the Login handler
> declares the small port it needs and lets Infrastructure implement it. `IUserRepository` has two methods —
> `Task<AppUser?> GetByUsernameAsync(string username, CancellationToken ct);` (login) and
> `Task<AppUser?> GetByIdAsync(long id, CancellationToken ct);` (used by refresh in §9 to reload the user) —
> implemented in `Shared.Infrastructure` with Dapper calling `"[identity].usp_AppUser_GetByUsername"` /
> `"[identity].usp_AppUser_GetById"` (bracketed — reserved keyword, [Phase 3](03-database-schema.md)).
> `AppUser` is a plain read shape carrying `Id, Username, PasswordHash, Role, IsActive` — deliberately *not* a
> rich domain aggregate, because authentication is a platform concern, not a Sales business rule. Register it
> in the composition root ([Phase 11 §7](11-cross-cutting.md)).

> ❓ **You might be wondering: "Why one vague error for both 'no such user' and 'wrong password'?"**
> Security. If you said "no such user" vs "wrong password," an attacker could probe which usernames
> exist. A single generic message ("Invalid username or password") leaks nothing. Small detail, real
> best practice.

## 6. Step 4 — The endpoint (a Carter module)

`Erp.Api/Auth/Login/LoginEndpoint.cs` — a Carter module, auto-discovered by `app.MapCarter()`:

```csharp
public sealed class LoginEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/auth/login", async (LoginCommand cmd, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(cmd, ct);
            return Results.Ok(result);
        })
        .AllowAnonymous();   // login must NOT require a token (chicken-and-egg)
}
```

> 🧭 **From WebForms:** this is your `Login.aspx` code-behind's `Login_Click` — but the credential check
> and token creation moved into a handler, and the endpoint is a two-line adapter. A `[WebMethod]` you used
> to call from jQuery is now this Carter `MapPost`.

## 7. Step 5 — Wiring token *validation* into the app

Creating tokens (login) is separate from *validating* them on every other request. In `Program.cs`:

```csharp
var jwt = builder.Configuration.GetSection("Jwt");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // CRITICAL: keep JWT claim names as-is. By default the handler *renames* inbound claims
        // (e.g. "sub" → ClaimTypes.NameIdentifier), which would make our CurrentUser.UserId lookup of
        // "sub" return null and blow up at runtime. Turning mapping off keeps "sub" as "sub". (See note.)
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,   ValidIssuer = jwt["Issuer"],
            ValidateAudience = true, ValidAudience = jwt["Audience"],
            ValidateLifetime = true,                     // reject expired tokens
            ValidateIssuerSigningKey = true,             // reject forged tokens
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["SigningKey"]!)),
            // We emit roles as ClaimTypes.Role and the name as ClaimTypes.Name (see JwtTokenService),
            // so tell the validator to read those types back for IsInRole / RequireRole to work.
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.Name
        };
    });

builder.Services.AddAuthorization();
```

> ⚠️ **The single most common JWT gotcha — don't skip this.** ASP.NET's JWT handler ships with a legacy
> *inbound claim map* that silently rewrites short JWT claim names to long WS-* URIs — most notably `sub`
> becomes `ClaimTypes.NameIdentifier`. So if you *write* `sub` (as `JwtTokenService` does) but later *read*
> `sub` (as `CurrentUser.UserId` does), the read returns **null** and `long.Parse(null)` throws on the
> first authenticated request — a baffling bug with a clean token. Setting `options.MapInboundClaims =
> false` keeps claim names exactly as issued, so `sub` reads back as `sub`. (An alternative is to read
> `ClaimTypes.NameIdentifier` instead — but turning mapping off is clearer and keeps token and reader in
> agreement.) We pair it with explicit `RoleClaimType`/`NameClaimType` so role checks still work.

And the middleware, **in this exact order** (order matters a lot):

```csharp
app.UseAuthentication();   // 1) read & validate the token → figure out WHO you are
app.UseAuthorization();    // 2) check WHETHER you're allowed
```

> ❓ **You might be wondering: "Why does the order matter?"** Authorization needs to know who you are
> before it can decide if you're allowed. `UseAuthentication` establishes identity; `UseAuthorization`
> acts on it. Swap them and authorization runs before anyone's identified — nothing works. Middleware is
> a pipeline; sequence is meaning.

## 8. Step 6 — Implementing `ICurrentUser`

Now that ASP.NET has validated the token, the claims are available on `HttpContext.User`. Infrastructure
reads them and fulfils the Application's `ICurrentUser` promise:

```csharp
// Erp.Shared.Infrastructure/Security/CurrentUser.cs
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal User => accessor.HttpContext?.User
        ?? throw new InvalidOperationException("No HTTP context.");

    public long UserId => long.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
    public string Username => User.FindFirstValue(ClaimTypes.Name)!;
    public string Role => User.FindFirstValue(ClaimTypes.Role)!;
    public bool IsInRole(string role) => User.IsInRole(role);
}
```

Register it: `services.AddHttpContextAccessor(); services.AddScoped<ICurrentUser, CurrentUser>();`

Now any handler can inject `ICurrentUser` and know the caller — used in [Phase 6](06-create-order.md) to
stamp `CreatedByUserId`, and in [Phase 8](08-approval-cancellation.md) to check category role rules.

## 9. Step 7 — Refresh tokens: staying logged in without staying exposed

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
    IUnitOfWork uow,
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
            await using var breach = await uow.BeginAsync(ct);
            await store.RevokeFamilyAsync(token.FamilyId, breach.Transaction, ct);
            await breach.CommitAsync(ct);
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
        await using var tx = await uow.BeginAsync(ct);
        await store.AddAsync(hashNew, user.Id, token.FamilyId,
            refreshTokens.SlidingExpiry(now), token.AbsoluteExpiresUtc, tx.Transaction, ct);
        await store.RevokeAsync(presentedHash, replacedByHash: hashNew, tx.Transaction, ct);   // old one spent
        await tx.CommitAsync(ct);

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
public sealed class LogoutHandler(IRefreshTokenService refreshTokens, IRefreshTokenRepository store, IUnitOfWork uow)
    : ICommandHandler<LogoutCommand, Unit>
{
    public async Task<Unit> Handle(LogoutCommand cmd, CancellationToken ct)
    {
        var token = await store.GetByHashAsync(refreshTokens.Hash(cmd.RefreshToken), ct);
        if (token is not null)   // unknown/already-gone token → nothing to do; logout is idempotent
        {
            await using var tx = await uow.BeginAsync(ct);
            await store.RevokeFamilyAsync(token.FamilyId, tx.Transaction, ct);   // this whole session out
            await tx.CommitAsync(ct);
        }
        return Unit.Value;
    }
}

// "Log me out everywhere" — needs a valid ACCESS token, so it acts on the authenticated caller.
public sealed record LogoutAllCommand : ICommand<Unit>;
public sealed class LogoutAllHandler(ICurrentUser currentUser, IRefreshTokenRepository store, IUnitOfWork uow)
    : ICommandHandler<LogoutAllCommand, Unit>
{
    public async Task<Unit> Handle(LogoutAllCommand cmd, CancellationToken ct)
    {
        await using var tx = await uow.BeginAsync(ct);
        await store.RevokeAllForUserAsync(currentUser.UserId, tx.Transaction, ct);
        await tx.CommitAsync(ct);
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

## 10. Two ways to authorize (endpoint vs. domain)

Authorization happens at **two levels**, and putting each rule at the right one is most of the skill:

1. **Coarse, at the endpoint** — "you must be a Manager to even call approve." Declared on the endpoint:
   ```csharp
   app.MapPost("/sales-orders/{id}/approve", ...).RequireAuthorization(p => p.RequireRole("SalesManager"));
   ```
   This rejects unauthorized callers *before* any handler runs. Cheap and clear.

2. **Fine, in the domain/handler** — "this *item category* requires a Manager." That's business logic
   tied to data (which category), so it lives deeper, checked with `ICurrentUser.IsInRole(...)` in the
   handler or domain (see [Phase 6](06-create-order.md)).

> 🔑 **Rule of thumb:** if the rule is "this endpoint is for role X, full stop" → put it on the endpoint.
> If the rule depends on the *data* ("this category needs a manager", "you can only cancel your own
> draft") → it's business logic, put it in the handler/domain. Don't force data-dependent rules into
> endpoint attributes; you'll contort them.

### See it work — two users, one endpoint

The rules above click once you watch a real request get **let in** or **turned away**. Our
[Phase 3](03-database-schema.md) seed gives us three users, each with exactly one role:

| Username | Password | Role |
|---|---|---|
| `rep` | `Password123!` | `SalesRep` |
| `manager` | `Password123!` | `SalesManager` |
| `admin` | `Password123!` | `Admin` |

Say the approve endpoint is gated **manager-only**:

```csharp
app.MapPost("/sales-orders/{id}/approve", ...)
   .RequireAuthorization(p => p.RequireRole("SalesManager"));
```

Log each user in (§6), take the access token you get back, and call approve with it. Here's who gets
through, and why:

| Caller | Sends a token? | `RequireRole("SalesManager")` check | Result |
|---|---|---|---|
| nobody (no `Authorization` header) | — | can't even run — there's no one to check | **401 Unauthorized** — "I don't know who you are" |
| `rep` (SalesRep) | ✅ valid | role is `SalesRep`, not `SalesManager` | **403 Forbidden** — "I know who you are; you're not allowed" |
| `manager` (SalesManager) | ✅ valid | role matches | **200 OK** — the handler runs |
| `admin` (Admin) | ✅ valid | role is `Admin`, not `SalesManager` | **403 Forbidden** — surprised? read on ↓ |

Two things beginners almost always trip on live in that table:

**401 and 403 are different answers to different questions.** A **401** means *authentication* failed —
no token, an expired one, or a forged one; the server can't establish who you are (§7 threw it out before
authorization ever ran). A **403** means authentication *succeeded* — the token is perfectly valid and the
server knows exactly who you are — but *authorization* said no: your role isn't on the list. "Not logged in"
and "logged in but not allowed" are **not** the same failure, and the status code is how the caller tells
them apart. ([Phase 11](11-cross-cutting.md) maps our exceptions to these codes.)

**Roles are a flat list, not a ladder — this is your "two managers, one gets in" case.** In plain English
an *Admin* obviously outranks a *SalesManager*, so why does `admin` get a 403? Because
`RequireRole("SalesManager")` means *literally* "carry the `SalesManager` role," full stop. ASP.NET does
**not** treat `Admin` as a superset of `SalesManager` — there is no built-in hierarchy, no "higher role wins."
If you want admins to approve too, you have to **say so explicitly**, by listing every role that should pass:

```csharp
.RequireAuthorization(p => p.RequireRole("SalesManager", "Admin"));   // hold EITHER role → you're in
```

`RequireRole` with several roles is an **OR**: hold *any one* of them and you clear the gate. That's exactly
why the real approve endpoint in [Phase 8](08-approval-cancellation.md) is written
`RequireRole("SalesManager", "Admin")` — so both roles pass — while a plain `SalesRep` still bounces off with
a 403.

> 🔑 There is no magic ranking of role names. If a role should have access, its literal string must appear
> in the list. When you genuinely want a hierarchy ("anything a manager can do, an admin can too, without
> re-listing it everywhere"), you build that yourself — usually with permission claims (§4) or an
> authorization policy — you don't get it by hoping the framework orders `Admin` above `SalesManager`.

### Where to add *your own* user & role checks — the cheat-sheet

New checks are the first thing you'll want to add, so here's the full picture in one place. Every check you
could write about a user or a role has exactly **one** correct home. Putting it in the wrong place is the most
common way beginners tangle this code — so match your check to the row below before you write it.

| The check you want to add | Where it goes | Concretely |
|---|---|---|
| **Is the request well-formed?** (shape only: username not empty, password ≥ 8 chars) | **Validator** (FluentValidation), in the same `…Handler.cs` file | Add a rule to `LoginValidator`: `RuleFor(x => x.Password).MinimumLength(8);` |
| **Is this login allowed?** (account active? password correct? locked out?) | **Handler** | Extend the existing guard in `LoginHandler`: `if (user is null \|\| !user.IsActive \|\| !hasher.Verify(...)) throw …` |
| **"This whole endpoint is for role X, full stop."** | **Endpoint**, via `RequireAuthorization` | `app.MapPost("/sales-orders/{id}/approve", …).RequireAuthorization(p => p.RequireRole("SalesManager"));` |
| **A role rule that depends on the *data*** ("*this item category* needs a Manager", "you can only cancel *your own* draft") | **Handler / domain**, via `ICurrentUser` | Inject `ICurrentUser`, then `if (order.RequiresManager && !currentUser.IsInRole("SalesManager")) throw …` |

> ⚠️ **Never put a business rule in the validator.** A validator answers only *"is the request shaped
> right?"* — positive ids, non-empty fields, ranges. *"Is this user allowed?"* / *"is the password
> correct?"* is a **handler/domain** decision that usually needs the database, and a validator must stay
> unit-testable without one. Mixing the two is treated as a defect in this course — it's the exact skill
> §10 is teaching you to keep straight.

> ⚠️ **Role strings must be the literal values `SalesRep`, `SalesManager`, `Admin`.** Not `"Manager"`, not
> `"salesmanager"`, not `"sales_manager"`. A typo doesn't throw an error — the role check simply never
> matches, so the rule silently does nothing and the wrong people get through. Copy the strings exactly, or
> better, keep them as `const` values in one place and reference those.

## 11. How a full authenticated request flows

```
1. POST /auth/login {rep, Password123!}  → LoginHandler verifies hash → returns { accessToken, refreshToken }
2. Client stores both: the access token for requests, the refresh token for later.
3. POST /sales-orders   Authorization: Bearer <accessToken>
     → UseAuthentication validates signature+expiry → builds HttpContext.User with claims
     → UseAuthorization checks the endpoint's role requirement (here: SalesRep or Manager — ok)
     → endpoint calls sender.Send(command)
     → handler injects ICurrentUser → reads UserId to stamp CreatedByUserId
4. ...15 minutes pass; the access token expires → the next request gets 401.
5. POST /auth/refresh {refreshToken}  → RefreshHandler rotates it → returns a new { accessToken, refreshToken }
     → client swaps in the new pair and retries. No re-login.
```

## 11b. Checkpoint — log in, get a token, prove it protects ✅

Run the app and verify auth works end-to-end. First, log in as the seed user `rep` (password
`Password123!`):

```bash
curl -X POST http://localhost:5xxx/auth/login \
     -H "Content-Type: application/json" \
     -d '{"username":"rep","password":"Password123!"}'
# → { "accessToken": "eyJ...", "refreshToken": "..." }
```

Now prove the token actually *gates* access. Add `.RequireAuthorization()` to the `/sales/ping` endpoint
from Phase 2, then:

```bash
curl http://localhost:5xxx/sales/ping?name=world                       # → 401 Unauthorized (no token)
curl http://localhost:5xxx/sales/ping?name=world -H "Authorization: Bearer eyJ..."   # → 200 "pong: world"
```

Finally, trade the refresh token for a fresh pair:

```bash
curl -X POST http://localhost:5xxx/auth/refresh -H "Content-Type: application/json" \
     -d '{"refreshToken":"<the refresh token above>"}'                 # → a NEW accessToken + refreshToken
```

If a bad/missing token gives 401, a valid one gives 200, and refresh returns a *different* token pair,
**Phase 4 is done and verifiable.** From Phase 6 on, every sales-order endpoint sits behind exactly this
gate. (You can also paste the access token into Swagger's **Authorize** box and call endpoints from the
browser.)

## 12. Common mistakes & best practices

- ❌ **Storing plain passwords / weak hashing (MD5, SHA1 alone).** ✅ Salted PBKDF2 (or bcrypt/Argon2).
- ❌ **Committing the signing key.** ✅ user-secrets/env vars; the key ≥ 32 chars.
- ❌ **Different login errors for "no user" vs "bad password."** ✅ One generic message.
- ❌ **Forgetting `.AllowAnonymous()` on login**, then wondering why you can't log in (you'd need a
  token to get a token). ✅ Login is always anonymous.
- ❌ **Middleware in the wrong order** (`UseAuthorization` before `UseAuthentication`). ✅ Authenticate,
  then authorize.
- ❌ **Making handlers depend on `HttpContext`.** ✅ Depend on `ICurrentUser`; keep the Application layer
  framework-free and unit-testable (you can fake `ICurrentUser` in tests).
- ❌ **Storing the raw refresh token in the database.** ✅ Store its hash; a DB leak then yields no usable
  tokens (§9).
- ❌ **Refreshing without rotating** (handing back the same refresh token). ✅ Rotate every time — that's
  what makes reuse detectable.
- ❌ **A long-lived access token "to save round-trips."** ✅ Keep the access token short (15 min) and lean on
  the refresh token; a stolen short-lived token expires before it's useful.
- ✅ **Best practice: reload the user on refresh.** It's your chance to honor a role change or a deactivation
  since login, instead of trusting stale claims for days.

---

## ✅ Check Yourself

1. In one sentence each: what's authentication vs. authorization?
2. Why does the Application layer talk to `ICurrentUser` instead of `HttpContext`?
3. Why must `UseAuthentication` come before `UseAuthorization`?
4. Give an authorization rule that belongs on the *endpoint*, and one that belongs in the *handler* —
   and say why.
5. Why does rotating the refresh token on every use make a *stolen* one detectable — and what does the
   server do the moment it detects reuse?
6. Why store only the *hash* of a refresh token, yet a plain SHA-256 is fine here where a password needs
   PBKDF2?

> 👉 Next: [Phase 5 — The SalesOrder domain model and its business rules](05-domain-model.md).
