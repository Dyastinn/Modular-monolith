# Phase 4 (Part 2 of 4) — Login, Validation & Authorization

> **Phase 4 is split into four parts** — do them in order:
> 1. [Passwords & access tokens](04-jwt-auth.md)
> 2. **Login, validation & authorization** ← you are here
> 3. [Refresh tokens & the API-key gate](04c-jwt-refresh-apikey.md)
> 4. [Request flow, checkpoint & common mistakes](04d-jwt-wrap-up.md)

> Builds on [Part 1](04-jwt-auth.md), which gave us the password hasher (§3) and the access-token service (§4). Here we assemble them into a login slice, wire token *validation* into ASP.NET Core, expose the caller through `ICurrentUser`, and use roles to decide who may do what.

---

## 5. Step 3 — The Login use case (a vertical slice)

Now that we can **hash passwords** (§3) and **create JWTs** (§4), we have everything needed to build a real
login feature.

A login has one simple responsibility:

1. Find the user.
2. Verify the account is allowed to log in.
3. Verify the supplied password.
4. Issue a JWT access token.
5. Return that token to the client.

We'll implement that entire flow as a **vertical slice**.

### Why is Login a command?

At first glance, login looks like a query because it reads a user from the database.

However, the real purpose of login isn't to retrieve user information—it's to **issue a new authentication
credential** (the JWT). Since it creates something new rather than simply returning existing data, we model
it as a **command**.

### Why does Login live in `Erp.Api`?

Authentication is a **host/platform concern**, not a business concern.

Business modules such as Sales, Purchasing, Inventory, or Accounting contain business rules. Login doesn't
belong to any of those modules—it simply identifies **who** is calling the API before any business logic is
allowed to run.

For that reason, the Login slice lives with the host application:

```text
Erp.Api/
└── Auth/
    └── Login/
        ├── LoginHandler.cs
        └── LoginEndpoint.cs
```

Just like every other feature:

- `LoginHandler.cs` contains the command, result, validator, and handler.
- `LoginEndpoint.cs` exposes the HTTP endpoint using Carter.

---

## First pass: access token only

We'll build login in **two passes**.

In this section, the handler issues **only an access token**.

That's already enough for a complete, working authentication system. A client can log in, receive a JWT, and
authenticate future API requests.

Later, in **[Part 3, §10](04c-jwt-refresh-apikey.md)**, we'll extend this exact feature to also issue a
**refresh token**.

Keeping refresh tokens out of this section avoids introducing several new concepts all at once:

- refresh token generation
- refresh token storage
- refresh token expiry
- refresh token rotation

That lets us focus on understanding the login flow first.

Before we build the handler, let's look at the services it depends on.

| Dependency | Responsibility |
|------------|----------------|
| `IUserRepository` | Loads users from the database |
| `IPasswordHasher` | Verifies passwords |
| `IJwtTokenService` | Creates signed JWTs |

`IPasswordHasher` and `IJwtTokenService` were built in the previous sections.

The only missing piece is `IUserRepository`.

---

## Building `IUserRepository`

The handler needs a way to load users from the database, but it shouldn't know **how** the data is stored.

Instead, it depends on an abstraction:

```text
LoginHandler
      │
      ▼
IUserRepository
      │
      ▼
UserRepository
      │
      ▼
SQL Server
```

This follows the Dependency Inversion Principle.

The handler depends only on the interface, while Infrastructure provides the implementation.

### The read model

Authentication isn't enforcing business rules.

It simply needs enough information to determine whether someone can log in.

For that reason, we don't build a rich domain aggregate here. A simple read model is enough.

`Erp.Shared.Common/Security/IUserRepository.cs`

```csharp
public sealed record AppUser(
    long Id,
    string Username,
    string PasswordHash,
    string Role,
    bool IsActive);

public interface IUserRepository
{
    Task<AppUser?> GetByUsernameAsync(string username, CancellationToken ct);
    Task<AppUser?> GetByIdAsync(long id, CancellationToken ct);
}
```

Notice how `AppUser` contains only the fields authentication needs:

- Id
- Username
- PasswordHash
- Role
- IsActive

Nothing more.

The repository exposes two read methods.

| Method | Used by |
|---------|---------|
| `GetByUsernameAsync()` | Login (this section) |
| `GetByIdAsync()` | Refresh tokens (§10) |

Although only one method is used today, adding the second one now avoids changing the interface later when we
build refresh tokens.

---

## Implementing `UserRepository`

Both repository methods are **pure reads**.

As discussed in **Phase 3b**, read-only operations don't need transactions, so they create their own database
connection directly.

`Erp.Shared.Infrastructure/Security/UserRepository.cs`

```csharp
public sealed class UserRepository(IDbConnectionFactory factory) : IUserRepository
{
    public async Task<AppUser?> GetByUsernameAsync(string username, CancellationToken ct)
    {
        using var conn = factory.Create();

        return await conn.QuerySingleOrDefaultAsync<AppUser>(
            new CommandDefinition(
                "[identity].usp_AppUser_GetByUsername",
                new { Username = username },
                commandType: CommandType.StoredProcedure,
                cancellationToken: ct));
    }

    public async Task<AppUser?> GetByIdAsync(long id, CancellationToken ct)
    {
        using var conn = factory.Create();

        return await conn.QuerySingleOrDefaultAsync<AppUser>(
            new CommandDefinition(
                "[identity].usp_AppUser_GetById",
                new { Id = id },
                commandType: CommandType.StoredProcedure,
                cancellationToken: ct));
    }
}
```

These stored procedures were created earlier in **Phase 3**.

- `[identity].usp_AppUser_GetByUsername`
- `[identity].usp_AppUser_GetById`

Notice the brackets around `identity`. They're required because `IDENTITY` is a reserved SQL keyword.

Finally, register the repository in the composition root:

```csharp
services.AddScoped<IUserRepository, UserRepository>();
```

With that in place, every dependency required by the login handler now exists.

---

## Building the Login handler

The login slice consists of four pieces:

- the command
- the result
- the validator
- the handler

`LoginHandler.cs`

```csharp
public sealed record LoginCommand(string Username, string Password)
    : ICommand<LoginResult>;

// First pass: just the access token.
// §10 extends this record to also include a refresh token.
public sealed record LoginResult(
    string AccessToken,
    string Username,
    string Role);

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
    IJwtTokenService jwt)
    : ICommandHandler<LoginCommand, LoginResult>
{
    public async Task<LoginResult> Handle(LoginCommand cmd, CancellationToken ct)
    {
        var user = await users.GetByUsernameAsync(cmd.Username, ct);

        if (user is null ||
            !user.IsActive ||
            !hasher.Verify(cmd.Password, user.PasswordHash))
        {
            throw new AuthenticationException("Invalid username or password.");
        }

        var access = jwt.CreateAccessToken(
            user.Id,
            user.Username,
            user.Role);

        return new LoginResult(
            access,
            user.Username,
            user.Role);
    }
}
```

---

## Walking through the handler

Although the handler performs authentication, it actually does very little itself.

Each dependency is responsible for one piece of work.

### Step A — Load the user

```csharp
var user = await users.GetByUsernameAsync(cmd.Username, ct);
```

The repository looks up the user by username.

If no matching user exists, it returns `null`.

---

### Step B — Validate the login

```csharp
if (user is null ||
    !user.IsActive ||
    !hasher.Verify(cmd.Password, user.PasswordHash))
{
    throw new AuthenticationException("Invalid username or password.");
}
```

Three checks happen here.

- Does the user exist?
- Is the account active?
- Does the supplied password match the stored hash?

If any answer is "no", authentication stops immediately.

Notice that we never compare plain-text passwords.

`IPasswordHasher.Verify()` hashes the supplied password and compares it against the stored hash.

---

### Why use one generic error?

You might expect different messages like:

- User does not exist
- Wrong password

Returning different errors would reveal which usernames exist in the system.

Instead, every authentication failure returns exactly the same message:

> Invalid username or password.

Whether the username is wrong, the password is wrong, or the account is inactive, the client receives the
same response.

This prevents attackers from discovering valid usernames.

---

### Step C — Create the JWT

```csharp
var access = jwt.CreateAccessToken(
    user.Id,
    user.Username,
    user.Role);
```

Once authentication succeeds, the handler asks `IJwtTokenService` to generate a signed JWT.

Notice that the handler doesn't know anything about JWT internals.

It doesn't know how the claims are created.

It doesn't know how the token is signed.

It simply delegates that responsibility to `IJwtTokenService`.

---

### Step D — Return the result

```csharp
return new LoginResult(
    access,
    user.Username,
    user.Role);
```

Finally, the handler packages the response that the client needs.

For now, that consists of:

- the access token
- the username
- the user's role

In **[Part 3, §10](04c-jwt-refresh-apikey.md)**, we'll extend this response to also include a refresh token.

At this point, the login feature is complete.

A client can:

1. Submit a username and password.
2. Have the password verified securely.
3. Receive a signed JWT.
4. Use that JWT to authenticate future API requests.

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

Creating tokens (login) is separate from *validating* them on every other request.

Just like `JwtTokenService`, we don't care **where** the JWT settings come from. We simply ask
`builder.Configuration` for them. ASP.NET Core's configuration system figures out where each value
comes from (for example `appsettings.json`, User Secrets, environment variables, or a secret
manager).

In `Program.cs`:

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
            ValidateIssuer = true,
            ValidIssuer = jwt["Issuer"],

            ValidateAudience = true,
            ValidAudience = jwt["Audience"],

            ValidateLifetime = true,         // reject expired tokens
            ValidateIssuerSigningKey = true, // reject forged tokens

            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwt["SigningKey"]!)),

            // We emit roles as ClaimTypes.Role and the name as ClaimTypes.Name (see JwtTokenService),
            // so tell the validator to read those types back for IsInRole / RequireRole to work.
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.Name
        };
    });

builder.Services.AddAuthorization();
```

### 🧭 Where does `builder.Configuration` actually get its values?

You may have noticed that we keep writing code like this:

```csharp
var jwt = builder.Configuration.GetSection("Jwt");
```

But nowhere do we tell it:

- "Read `appsettings.json`."
- "Read `appsettings.Development.json`."
- "Read User Secrets."
- "Read an environment variable."

So how does it know?

The answer is that **`builder.Configuration` isn't a single file**. It's a **configuration system**
made up of multiple **configuration providers**.

Think of it as one big dictionary that ASP.NET Core assembles when the application starts.

```
                    ASP.NET Core starts
                             │
                             ▼
                 Builds IConfiguration
                             │
        ┌────────────────────┼─────────────────────┐
        │                    │                     │
        ▼                    ▼                     ▼
 appsettings.json   appsettings.{Environment}.json User Secrets*
        │                    │                     │
        └────────────────────┼─────────────────────┘
                             │
                             ▼
                 Environment Variables
                             │
                             ▼
                 Command-line Arguments
                             │
                             ▼
                builder.Configuration

* User Secrets are only loaded in Development.
```

Each provider contributes configuration values.

If two providers contain the **same key**, the provider loaded **later** overrides the earlier one.

The default priority is roughly:

```
Lowest priority

appsettings.json
        │
        ▼
appsettings.{Environment}.json
        │
        ▼
User Secrets (Development only)
        │
        ▼
Environment Variables
        │
        ▼
Command-line Arguments

Highest priority
```

This means the configuration system always keeps the **last value it sees**.

---

### Example 1 — Running locally

Suppose you're developing on your own machine.

Your environment is:

```text
ASPNETCORE_ENVIRONMENT=Development
```

Your files contain:

**appsettings.json**

```json
{
  "Jwt": {
    "Issuer": "Erp.Api",
    "Audience": "Erp.Client",
    "SigningKey": "",
    "AccessTokenMinutes": 15
  }
}
```

**appsettings.Development.json**

```json
{
  "Jwt": {
    "SigningKey": "temporary-dev-key"
  }
}
```

or perhaps you've stored the key in User Secrets instead:

```text
Jwt:SigningKey = my-local-secret
```

When the application starts, the configuration pipeline looks like this:

```
appsettings.json
        │
        ▼
appsettings.Development.json
        │
        ▼
User Secrets
        │
        ▼
builder.Configuration
```

Now your code executes:

```csharp
var jwt = builder.Configuration.GetSection("Jwt");
```

The resulting values are effectively:

```
Issuer = Erp.Api
Audience = Erp.Client
AccessTokenMinutes = 15
SigningKey = my-local-secret
```

Notice that `SigningKey` came from the **highest-priority provider**.

`JwtTokenService` has no idea where it came from.

---

### Example 2 — Running in production

Now imagine you deploy the exact same application to a production server.

There is:

- no `appsettings.Development.json`
- no User Secrets

Instead, the server is configured with:

```text
Jwt__SigningKey=Vq4Hd1bXr8n2Lm5...
```

The configuration pipeline now looks like this:

```
appsettings.json
        │
        ▼
Environment Variables
        │
        ▼
builder.Configuration
```

When this line executes:

```csharp
var jwt = builder.Configuration.GetSection("Jwt");
```

ASP.NET Core returns:

```
Issuer = Erp.Api
Audience = Erp.Client
AccessTokenMinutes = 15
SigningKey = Vq4Hd1bXr8n2Lm5...
```

Again, the JWT code never changes.

The only thing that changed was **where the server stored the secret**.

---

### Why doesn't the code check whether it's Development or Production?

Notice that nowhere do we write:

```csharp
if (builder.Environment.IsDevelopment())
{
    // Read appsettings.Development.json
}
else
{
    // Read environment variables
}
```

That logic already exists inside ASP.NET Core's configuration system.

Instead, every part of the application simply asks for the configuration it needs:

```csharp
config["Jwt:SigningKey"]
```

or

```csharp
config["Jwt:Issuer"]
```

The configuration system returns the correct value for the current environment.

This is why the same application can run:

- on your laptop
- on IIS
- inside Docker
- inside Kubernetes
- on Azure
- on AWS

without changing a single line of JWT code.

The application depends only on **`IConfiguration`**, not on where the values are stored.

```
                    JwtTokenService
                           │
                           ▼
                  IConfiguration
                           │
          "Give me Jwt:SigningKey"
                           │
        ┌──────────────────┼──────────────────┐
        │                  │                  │
        ▼                  ▼                  ▼
 appsettings.json   User Secrets   Environment Variables
                                            │
                                            ▼
                                  Secret Manager (optional)
```

This separation is intentional. It keeps infrastructure concerns (where configuration is stored)
separate from application code (how the configuration is used).

### 🔐 Where the signing key belongs — and how to set it in each environment

Of all the `Jwt` values, the `SigningKey` is the one real **secret**: anyone who knows it can mint tokens your
API will trust, so it must **never** be committed to source control. The pipeline above explains *how* a value
is resolved; here is *where to actually put the key* per environment:

| Environment | Where the signing key belongs |
|------------|-------------------------------|
| Local development | **User Secrets** (recommended) or `appsettings.Development.json` for small tutorials |
| Test / Staging | Environment variable or your organization's secret manager |
| Production | Environment variable or a dedicated secret manager |

During development, the recommended approach is **User Secrets**, which stores the value **outside your
project** so it never appears in Git:

```bash
dotnet user-secrets init
dotnet user-secrets set "Jwt:SigningKey" "your-random-secret"
```

For production, **environment variables** are the simplest common solution. The configuration key `Jwt:SigningKey`
maps to the environment variable:

```text
Jwt__SigningKey
```

Notice the **double underscore (`__`)** — in environment variables, `__` maps to the `:` used in config keys.

```powershell
$env:Jwt__SigningKey="your-random-secret"     # Windows (PowerShell), current session
setx Jwt__SigningKey "your-random-secret"      # Windows, persist across reboots
```

```bash
export Jwt__SigningKey="your-random-secret"    # Linux/macOS (add to ~/.bashrc to persist)
```

On a cloud platform you normally set it through the hosting service instead of by hand — **Azure App Service**
→ Application Settings, **Docker** → `-e Jwt__SigningKey=...`, **Kubernetes** → Secrets, **IIS** → Environment
Variables. Many organizations go further and use a **secret manager** (Azure Key Vault, AWS Secrets Manager,
Google Secret Manager, HashiCorp Vault) for storage, access control, auditing, and automatic rotation.

The nice part: **your code never changes.** Wherever the key comes from, ASP.NET Core exposes it through the
same `config["Jwt:SigningKey"]` — which is exactly why the `IssuerSigningKey` line above just reads
`jwt["SigningKey"]` and doesn't care about the source.

> 🔑 **How large should the signing key be?** Generate at least **32 random bytes (256 bits)** with a
> cryptographically secure generator — e.g. `openssl rand -base64 32` — suitable for HS256 signing.
>
> ⚠️ **If the signing key is ever exposed** (committed to GitHub, accidentally shared), generate a brand-new
> key and replace the old one — **rotating** the key. After rotation, every JWT signed with the old key
> immediately becomes invalid, so users must authenticate again.

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

## 9. Two ways to authorize (endpoint vs. domain)

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

### The other case — two *managers*, one order

The endpoint gate above is **coarse**: all it can see is your *role*. So it can wave through both `manager`
and `admin` for approve, but it fundamentally **cannot** say *"Manager A may approve this order, Manager B
may not"* — both hold the identical `SalesManager` role, so to the endpoint they're indistinguishable. Any
rule that separates two callers who share the **same role** must depend on the **data**, which means it
lives one level deeper — in the handler/domain (level 2 above).

Classic example: **segregation of duties — a manager may not approve an order they created themselves** (the
person who *raises* a spend shouldn't also *sign it off*). Picture order #50, created by `manager` (call them
Manager B). Now two managers try to approve it:

| Caller (both `SalesManager`) | Clears the endpoint role gate? | Data check: is the caller the order's creator? | Result |
|---|---|---|---|
| Manager A (did **not** create #50) | ✅ yes — has the role | no — creator was someone else | **approves** — 200 OK |
| Manager B (created #50) | ✅ yes — the *same* role | **yes** — caller *is* the creator | **blocked** — 403 Forbidden |

Both cleared the endpoint (identical role) — the only thing that separates them is *who created this
specific order*, which the endpoint attribute can't know. So the check goes in the **handler**, using
`ICurrentUser` (who's calling) against the order's own data (`CreatedBy`, stamped at create in
[Phase 6](06-create-order.md)):

```csharp
// In ApproveSalesOrderHandler (Phase 8), right after loading the order — a DATA-dependent rule,
// so it reads ICurrentUser and the order itself. No endpoint attribute could express this.
if (order.CreatedBy.Value == currentUser.UserId)
    throw new ForbiddenException("A manager cannot approve an order they created themselves.");
```

`ForbiddenException` maps to **403** in [Phase 11](11-cross-cutting.md) — the same "logged in, but not
allowed" answer as the role gate, just decided by the data instead of the role.

> 🔑 **The tell for level 2:** if two callers with the *identical role* should get *different* answers, the
> rule is **never** an endpoint attribute — it needs the data, so it belongs in the handler/domain via
> `ICurrentUser`. "Which *role* may call this?" → endpoint. "Which *specific user*, given *this* order?"
> ("not your own order," "only your own customers," "only your region") → handler/domain.

> ⚠️ This self-approval rule is an **illustration** of where a data-dependent rule goes — it is *not* wired
> into the base build (the base `Approve` in [Phase 8](08-approval-cancellation.md) checks only the state
> transition). It's exactly the kind of check you'd add using the cheat-sheet below.

### Where to add *your own* user & role checks — the beginner's guide

When people first learn authentication and authorization, they often know **what** they want to check but not **where** to put that check.

For example, you might think:

- "The password should be at least 8 characters."
- "Only managers can approve orders."
- "This account is disabled."
- "A sales representative can only edit their own orders."

All of those are valid checks—but **they do not belong in the same place.**

Think of your application as a team where every part has a specific job.

- The **Validator** checks whether the request is written correctly.
- The **Handler** performs the business operation.
- The **Endpoint** decides who is allowed to enter.
- The **Domain/Business Logic** decides whether the current user is allowed to perform a specific business action.

If you put checks in the wrong place, your code quickly becomes difficult to understand and maintain.

The table below shows the correct home for each kind of check.

| The check you want to add | Where it goes | Why? | Example |
|---|---|---|---|
| **Is the request well-formed?** (username not empty, password ≥ 8 chars, quantity > 0) | **Validator** (FluentValidation), in the same `...Handler.cs` file | The validator only checks whether the incoming request is valid. It does **not** know anything about users, roles, or the database. | `RuleFor(x => x.Password).MinimumLength(8);` |
| **Is this login allowed?** (account active, password correct, account locked) | **Handler** | The handler performs the login process. It can load the user from the database and decide whether authentication should succeed. | `if (user is null || !user.IsActive || !hasher.Verify(...)) throw ...;` |
| **Only users with a certain role may call this endpoint.** | **Endpoint**, using `RequireAuthorization` | The endpoint acts like the front door. Users without the required role are rejected before the handler even runs. | `app.MapPost(...).RequireAuthorization(p => p.RequireRole("SalesManager"));` |
| **The rule depends on the business data.** ("Managers may approve high-value orders", "Sales reps may only edit their own orders") | **Handler / Domain**, using `ICurrentUser` | The application must first load the business data before it can decide whether the user is allowed to continue. | `if (order.RequiresManager && !currentUser.IsInRole("SalesManager")) throw ...;` |

---

### Understanding the difference

A useful way to remember these responsibilities is to imagine visiting a secure office building.

#### 1. The Validator checks the form

Imagine you're filling out a visitor form.

The receptionist checks things like:

- Did you write your name?
- Did you write today's date?
- Is your phone number in the correct format?

They **do not** check whether you're actually allowed inside.

Likewise, a validator only checks whether the request is properly formed.

Good validator rules include:

- Username is required.
- Password must be at least 8 characters.
- Quantity must be greater than zero.
- Customer ID must be positive.

A validator should **never** ask:

- Does this user exist?
- Is the password correct?
- Is the account active?
- Is this user a manager?

Those questions require business knowledge or database access.

---

#### 2. The Endpoint checks who may enter

Once the request reaches the endpoint, ASP.NET Core already knows who the user is because the JWT has been validated.

The endpoint's job is simply to answer:

> "Does this user have the required role?"

If the endpoint requires the `SalesManager` role, then anyone without that role is rejected immediately.

```csharp
app.MapPost("/approve", ...)
    .RequireAuthorization(p =>
        p.RequireRole("SalesManager"));
```

The handler never runs if this check fails.

Think of this as a security guard checking your employee badge before allowing you into the building.

---

#### 3. The Handler performs the work

Once the user is allowed through the endpoint, the handler performs the actual business operation.

For login, the handler might:

1. Find the user.
2. Verify the password.
3. Check whether the account is active.
4. Generate a JWT.

For an order, the handler might:

1. Load the order.
2. Update its status.
3. Save the changes.

The handler is the correct place for rules that require the database or other services.

---

#### 4. Business rules belong with the business logic

Some authorization rules depend on the actual data.

For example:

- Orders over ₱500,000 require a manager.
- Only the employee who created the draft may edit it.
- Only warehouse staff assigned to a warehouse may modify its inventory.

These rules cannot be checked at the endpoint because the endpoint has not loaded the order yet.

The application must first retrieve the order from the database, then inspect both:

- the current user, and
- the business data.

For example:

```csharp
if (order.RequiresManager &&
    !currentUser.IsInRole("SalesManager"))
{
    throw new ForbiddenException();
}
```

This is a business decision, so it belongs inside the handler or domain logic.

---

### A quick rule of thumb

Before writing a check, ask yourself one question:

> **"What information does this check need?"**

If it only needs the request itself:

→ **Validator**

If it only needs the user's role:

→ **Endpoint**

If it needs the database:

→ **Handler**

If it needs both the current user **and** business data:

→ **Handler or Domain**

---

> ⚠️ **Never put business rules inside a validator.**
>
> A validator answers only:
>
> > "Is this request shaped correctly?"
>
> It should never answer:
>
> - Is the password correct?
> - Does the user exist?
> - Is the account active?
> - Is this customer allowed to order?
> - Is this user a manager?
>
> Those are business decisions that belong in the handler or domain. Validators should remain simple and independent of the database so they are easy to test.

---

> ⚠️ **Role names must match exactly.**
>
> If your application uses:
>
> - `SalesRep`
> - `SalesManager`
> - `Admin`
>
> then use those exact strings everywhere.
>
> These will **not** match:
>
> - `"Manager"`
> - `"salesmanager"`
> - `"sales_manager"`
>
> Role comparisons are exact. A typo usually doesn't produce an error—the authorization simply fails to match, and your security rule won't work as intended.
>
> To avoid mistakes, define role names as constants in one place and reuse them throughout the application.

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
> §9 is teaching you to keep straight.

> ⚠️ **Role strings must be the literal values `SalesRep`, `SalesManager`, `Admin`.** Not `"Manager"`, not
> `"salesmanager"`, not `"sales_manager"`. A typo doesn't throw an error — the role check simply never
> matches, so the rule silently does nothing and the wrong people get through. Copy the strings exactly, or
> better, keep them as `const` values in one place and reference those.

---

> 👉 Next: [Phase 4 (Part 3 of 4) — Refresh Tokens & the API-Key Gate](04c-jwt-refresh-apikey.md) — stay logged in without staying exposed, then add an app-level gate in front of the login door itself.
