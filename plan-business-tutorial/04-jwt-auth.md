# Phase 4 — JWT Authentication & Authorization

> This is a **new topic** not covered in the earlier plans, so we go slowly and assume nothing. By the
> end you can log in, receive a token, and protect endpoints — including "only a Manager may approve."

---

## 1. What we're building

- A `POST /auth/login` endpoint: give it a username + password, get back a **JWT token**.
- Token *validation* wired into ASP.NET Core so protected endpoints reject anyone without a valid token.
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

## 4. Step 2 — Creating the token (`IJwtTokenService`)

`Erp.Shared.Infrastructure/Security/JwtTokenService.cs`:

```csharp
public interface IJwtTokenService
{
    string CreateToken(long userId, string username, string role);
}

public sealed class JwtTokenService(IConfiguration config) : IJwtTokenService
{
    public string CreateToken(long userId, string username, string role)
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
public sealed record LoginResult(string Token, string Username, string Role);

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
    IJwtTokenService tokens) : ICommandHandler<LoginCommand, LoginResult>
{
    public async Task<LoginResult> Handle(LoginCommand cmd, CancellationToken ct)
    {
        var user = await users.GetByUsernameAsync(cmd.Username, ct);

        // Same generic error whether the user is missing OR the password is wrong.
        if (user is null || !user.IsActive || !hasher.Verify(cmd.Password, user.PasswordHash))
            throw new AuthenticationException("Invalid username or password.");

        var token = tokens.CreateToken(user.Id, user.Username, user.Role);
        return new LoginResult(token, user.Username, user.Role);
    }
}
```

> ℹ️ **What are `IUserRepository` and `AppUser`?** Just like the Sales repositories, the Login handler
> declares the small port it needs and lets Infrastructure implement it. `IUserRepository` is one method —
> `Task<AppUser?> GetByUsernameAsync(string username, CancellationToken ct);` — implemented in
> `Shared.Infrastructure` with Dapper calling `identity.usp_AppUser_GetByUsername` from
> [Phase 3](03-database-schema.md). `AppUser` is a plain read shape carrying `Id, Username, PasswordHash,
> Role, IsActive` — deliberately *not* a rich domain aggregate, because authentication is a platform
> concern, not a Sales business rule. Register it in the composition root ([Phase 11 §7](11-cross-cutting.md)).

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

## 9. Two ways to authorize (endpoint vs. domain)

There are **two levels** of authorization, and using each correctly is a key lesson:

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

## 10. How a full authenticated request flows

```
1. POST /auth/login {rep, Password123!}  → LoginHandler verifies hash → returns JWT
2. Client stores the token.
3. POST /sales-orders   Authorization: Bearer <token>
     → UseAuthentication validates signature+expiry → builds HttpContext.User with claims
     → UseAuthorization checks the endpoint's role requirement (here: SalesRep or Manager — ok)
     → endpoint calls sender.Send(command)
     → handler injects ICurrentUser → reads UserId to stamp CreatedByUserId
```

## 11. Common mistakes & best practices

- ❌ **Storing plain passwords / weak hashing (MD5, SHA1 alone).** ✅ Salted PBKDF2 (or bcrypt/Argon2).
- ❌ **Committing the signing key.** ✅ user-secrets/env vars; the key ≥ 32 chars.
- ❌ **Different login errors for "no user" vs "bad password."** ✅ One generic message.
- ❌ **Forgetting `.AllowAnonymous()` on login**, then wondering why you can't log in (you'd need a
  token to get a token). ✅ Login is always anonymous.
- ❌ **Middleware in the wrong order** (`UseAuthorization` before `UseAuthentication`). ✅ Authenticate,
  then authorize.
- ❌ **Making handlers depend on `HttpContext`.** ✅ Depend on `ICurrentUser`; keep the Application layer
  framework-free and unit-testable (you can fake `ICurrentUser` in tests).
- ✅ **Best practice: short token lifetimes** (e.g. 60 min). Real systems add refresh tokens; that's a
  fine "next step" but out of scope here.

---

## ✅ Check Yourself

1. In one sentence each: what's authentication vs. authorization?
2. Why does the Application layer talk to `ICurrentUser` instead of `HttpContext`?
3. Why must `UseAuthentication` come before `UseAuthorization`?
4. Give an authorization rule that belongs on the *endpoint*, and one that belongs in the *handler* —
   and say why.

> 👉 Next: [Phase 5 — The SalesOrder domain model and its business rules](05-domain-model.md).
