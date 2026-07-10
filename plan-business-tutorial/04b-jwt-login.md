# Phase 4 (Part 2 of 5) — Login, Validation & Authorization

> **Phase 4 is split into five parts** — do them in order:
> 1. [Passwords & access tokens](04-jwt-auth.md)
> 2. **Login, validation & authorization** ← you are here
> 3. [Refresh tokens & the API-key gate](04c-jwt-refresh-apikey.md)
> 4. [Changing & resetting a password](04d-jwt-change-password.md)
> 5. [Request flow, checkpoint & common mistakes](04e-jwt-wrap-up.md)

> In [Part 1](04-jwt-auth.md), we learned how passwords are securely hashed and how a JWT access token is
> created.
>
> In this part, we'll put those building blocks together into a complete authentication system.
>
> We'll build a real login feature, validate JWTs on incoming requests, understand how ASP.NET Core
> reconstructs the authenticated user from the token, expose that user through `ICurrentUser`, and finally
> protect endpoints with role-based authorization.
>
> By the end of this part, users will be able to log in, receive a JWT, call protected endpoints, and you'll
> understand the complete authentication pipeline—from `POST /auth/login` to authorization inside your
> handlers.

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

### First pass: access token only

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

### Building `IUserRepository`

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

The **port** lives in `Erp.Shared.Common/Security/IUserRepository.cs` — the same home as `ICurrentUser` and
`IDbConnectionFactory`:

```csharp
// src/Shared/Erp.Shared.Common/Security/IUserRepository.cs
namespace Erp.Shared.Common;

public sealed record AppUser(
    long Id,
    string Username,
    string PasswordHash,
    string Role,
    bool IsActive,
    Guid SecurityStamp,          // rotates on every password change (Phase 3 schema)
    DateTime PasswordExpiresUtc); // DB-driven 90-day policy; caps a refresh token's lifetime

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
- SecurityStamp — the value we snapshot onto each refresh token, so a password change can invalidate it (§10)
- PasswordExpiresUtc — the database's password-expiry date, which becomes a refresh token's hard cap (§10)

Nothing more. The last two aren't used by *this* first-pass login; they earn their place when we add refresh
tokens in [Part 3, §10](04c-jwt-refresh-apikey.md), and `AppUser` is the natural place to carry them since
both procs (`GetByUsername`, `GetById`) already return them.

The repository exposes two read methods.

| Method | Used by |
|---------|---------|
| `GetByUsernameAsync()` | Login (this section) |
| `GetByIdAsync()` | Refresh tokens (§10) |

Although only one method is used today, adding the second one now avoids changing the interface later when we
build refresh tokens.

---

### Implementing `UserRepository`

Both repository methods are **pure reads**.

As discussed in **Phase 3b**, read-only operations don't need transactions, so they create their own database
connection directly.

The **adapter** lives in `Erp.Shared.Infrastructure/Identity/UserRepository.cs`:

```csharp
// src/Shared/Erp.Shared.Infrastructure/Identity/UserRepository.cs
using System.Data;                  // CommandType
using Dapper;                       // QuerySingleOrDefaultAsync, CommandDefinition
using Erp.Shared.Common;            // AppUser, IUserRepository
using Erp.Shared.Common.Data;       // IDbConnectionFactory

namespace Erp.Shared.Infrastructure;

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

### Building the Login handler

The login slice consists of four pieces:

- the command
- the result
- the validator
- the handler

`Erp.Api/Auth/Login/LoginHandler.cs`

```csharp
using System.Security.Authentication;   // AuthenticationException
using Erp.Shared.Common;                // IUserRepository
using Erp.Shared.Infrastructure;        // IPasswordHasher, IJwtTokenService
using Erp.Shared.Messaging;             // ICommand, ICommandHandler
using FluentValidation;

namespace Erp.Api.Auth;

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

### Walking through the handler

Although the handler performs authentication, it actually does very little itself.

Each dependency is responsible for one piece of work.

#### Step A — Load the user

```csharp
var user = await users.GetByUsernameAsync(cmd.Username, ct);
```

The repository looks up the user by username.

If no matching user exists, it returns `null`.

---

#### Step B — Validate the login

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

##### Why use one generic error?

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

#### Step C — Create the JWT

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

#### Step D — Return the result

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
using Carter;
using Erp.Shared.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Api.Auth;

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

### ⚠️ Register the login handler — the assembly scan won't find it

Here's a trap that will bite you the first time you call `POST /auth/login`. In
[Phase 2](02-mediator-own-vs-free.md), `AddSalesModule()` discovered handlers by scanning **the Sales
module's assembly**. But `LoginHandler` doesn't live in Sales — it lives in the **host**, `Erp.Api`, because
authentication isn't a business capability. Nothing scans the host.

So `sender.Send(cmd, ct)` would compile perfectly and then throw at runtime:

```
System.InvalidOperationException: No service for type
'Erp.Shared.Messaging.IRequestHandler`2[LoginCommand,LoginResult]' has been registered.
```

The fix is one line per host-assembly slice, in `Program.cs`. We register them by hand rather than scanning
`Erp.Api` — there are only a handful of auth slices, and an explicit list is easier to reason about than a
second reflection loop:

```csharp
// Program.cs — the auth slices live in the HOST assembly, so the module scans don't see them.
builder.Services.AddScoped<IRequestHandler<LoginCommand, LoginResult>, LoginHandler>();
builder.Services.AddScoped<IValidator<LoginCommand>, LoginValidator>();

// ...and the repository the handler depends on (this one is a shared-infrastructure service):
builder.Services.AddScoped<IUserRepository, UserRepository>();
```

Parts 3 and 4 add one more line each, as they add the refresh, logout, and change-password handlers.

## 7. Step 5 — Wiring token validation into the app

Creating an access token during login is only half of the authentication story.

Once a client has a JWT, it sends that token on every future request:

```
Authorization: Bearer eyJhbGciOi...
```

Something now has to answer an important question:

> **"Can this token be trusted?"**

That is the job of ASP.NET Core's JWT authentication middleware.

On every authenticated request it automatically:

- reads the JWT from the `Authorization` header,
- verifies the digital signature,
- checks that the token hasn't expired,
- validates the issuer and audience,
- and, if everything is valid, reconstructs the authenticated user.

The result is an authenticated `ClaimsPrincipal` stored in `HttpContext.User`.

We'll use that in the next step when we build `ICurrentUser`.

---

### Configuring JWT validation

Just like `JwtTokenService`, we don't care **where** the JWT settings come from.

We simply ask `builder.Configuration` for them.

ASP.NET Core's configuration system figures out whether each value came from
`appsettings.json`, User Secrets, environment variables, or another configuration provider.

In `Program.cs`:

```csharp
var jwt = builder.Configuration.GetSection("Jwt");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt["Issuer"],

            ValidateAudience = true,
            ValidAudience = jwt["Audience"],

            ValidateLifetime = true,

            ValidateIssuerSigningKey = true,

            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwt["SigningKey"]!)),

            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.Name
        };
    });

builder.Services.AddAuthorization();
```

---

### Walking through the configuration

Each validation option protects against a different kind of problem.

#### Validate the issuer

```csharp
ValidateIssuer = true
```

Only tokens created by **our own API** should be accepted.

If another application generates a JWT, it should be rejected.

---

#### Validate the audience

```csharp
ValidateAudience = true
```

A token intended for one application shouldn't automatically work for another.

The audience tells the server which application the token was created for.

---

#### Validate the lifetime

```csharp
ValidateLifetime = true
```

Expired tokens are rejected automatically.

This is why access tokens can safely be short-lived.

---

#### Validate the signing key

```csharp
ValidateIssuerSigningKey = true
```

This verifies the JWT's digital signature.

If anyone changes even a single character inside the token, the signature no longer matches and the request is rejected.

---

#### Configure the signing key

```csharp
IssuerSigningKey = ...
```

The same signing key used to create the JWT must also be used to validate it.

Think of it like signing a document.

The server signs the JWT during login.

Later, it verifies that same signature on every request.

---

### A very common JWT gotcha

```csharp
options.MapInboundClaims = false;
```

This line is easy to overlook but prevents a very confusing bug.

By default, ASP.NET Core silently renames several standard JWT claims.

For example:

```
sub
```

becomes

```
ClaimTypes.NameIdentifier
```

Later in this course we'll read the user's ID directly from the `sub` claim.

Without

```csharp
MapInboundClaims = false
```

that claim no longer exists under the name `sub`, causing `CurrentUser` to fail when it tries to read it.

Turning claim mapping off keeps every claim exactly as it appears inside the JWT.

---

### Middleware order matters

Authentication must always run before authorization.

```csharp
app.UseAuthentication();
app.UseAuthorization();
```

The request pipeline now looks like this:

```
Incoming Request
        │
Authorization: Bearer eyJ...
        │
        ▼
UseAuthentication()
        │
        ▼
JWT Middleware
        │
        ▼
Validate Signature
Validate Expiration
Validate Issuer
Validate Audience
        │
        ▼
Create ClaimsPrincipal
        │
        ▼
HttpContext.User
        │
        ▼
UseAuthorization()
```

Notice that authorization happens **after** authentication.

The framework cannot decide whether someone is allowed to perform an action until it first knows who they are.

---

### Where did these JWT settings come from?

Throughout this chapter we've written code like:

```csharp
var jwt = builder.Configuration.GetSection("Jwt");
```

Notice that we never tell ASP.NET Core:

- "Read `appsettings.json`."
- "Read User Secrets."
- "Read an environment variable."

Instead, we simply ask for the `Jwt` section.

The reason this works is because `builder.Configuration` is **not a single configuration file**.

It is a configuration system built from multiple **configuration providers**.

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

If multiple providers define the same key, the one loaded later wins.

The default precedence is:

```
Lowest priority

appsettings.json
        │
        ▼
appsettings.{Environment}.json
        │
        ▼
User Secrets
        │
        ▼
Environment Variables
        │
        ▼
Command-line Arguments

Highest priority
```

This means your JWT code never changes.

Whether the signing key comes from:

- `appsettings.Development.json`,
- User Secrets,
- an environment variable,
- Azure Key Vault,
- Docker,
- Kubernetes,

`builder.Configuration` always exposes it through the same API.

We'll use that same abstraction throughout the rest of the application.

## 8. Step 6 — Accessing the authenticated user (`ICurrentUser`)

In the previous step, ASP.NET Core validated the JWT and created a
`ClaimsPrincipal` representing the authenticated user.

That `ClaimsPrincipal` is available through:

```csharp
HttpContext.User
```

So why don't our handlers simply use `HttpContext.User` directly?

### Why an `ICurrentUser`?

Remember the rule from [Phase 1](01-solution-setup.md): **a module's `Domain/` never sees the web.**

`HttpContext` is part of ASP.NET Core. If a handler or an entity reached for it directly, business code would
be welded to the web framework — and you could no longer unit-test "a rep may only see their own customers"
without spinning up an HTTP request.

So we use the same port/adapter move as `IDbConnectionFactory` in [Phase 3b](03b-data-access.md):

- `Erp.Shared.Common` declares **what's needed** (`ICurrentUser`).
- `Erp.Shared.Infrastructure` supplies **how it's done** (`CurrentUser`, which reads `HttpContext`).

```
A handler (or the domain)
      │
      ▼
ICurrentUser            ← the port, in Erp.Shared.Common
      ▲
      │
CurrentUser             ← the adapter, in Erp.Shared.Infrastructure
      │
      ▼
HttpContext.User
```

Business code now depends only on the `ICurrentUser` abstraction, never on ASP.NET Core itself.

---

### Implementing `CurrentUser` and `ICurrentUser`

The **port** goes in `Erp.Shared.Common`, which references nothing — so any module may depend on it.

```csharp
// src/Shared/Erp.Shared.Common/Security/ICurrentUser.cs
namespace Erp.Shared.Common;

public interface ICurrentUser
{
    long UserId { get; }
    string Username { get; }
    string Role { get; }
    bool IsInRole(string role);
}
```

The **adapter** goes in `Erp.Shared.Infrastructure`, the only shared project allowed to touch ASP.NET:

```csharp
// src/Shared/Erp.Shared.Infrastructure/Security/CurrentUser.cs
using System.IdentityModel.Tokens.Jwt;   // JwtRegisteredClaimNames
using System.Security.Claims;            // ClaimsPrincipal, ClaimTypes, FindFirstValue
using Erp.Shared.Common;
using Microsoft.AspNetCore.Http;         // IHttpContextAccessor

namespace Erp.Shared.Infrastructure;

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal User => accessor.HttpContext?.User
        ?? throw new InvalidOperationException("No HTTP context.");

    public long UserId =>
        long.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

    public string Username =>
        User.FindFirstValue(ClaimTypes.Name)!;

    public string Role =>
        User.FindFirstValue(ClaimTypes.Role)!;

    public bool IsInRole(string role) =>
        User.IsInRole(role);
}
```

---

### Walking through the implementation

`CurrentUser` doesn't validate JWTs.

It doesn't query the database.

It doesn't know anything about login.

Its only responsibility is reading information that the authentication middleware has already verified.

#### Step A — Access the authenticated user

```csharp
private ClaimsPrincipal User =>
    accessor.HttpContext?.User
        ?? throw new InvalidOperationException("No HTTP context.");
```

`IHttpContextAccessor` gives us access to the current request's `HttpContext`.

Earlier in the request pipeline, `UseAuthentication()` validated the JWT access token and stored the authenticated `ClaimsPrincipal` in:

```csharp
HttpContext.User
```

This property simply returns that `ClaimsPrincipal`.

If no HTTP request exists, we throw an exception because there is no authenticated user to read.

---

#### Step B — Read the user ID

```csharp
User.FindFirstValue(JwtRegisteredClaimNames.Sub)
```

During login we created the `sub` claim:

```csharp
new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())
```

After the JWT was validated, ASP.NET Core reconstructed those claims and placed them into the `ClaimsPrincipal`.

`FindFirstValue()` simply searches those claims and returns the value of the `sub` claim.

---

#### Step C — Read the username

```csharp
User.FindFirstValue(ClaimTypes.Name)
```

This returns the value of the `Name` claim that was reconstructed from the validated JWT.

---

#### Step D — Read the role

```csharp
User.FindFirstValue(ClaimTypes.Role)
```

Later, our authorization rules will use this role to decide whether a user may perform a particular action.

---

#### Step E — Check roles

```csharp
User.IsInRole(role)
```

This is a convenience method provided by ASP.NET Core.

It checks whether the authenticated user belongs to the specified role.

---

### Register the service

Register both services in `Program.cs`:

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
```

`IHttpContextAccessor` allows Infrastructure to access the current HTTP request.

`CurrentUser` is registered as the implementation of `ICurrentUser`, allowing any handler to request it through dependency injection.

---

### How everything fits together

At runtime, the flow now looks like this:

```
Client Request
        │
Bearer JWT
        │
        ▼
Authentication Middleware
        │
        ▼
JWT validated
        │
        ▼
ClaimsPrincipal created
        │
        ▼
HttpContext.User
        │
        ▼
CurrentUser
        │
        ▼
ICurrentUser
        │
        ▼
Your handlers
```

Notice that `CurrentUser` never validates the JWT itself.

That work has already been completed by the authentication middleware.

`CurrentUser` simply exposes the authenticated user's information through an interface that a module can safely depend on.

We'll use `ICurrentUser` throughout the rest of the course—for example, to record who created an order and to apply business rules based on the current user's role.

## 9. Step 7 — Authorization

So far we've built the complete authentication pipeline:

```
User logs in
        │
        ▼
Password verified
        │
        ▼
JWT created
        │
        ▼
Client sends JWT
        │
        ▼
JWT validated
        │
        ▼
ICurrentUser
```

At this point the API knows **who** the caller is.

The next question is just as important:

> **"Now that we know who this user is, are they allowed to perform this action?"**

That question is called **authorization**.

Authorization decides whether an authenticated user has permission to perform a specific operation.

---

### Authentication vs. Authorization

Although the words sound similar, they answer two completely different questions.

| Authentication | Authorization |
|---------------|---------------|
| **Who are you?** | **What are you allowed to do?** |
| Verifies the user's identity. | Checks whether that user has permission. |
| Happens when the JWT is validated. | Happens after authentication succeeds. |

Think of entering an office building.

Authentication is showing your employee ID at the entrance.

Authorization is the security guard deciding which floors you're allowed to access.

You must always authenticate before authorization can happen.

If the server doesn't know who you are, it cannot decide what you're allowed to do.

In ASP.NET Core, authentication and authorization happen in exactly that order:

```
Incoming Request
        │
Bearer JWT
        │
        ▼
Authentication
(Validate the JWT)
        │
        ▼
ClaimsPrincipal
        │
        ▼
Authorization
(Check roles and permissions)
        │
        ▼
Your endpoint
```

Throughout the rest of this course, you'll see authorization happen at **two different levels**.

Some rules are simple enough to check before a handler runs.

Others depend on business data and can only be checked after the application has loaded that data from the database.

### Level 1 — Endpoint authorization

The simplest kind of authorization depends **only on the caller's role**.

For example:

> "Only Sales Managers may approve orders."

This rule doesn't depend on the order, the customer, or anything in the database.

It only asks one question:

> **"Does the authenticated user have the required role?"**

ASP.NET Core lets us express that rule directly on the endpoint.

```csharp
app.MapPost("/sales-orders/{id}/approve", ...)
    .RequireAuthorization(p =>
        p.RequireRole("SalesManager"));
```

This is called **endpoint authorization**.

Think of the endpoint as the front door to a building.

Before anyone is allowed inside, the security guard checks their employee badge.

If the badge doesn't contain the required role, they're turned away immediately.

The handler never executes.

```
Incoming Request
        │
        ▼
Authentication
(Is the JWT valid?)
        │
        ▼
Authorization
(Does the user have the required role?)
        │
        ▼
Handler runs
```

This has two important advantages.

- Unauthorized requests are rejected before any business code runs.
- The handler can assume the caller already has the minimum required role.

Endpoint authorization works best for rules such as:

- Only Managers may approve orders.
- Only Administrators may create users.
- Only Warehouse staff may adjust inventory.

All of these rules depend only on **who** is calling—not on any business data.

As soon as the rule needs information from the database, it no longer belongs at the endpoint. We'll see those cases later in this step.

### Understanding 401 vs. 403

Once authentication and authorization are wired into the application, there are two HTTP status codes you'll see frequently:

- **401 Unauthorized**
- **403 Forbidden**

Although their names sound similar, they mean completely different things.

The easiest way to understand the difference is to watch the same endpoint being called by different users.

Our seed data from [Phase 3](03-database-schema.md) creates three users:

| Username | Password | Role |
|---|---|---|
| `rep` | `Password123!` | `SalesRep` |
| `manager` | `Password123!` | `SalesManager` |
| `admin` | `Password123!` | `Admin` |

Suppose we protect the approve endpoint so that only Sales Managers may call it.

```csharp
app.MapPost("/sales-orders/{id}/approve", ...)
    .RequireAuthorization(p =>
        p.RequireRole("SalesManager"));
```

Now let's see what happens when different callers send requests.

| Caller | JWT | Role Check | Result |
|---|---|---|---|
| No token | ❌ | Cannot run | **401 Unauthorized** |
| `rep` | ✅ | `SalesRep` ≠ `SalesManager` | **403 Forbidden** |
| `manager` | ✅ | Role matches | **200 OK** |
| `admin` | ✅ | `Admin` ≠ `SalesManager` | **403 Forbidden** |

Notice that there are two completely different failure cases.

#### 401 Unauthorized

A **401 Unauthorized** response means the server could **not authenticate** the caller.

This happens when:

- no JWT was sent,
- the JWT has expired,
- the JWT was modified,
- the JWT's signature is invalid.

The authentication middleware rejects the request before authorization even begins.

```
Request
    │
    ▼
Authentication
    │
    ├── Invalid JWT
    ▼
401 Unauthorized
```

At this point the server cannot answer the question:

> **"What are you allowed to do?"**

because it doesn't even know who the caller is.

---

#### 403 Forbidden

A **403 Forbidden** response means authentication **succeeded**.

The JWT is valid.

The server knows exactly who the caller is.

However, the caller does not have permission to perform the requested action.

```
Request
    │
    ▼
Authentication
    │
    ▼
Authenticated User
    │
    ▼
Authorization
    │
    ├── Permission denied
    ▼
403 Forbidden
```

The important distinction is:

- **401** = "I don't know who you are."
- **403** = "I know who you are, but you're not allowed to do that."

Keeping those two responses separate makes APIs much easier to understand and debug.

Later, in [Phase 11](11-cross-cutting.md), we'll map our exceptions to these HTTP status codes so every endpoint returns consistent responses.

### There is no built-in role hierarchy

Looking at the previous table, one result surprises almost everyone the first time they see it.

Why did the `admin` user receive a **403 Forbidden**?

Isn't an Administrator supposed to have more privileges than a Sales Manager?

The answer is **no—not automatically.**

ASP.NET Core does **not** assume that one role is "higher" than another.

When you write:

```csharp
.RequireAuthorization(p =>
    p.RequireRole("SalesManager"));
```

the framework performs a very simple check:

> **"Does this user have the `SalesManager` role?"**

If the answer is **yes**, the request continues.

If the answer is **no**, the request is rejected.

It does **not** ask:

- Is this user an administrator?
- Is this role more powerful?
- Should a higher role inherit lower roles?

It simply compares role names.

That means these two users are treated differently:

| User | Roles | Passes `RequireRole("SalesManager")`? |
|------|-------|----------------------------------------|
| `manager` | `SalesManager` | ✅ Yes |
| `admin` | `Admin` | ❌ No |

Even though an Administrator may have more responsibilities in your organization, ASP.NET Core doesn't know that.

To allow both roles, you must explicitly list both.

```csharp
app.MapPost("/sales-orders/{id}/approve", ...)
    .RequireAuthorization(p =>
        p.RequireRole("SalesManager", "Admin"));
```

When multiple roles are supplied, **`RequireRole` uses OR logic**.

The user only needs **one** of the listed roles.

```
RequireRole(
    "SalesManager",
    "Admin")
```

means:

```
SalesManager
        OR
Admin
```

Either role is enough to access the endpoint.

This is exactly how we'll protect the approval endpoint later in the course.

---

#### Why ASPNET Core does not have a role hierarchy?

Different applications organize roles differently.

For example:

```
Company A

Admin
 └── Manager
      └── SalesRep
```

might consider administrators to inherit every permission.

But another company might use completely different rules:

```
Admin
    │
    ├── Manages users
    └── Cannot approve purchases

SalesManager
    │
    └── Can approve purchases
```

In that system, an Administrator should **not** automatically become a Sales Manager.

Because every application has different business rules, ASP.NET Core avoids making assumptions about role hierarchies.

Instead, it leaves that decision to your application.

---

#### If you want a hierarchy, build one explicitly

For small applications, the simplest solution is usually to list every role that should have access.

```csharp
.RequireAuthorization(p =>
    p.RequireRole(
        "SalesManager",
        "Admin"))
```

As applications become larger, however, repeatedly listing roles becomes difficult to maintain.

Instead of asking:

> "Does this user have the SalesManager role?"

you start asking:

> "Does this user have permission to approve orders?"

We'll look at that approach next, when we discuss **permission claims** inside a JWT.

### Level 2 — Business authorization

In the previous section, authorization depended only on the caller's role.

The endpoint could answer questions such as:

> **"Is this user a Sales Manager?"**

without loading anything from the database.

But many business rules are more complicated than that.

For example:

- A manager may not approve an order they created themselves.
- A sales representative may edit only their own draft orders.
- An order that would push a customer past their credit limit must be refused.
- Only employees assigned to a warehouse may update that warehouse's inventory.

These rules cannot be checked by the endpoint.

Why?

Because the endpoint hasn't loaded the business data yet.

It knows **who** is calling, but it doesn't yet know anything about:

- the order,
- the customer,
- the warehouse,
- or the item category.

Those rules belong one level deeper—in the **handler** or **domain**.

---

#### A good way to recognize business authorization

Ask yourself one question:

> **"Can I answer this question without loading data from the database?"**

If the answer is **yes**, the rule probably belongs on the endpoint.

If the answer is **no**, the rule belongs in the handler or domain.

Think about these two rules.

> "Only Sales Managers may approve orders."

The endpoint already has enough information to answer that.

Now compare it with:

> "A manager may not approve an order they created themselves."

The endpoint cannot answer that question because it doesn't know who created the order.

The application must first load the order before making the decision.

---

#### Two managers, one order

Suppose order **#50** was created by **Manager B**.

Now two different managers try to approve it.

| Caller | Role | Created the order? | Result |
|------|------|-------------------|--------|
| Manager A | `SalesManager` | ❌ No | ✅ Approve |
| Manager B | `SalesManager` | ✅ Yes | ❌ Reject |

Notice something important.

Both users have exactly the same role.

Both successfully passed the endpoint authorization.

The only thing that separates them is the **business data**.

That means the authorization rule cannot live on the endpoint.

It must live inside the handler.

---

#### Using `ICurrentUser`

Once the order has been loaded, the handler can compare:

- the authenticated user, and
- the order itself.

For example:

```csharp
if (order.CreatedBy.Value == currentUser.UserId)
{
    throw new ForbiddenException(
        "A manager cannot approve an order they created themselves.");
}
```

(`ForbiddenException` is one of the small exception types you'll define in [Phase 11](11-cross-cutting.md),
where the global handler learns to turn it into a **403**. And note this particular rule — "not your own
order" — isn't one we build; it's here because it's the clearest possible example of a check the endpoint
*cannot* make. It's waiting for you as an [extension exercise](13-wrap-up-checklist.md).)

Here the handler combines two pieces of information:

- `currentUser.UserId` — who is making the request.
- `order.CreatedBy` — who created the order.

Only after comparing both values can it decide whether approval is allowed.

This is exactly the kind of rule `ICurrentUser` was designed for.

---

#### Endpoint vs. business authorization

A useful way to remember the difference is:

| Endpoint authorization | Business authorization |
|------------------------|------------------------|
| Depends only on the authenticated user's role. | Depends on both the authenticated user and business data. |
| Runs before the handler. | Runs inside the handler or domain. |
| Never needs the database. | Usually requires loading business data first. |

A simple rule of thumb is:

> **If two users with the same role should receive different answers, the rule belongs in the handler or domain—not on the endpoint.**

We'll use this pattern repeatedly throughout the rest of the course whenever authorization depends on the business data rather than just the caller's role.

### Can a JWT contain more than one role?

So far, our JWT contains a single role:

```json
{
  "sub": "42",
  "name": "rep",
  "role": "SalesRep"
}
```

That keeps the examples throughout this course simple, and for many business applications it is completely sufficient.

But a JWT isn't limited to a single role.

A JWT is simply a collection of **claims**.

A claim is just one fact about the authenticated user.

That means you can include as many claims as your application needs.

For example, a larger application might issue a JWT like this:

```json
{
  "sub": "42",
  "name": "rep",
  "roles": [
    "SalesRep",
    "SalesManager"
  ],
  "permissions": [
    "orders:read",
    "orders:create",
    "orders:approve"
  ],
  "tenant": "acme-corp",
  "region": "APAC"
}
```

Every additional claim gives the application more information about the authenticated user.

For example:

| Claim | Purpose |
|-------|---------|
| `roles` | A user may belong to multiple roles. |
| `permissions` | Represents specific capabilities instead of broad job titles. |
| `tenant` | Identifies which company or customer the user belongs to in a multi-tenant application. |
| `region` | Restricts users to data belonging to a particular region. |

---

#### Roles vs. permissions

As applications grow, roles often become too broad.

Suppose your application has these roles:

```
SalesRep
SalesManager
Admin
```

One way to protect the approve endpoint is:

```csharp
.RequireAuthorization(p =>
    p.RequireRole("SalesManager", "Admin"));
```

That works well for a small application.

But imagine dozens of endpoints.

Soon you'll find yourself repeatedly writing:

```text
SalesManager OR Admin

SalesManager OR Admin

SalesManager OR Admin
```

If your business later decides that supervisors may also approve orders, every endpoint must be updated.

Instead of checking for roles, many larger systems check for **permissions**.

Instead of asking:

> **"Is this user a Sales Manager?"**

they ask:

> **"Does this user have permission to approve orders?"**

For example:

```
orders:approve
```

Now it doesn't matter which role granted that permission.

The authorization rule remains the same.

Changing who receives that permission becomes a configuration decision instead of a code change.

---

#### Why this course uses a single role

Although permission-based authorization is common in larger systems, it also introduces additional concepts:

- permission management,
- role-to-permission mapping,
- authorization policies,
- permission administration.

Those topics would distract from learning JWT authentication itself.

To keep the examples focused, this course stores a single role inside the JWT.

The authentication pipeline is exactly the same.

The only difference is the claims you choose to include.

As your application's authorization requirements grow, you can extend the JWT by adding more claims without changing the overall authentication flow.

### From WebForms — module/view/button permissions

If you've built applications with **ASP.NET WebForms**, this idea probably looks familiar.

Many WebForms applications don't authorize using simple roles alone.

Instead, they maintain a **permission matrix**.

For example:

- Which modules may this user access?
- Which screens should appear in the menu?
- Which buttons should be enabled?
- Which actions may they perform?

A JWT can carry that information too.

For example:

```json
{
  "sub": "42",
  "name": "rep",
  "role": "SalesRep",
  "access": [
    {
      "moduleId": "SALES",
      "views": [
        "orders",
        "customers"
      ],
      "buttons": [
        "order:create",
        "order:cancel"
      ]
    },
    {
      "moduleId": "INVENTORY",
      "views": [
        "stock"
      ],
      "buttons": []
    }
  ]
}
```

The client application can read these claims to decide what to display.

For example:

- Show or hide menu items.
- Enable or disable toolbar buttons.
- Hide pages the user cannot access.

This is very similar to what many WebForms applications did with code like:

```vb
Panel.Visible = hasAccess
btnApprove.Enabled = canApprove
```

The difference is where the information comes from.

Instead of reading permissions from a server-side session, the client reads them from the authenticated user's claims.

---

#### UI permissions are not security

A common mistake is assuming that hiding a button secures the application.

It doesn't.

Suppose the front end hides the **Approve** button because the current user lacks permission.

Nothing prevents someone from manually sending this request:

```http
POST /sales-orders/50/approve
```

using a tool such as Postman or curl.

That's why the server must **always** perform its own authorization checks.

Think of the UI as a convenience layer.

It helps users avoid attempting actions they cannot perform.

The server is still responsible for enforcing the real security rules.

```
Client
    │
Hide Approve button
    │
    ▼
User cannot click it
        │
        ├──────────────┐
        │              │
        ▼              ▼
Normal UI         Manual HTTP request
                        │
                        ▼
                  API Endpoint
                        │
                        ▼
              Authorization check
                        │
                        ▼
                Allow or Reject
```

Whether the request comes from your application, Postman, curl, or another client makes no difference.

Every request reaches the same authorization logic.

That's why endpoint and business authorization remain the application's true security boundary.

The UI simply reflects those rules to improve the user experience.

### JWT size trade-offs

At this point you might be wondering:

> **"If a JWT can contain all of these claims, why not put everything inside it?"**

The answer is simple:

> **Every claim makes the token larger.**

Remember that the client sends the JWT with **every authenticated request**.

```
Request 1
Authorization: Bearer eyJ...

Request 2
Authorization: Bearer eyJ...

Request 3
Authorization: Bearer eyJ...
```

Unlike a database query, which happens only when needed, the JWT travels across the network every single time.

A small token has very little overhead.

A large token is transmitted repeatedly throughout the user's session.

---

#### Another trade-off: claims become stale

JWTs are created during login.

After that, the claims inside the token never change.

Suppose a user logs in at **9:00 AM** with this permission:

```json
{
  "permissions": [
    "orders:approve"
  ]
}
```

At **10:00 AM**, an administrator removes that permission.

The user still has the old JWT.

That means the token continues to say:

```json
{
  "permissions": [
    "orders:approve"
  ]
}
```

until the access token expires and a new one is issued.

This is one reason access tokens are intentionally short-lived.

In the next part of this phase we'll introduce **refresh tokens**, which periodically issue a new access token containing the latest claims.

---

#### What should go inside a JWT?

A good rule of thumb is:

> **Put into the JWT only the information the server needs to authorize every request.**

Examples include:

- User ID
- Username
- Role
- Small permission sets
- Tenant ID

Avoid placing large or frequently changing data inside the token.

For example:

- Complete menu structures
- Hundreds of button permissions
- User preferences
- Customer lists
- Large profile information

Those are usually better loaded separately after login.

For example:

```
POST /auth/login
        │
        ▼
Receive JWT
        │
        ▼
GET /me
        │
        ▼
User profile

GET /me/permissions
        │
        ▼
Full permission matrix
```

This approach keeps the JWT small while still allowing the application to retrieve richer information when needed.

---

#### A practical guideline

For this course we'll keep our JWT intentionally small.

It contains only the claims that every request needs:

- User ID
- Username
- Role
- Token ID (`jti`)

Everything else can be loaded separately when the application starts.

As your applications grow, this simple guideline scales well:

> **Use the JWT to identify the caller. Use your API to retrieve everything else.**

### Where should I put this check?

By this point you've seen several kinds of authentication and authorization rules.

A common beginner question is:

> **"I know what I want to check—but where should I put that check?"**

The answer depends on **what information the rule needs**.

| The rule you want to write | Where it belongs | Why? | Example |
|---|---|---|---|
| **Is the request well-formed?** | **Validator** | Validators only check whether the request is valid. They don't know about users, roles, or the database. | `RuleFor(x => x.Password).MinimumLength(8);` |
| **Can this user log in?** | **Handler** | The handler loads the user, verifies the password, and decides whether authentication succeeds. | `if (user is null \|\| !hasher.Verify(...)) throw ...;` |
| **May this role call this endpoint?** | **Endpoint** | The endpoint checks whether the authenticated user satisfies the required role before the handler runs. | `.RequireAuthorization(p => p.RequireRole("SalesManager"))` |
| **Does this business rule depend on business data?** | **Handler / Domain** | The handler must load the data first; then the entity itself decides whether the action is legal. | `order.Approve(new UserId(currentUser.UserId));` |

---

#### Why is "does this record exist?" handled in the handler?

A common question is:

> **"Why isn't `if (order is null)` considered a business rule?"**

Because checking whether a record exists is **not** business logic—it's part of loading the data needed to perform the use case.

The domain cannot decide whether an order exists, because the domain object doesn't exist until the handler loads it.

```csharp
var order = await orders.GetAsync(cmd.Id, ct);

if (order is null)
    throw new NotFoundException($"Sales order {cmd.Id.Value} was not found.");

order.Approve(new UserId(currentUser.UserId));   // the DOMAIN decides whether this is legal

await orders.UpdateStatusAsync(order, tx, ct);
```

The handler is responsible for:

- Loading data from the database
- Checking whether the requested data exists
- Calling the domain to perform business behavior
- Saving any changes

The domain is responsible for deciding **whether the requested action is allowed**.

```csharp
// This is roughly what SalesOrder.Approve looks like — you'll write it for real in Phase 5.
public void Approve(UserId approver)
{
    if (Status != SalesOrderStatus.PendingApproval)
        throw new DomainException("Only orders awaiting approval can be approved.");

    Status = SalesOrderStatus.Approved;
    ApprovedBy = approver;
    Raise(new SalesOrderApproved(Id, CustomerId, Total, approver));
}
```

Notice that the entity doesn't know anything about SQL, Dapper, repositories, or HTTP. It only knows the business rules.

> 🔑 **Notice what `Approve` does *not* take: the caller's role.** "Only a `SalesManager` may approve" needs
> nothing but the JWT, so it's already been enforced at the endpoint by the time this method runs — the
> domain would just be re-asking a question that was answered at the door. What the domain *does* enforce is
> the rule that needs the order itself: you cannot approve something that isn't awaiting approval. That's the
> split this whole section is about, and [Phase 8](08-approval-cancellation.md) builds it for real.

---

#### A simple way to remember it

Before writing any check, ask yourself:

> **"What information does this rule need?"**

If it only needs the incoming request:

→ **Validator**

If it only needs the authenticated user's role:

→ **Endpoint**

If it needs to load data from the database:

→ **Handler**

If it needs business data that has already been loaded:

→ **Domain**

```
                 What information does the rule need?
                               │
        ┌──────────────────────┼──────────────────────┐
        │                      │                      │
        ▼                      ▼                      ▼
Request only?            User's role?          Business data?
        │                      │                      │
        ▼                      ▼                      ▼
 Validator               Endpoint          Handler / Domain
```

A useful mental model is:

- **Validator** → "Is the request valid?"
- **Endpoint** → "Is the caller allowed to enter?"
- **Handler** → "Load everything needed for this use case."
- **Domain** → "Given this data, is this business action allowed?"

---

#### Common mistakes

⚠️ **Don't put business rules in validators.**

A validator answers only:

> **"Is this request shaped correctly?"**

It should never answer questions such as:

- Does the user exist?
- Is the password correct?
- Is the account active?
- Is this customer allowed to order?
- Is this user a manager?

Those are business decisions and belong in the handler or domain.

---

⚠️ **Don't trust the UI for security.**

Hiding a button or menu item improves the user experience, but it does **not** secure your API.

Every protected operation must still be enforced on the server using endpoint authorization or business authorization.

---

⚠️ **Keep role names consistent.**

If your application defines:

- `SalesRep`
- `SalesManager`
- `Admin`

then use those exact values everywhere.

A typo usually won't produce an error.

Instead, the authorization check simply won't match.

To avoid mistakes, define role names as constants in one place and reuse them throughout the application.

---

At this point, users can:

- log in,
- receive a JWT,
- call protected endpoints,
- be authenticated by ASP.NET Core,
- be exposed through `ICurrentUser`,
- and be authorized using roles and business rules.

In the next part we'll complete the authentication system by adding **refresh tokens**, allowing users to stay signed in securely without keeping long-lived access tokens.