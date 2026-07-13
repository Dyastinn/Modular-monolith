# Phase 4 (Part 1 of 5) — Passwords & Access Tokens

> **Phase 4 is split into five parts** — do them in order:
> 1. **Passwords & access tokens** ← you are here
> 2. [Login, validation & authorization](04b-jwt-login.md)
> 3. [Refresh tokens & the API-key gate](04c-jwt-refresh-apikey.md)
> 4. [Changing & resetting a password](04d-jwt-change-password.md)
> 5. [Request flow, end-to-end checkpoint & common mistakes](04e-jwt-wrap-up.md)


> Nothing here assumes you've done auth before. We go slowly. By the end of all five parts you can log in,
> receive a token, refresh it safely, and protect endpoints — including "only a **SalesManager** may approve."

---

> ⏱️ **Time for all five parts:** ~3.5–4 hr (this Part 1 is ~50 min). Everything is new: hashing,
> tokens, claims, middleware order, the `MapInboundClaims` gotcha, and refresh-token rotation with reuse
> detection ([Part 3](04c-jwt-refresh-apikey.md)). Each part ends with its own runnable checkpoint — budget
> ~10 minutes each for those, and don't skip them. Go slow; rushing auth is how subtle security bugs sneak in.

## 1. What we're building

**Across all five parts**, you'll end up with a complete authentication system:

| Part | What it adds | Ends with a checkpoint proving… |
|---|---|---|
| **1** (this file) | Password hashing (`IPasswordHasher`) and access-token creation (`IJwtTokenService`) — the two ingredients login needs | a password verifies, and a signed token mints + decodes |
| [**2**](04b-jwt-login.md) | The `POST /auth/login` slice, token *validation* on incoming requests, `ICurrentUser`, and role-based authorization | you can log in, and the token gates a real endpoint |
| [**3**](04c-jwt-refresh-apikey.md) | **Refresh tokens** with rotation and reuse detection, logout endpoints, and the **API-key gate** that keeps unknown client apps out even before authentication | the gate fires before login; refresh rotates; a replay burns the family |
| [**4**](04d-jwt-change-password.md) | `POST /auth/change-password`, and the surprising reason it must be anonymous | an anonymous change works, and voids every session |
| [**5**](04e-jwt-wrap-up.md) | The full request-flow trace, one end-to-end run of *all* the doors, and the mistakes people make | the whole gate, in one pass |

> 🔑 **Each part ends in something you can run.** Phase 4 is the longest phase in the course, and a single
> checkpoint at the very end would leave you debugging five parts at once. Don't skip a part's checkpoint to
> "catch up at the end" — the whole reason they're there is so a failure has only one part's worth of code to
> hide in.

**This part builds two services and nothing else.** They're the pieces login will assemble in Part 2:
a `PasswordHasher` that can store and check a password safely, and a `JwtTokenService` that can mint a signed,
short-lived access token. Neither is reachable over HTTP yet — there's no endpoint until Part 2.

## 2. Why it's designed this way

### What even *is* a JWT?

A **JWT** (JSON Web Token) is a small, digitally-signed string that says "this is user *rep*, role
*SalesRep*, and these claims were issued by the server." The client sends it on every request in an
`Authorization: Bearer <token>` header.

When the API receives the request, it verifies the token's **digital signature**. If the signature is valid
and the token hasn't expired, the server trusts the claims inside **without querying the database again**.

> 💡 **Analogy:** a JWT is like a **festival wristband**. At the gate (login) they check your ID once and
> give you a tamper-proof wristband. After that, every stage just glances at the wristband—they don't ask for
> your ID again. The digital signature is what makes the wristband tamper-proof. Anyone can see the wristband,
> but only the festival can issue a genuine one.

### Why bother? The problem a token solves

The fastest way to understand why tokens exist is to imagine a system without them.

**Attempt 1 — send the password on every request.**

Every button click sends your username and password again.

Three problems immediately appear:

- 🔓 Your password travels across the network on every request, increasing the chance it could be exposed.
- 🐌 The server must perform the expensive password hash verification every single time. That slowness is a
  feature during login, but a waste on every request.
- 🗄️ The client must keep the user's real password somewhere so it can resend it. Storing passwords on the
  client is something we want to avoid whenever possible.

**Attempt 2 — log in once, then carry a token.**

The user logs in **once**.

The server performs the expensive password verification **once**.

If everything is valid, the server creates a signed JWT containing information about the user.

After that, the client sends **the token**, not the password.

The server simply verifies the token's signature, which is much faster than checking the password again.

The entire purpose of a JWT can be summarized in one sentence:

> 🔑 **A JWT lets the server re-establish who you are on every request without asking for your password again.**

---

### Stateless authentication

A natural question is:

> **"Doesn't the server remember who's logged in?"**

With JWT authentication, the answer is **no**.

This style of authentication is called **stateless authentication**.

The server keeps **no in-memory session** and no list of currently logged-in users.

Instead, every request carries everything the server needs inside the JWT itself.

```
Request
    │
Authorization: Bearer eyJ...
    │
    ▼
API validates the token
    │
    ▼
Claims become the authenticated user
```

This has two major advantages.

- **It scales easily.** Any server can validate the token, so requests can be distributed across multiple
  servers without sharing session state.
- **It reduces database work.** Most authenticated requests don't need to query the Users table just to find
  out who is calling.

There is one tradeoff.

Since the server doesn't remember sessions, it also can't instantly forget them.

A valid access token remains valid until it expires.

That's why access tokens are intentionally short-lived, and why we'll introduce **refresh tokens** later in
this phase.

---

### What is a claim?

A **claim** is simply one piece of information about the authenticated user.

Common claims include:

| Claim | Meaning |
|-------|---------|
| `sub` | The user's unique ID |
| `name` | The user's username |
| `role` | The user's role |

You can think of a JWT as a collection of claims that has been digitally signed by the server.

For example:

```json
{
  "sub": "42",
  "name": "rep",
  "role": "SalesRep"
}
```

Later in this phase we'll create these claims using code like:

```csharp
new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())
new Claim(ClaimTypes.Name, username)
new Claim(ClaimTypes.Role, role)
```

When ASP.NET Core validates the JWT on future requests, these claims become the authenticated user's identity.

---

### What a token actually looks like

A JWT is simply **three pieces of text joined together with dots**:

```
header.payload.signature
```

A real JWT looks something like this:

```
eyJhbGciOiJIUzI1NiJ9  .  eyJzdWIiOiI0MiIsInJvbGUiOiJTYWxlc1JlcCJ9  .  3Rk...signature...
└──── header ────┘        └──────────── payload ────────────┘      └── signature ──┘
```

Each section has a different purpose.

- **Header** — describes how the token was signed (for example, HMAC-SHA256).
- **Payload** — contains the claims about the user.
- **Signature** — proves that the server created the token and that it hasn't been modified.

Two facts about JWTs surprise most beginners.

### JWTs are signed, not encrypted

Anyone can decode the payload.

For example, a website or Base64 decoder could show:

```json
{
  "sub": "42",
  "role": "SalesRep"
}
```

This is completely normal.

The payload should never contain sensitive information such as:

- passwords
- credit card numbers
- API keys
- personal secrets

JWTs are designed to prove authenticity, **not** to hide information.

### Why can't someone simply change the role?

Suppose an attacker changes:

```json
{
    "role": "SalesRep"
}
```

into

```json
{
    "role": "Admin"
}
```

That immediately invalidates the signature.

Since only the server knows the signing key, only the server can generate a valid signature.

On every request ASP.NET Core recalculates the signature and compares it with the one inside the token.

If they don't match, the token is rejected.

This is why JWTs are often described as:

> **Readable, but not forgeable.**

---

### Authentication vs. Authorization

These two terms are often confused.

**Authentication** answers:

> **Who are you?**

It verifies your identity.

Examples:

- logging in
- validating a JWT
- proving the caller is really user 42

**Authorization** answers:

> **What are you allowed to do?**

It uses the authenticated user's claims to decide whether an action is permitted.

Examples:

- Only Managers may approve orders.
- Only Admins may create users.
- A Sales Representative may create orders but not approve them.

Authentication always comes first.

Once the server knows **who** you are, it can decide **what** you're allowed to do.

## 3. Step 1 — Hashing passwords (never store plain text)

Before we can build a login feature, we need a safe way to **store** and **verify** passwords.

A password must **never** be stored directly in the database. If someone steals your database, every user's
password would immediately be exposed.

Instead, we store a **one-way hash**.

A hash is the result of running data through a mathematical function that cannot be reversed. That means we
can verify whether a password is correct, but we cannot recover the original password from the stored hash.

.NET already provides a secure password hashing implementation called `Rfc2898DeriveBytes`, which implements
the **PBKDF2 (Password-Based Key Derivation Function 2)** algorithm defined by **RFC 2898**.

Unlike a normal hash such as SHA-256, PBKDF2 intentionally performs hundreds of thousands of hashing rounds.
That makes each password verification slightly slower, which is exactly what we want—it makes brute-force
attacks dramatically more expensive.

### Why an `IPasswordHasher`?

The login handler shouldn't know *how* passwords are hashed.

Its only responsibility is deciding whether the supplied password is correct.

Just like we've done throughout this course, we separate **what** we need from **how** it's implemented.

```
LoginHandler
      │
      ▼
IPasswordHasher
      │
      ▼
PasswordHasher
      │
      ▼
PBKDF2
```

The handler depends only on the `IPasswordHasher` interface. The infrastructure project provides the actual
implementation using PBKDF2.

Create `Erp.Shared.Infrastructure/Security/PasswordHasher.cs`:

```csharp
// src/Shared/Erp.Shared.Infrastructure/Security/PasswordHasher.cs
using System.Security.Cryptography;   // RandomNumberGenerator, Rfc2898DeriveBytes, CryptographicOperations

namespace Erp.Shared.Infrastructure;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string storedHash);
}

public sealed class PasswordHasher : IPasswordHasher
{
    // SaltSize
    //     16 random bytes mixed into every password.
    //
    // KeySize
    //     Produce a 32-byte (256-bit) hash.
    //
    // Iterations
    //     The PBKDF2 work factor. Higher values make passwords harder to crack.
    //     We also store this value inside the hash so it can be increased later
    //     without breaking existing passwords.
    private const int SaltSize = 16, KeySize = 32, Iterations = 600_000;

    public string Hash(string password)
    {
        // Generate a cryptographically secure random salt.
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);

        // Hash the password using PBKDF2.
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            KeySize);

        // Store: iterations.base64Salt.base64Hash
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public bool Verify(string password, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(storedHash))
            return false;

        // Split the stored value into:
        // iterations, salt and hash.
        var parts = storedHash.Split('.', 3);

        if (parts.Length != 3)
            return false;

        if (!int.TryParse(parts[0], out var iterations))
            return false;

        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var key = Convert.FromBase64String(parts[2]);

            var attempt = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                key.Length);

            return CryptographicOperations.FixedTimeEquals(attempt, key);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
```

### Walking through the implementation

`PasswordHasher` exposes two methods.

```csharp
Hash(string password)
```

This is used when a user first creates or changes their password.

It generates a random salt, hashes the password using PBKDF2, and returns the value that will be stored in the
database.

```csharp
Verify(string password, string storedHash)
```

This is used during login.

It reads the stored hash, recreates the PBKDF2 hash using the same salt and iteration count, and compares the
result with the stored value.

If they match, the password is correct.

If they don't, authentication fails.

### Why do we use a salt?

A **salt** is a random value generated for every password before hashing.

Without a salt:

```
Password123 → Hash A
Password123 → Hash A
```

Every user with the same password ends up with the same hash.

With a random salt:

```
Password123 + Salt A → Hash X
Password123 + Salt B → Hash Y
```

Now the hashes are completely different, even though the original password is identical.

This prevents attackers from using precomputed lookup tables, often called **rainbow tables**, to crack large
numbers of passwords.

The salt is **not secret**, so we store it together with the hash.

### Why store the iteration count?

Notice that we store

```
iterations.salt.hash
```

instead of only the hash.

As computers become faster, we may decide to increase the PBKDF2 work factor.

By storing the iteration count alongside each password, older passwords continue to verify correctly while
newer passwords can be created using a higher iteration count.

### Why use `FixedTimeEquals()`?

A normal comparison such as

```csharp
attempt == key
```

may stop comparing as soon as it finds the first different byte.

That creates tiny timing differences that sophisticated attackers can sometimes measure.

`CryptographicOperations.FixedTimeEquals()` always compares every byte before returning, making timing attacks
much harder.

### Checkpoint — real hashes for the seed users ✅

[Phase 3](03-database-schema.md) seeded `rep`, `manager`, and `admin` with the placeholder
`'<hash-of-Password123!>'`. Nothing can log in until those become real hashes — so generating them is both
this part's exercise *and* the way you prove `PasswordHasher` works.

Call `Hash("Password123!")` **three times** (each call produces a different string, because each gets its own
random salt) and paste the three results into your `003_seed.sql`. Then sanity-check the round trip:

```csharp
var hasher = new PasswordHasher();
var stored = hasher.Hash("Password123!");

Console.WriteLine(stored);                                  // 600000.rXf2…==.9kQ1…=
Console.WriteLine(hasher.Verify("Password123!", stored));   // True
Console.WriteLine(hasher.Verify("wrong", stored));          // False
Console.WriteLine(hasher.Hash("Password123!") == stored);   // False — different salt every time
```

Those four lines are the whole lesson in miniature: the same password verifies, a wrong one doesn't, and no
two hashes of the same password are ever equal.

> ℹ️ **Don't want to boot the app just to generate three hashes?** The companion
> [04a-password-hashing/](04a-password-hashing/README.md) folder has a Python implementation of the same
> algorithm with the same parameters. Run `python hash_password.py --seed` and copy its output into your seed
> script — the hashes it produces verify correctly against the C# `Verify()` above.

## 4. Step 2 — Creating the access token (`IJwtTokenService`)

Now that we can safely store and verify passwords, the next piece is creating the **access token** that a
client receives after a successful login.

Remember the goal of JWT authentication:

1. The user logs in with a username and password.
2. The server verifies the password.
3. The server creates a signed JWT.
4. The client sends that JWT on every future request instead of sending the password again.

Something has to create that JWT.

### Why an `IJwtTokenService`?

We could put all of the JWT creation logic directly inside the login handler.

But then the login handler would need to know:

- how to build claims,
- how to sign a token,
- how long the token should live,
- how to serialize the JWT into a string.

Those responsibilities don't belong to the login handler.

Instead, we create a dedicated service whose only responsibility is generating access tokens.

```
LoginHandler
      │
      ▼
IJwtTokenService
      │
      ▼
JwtTokenService
      │
      ▼
Creates a signed JWT
```

Later, the login handler will simply call:

```csharp
var accessToken = jwt.CreateAccessToken(
    user.Id,
    user.Username,
    user.Role);
```

without needing to know anything about how JWTs are built internally.

---

### Packages and configuration

JWT support isn't part of the .NET runtime itself, so we need two packages.

```bash
dotnet add src/Shared/Erp.Shared.Infrastructure package System.IdentityModel.Tokens.Jwt
dotnet add src/Erp.Api package Microsoft.AspNetCore.Authentication.JwtBearer
```

If you're using Central Package Management, `dotnet add` also updates
`Directory.Packages.props` with the package versions.

Next, add the JWT configuration to `appsettings.json`.

```json
{
  "Jwt": {
    "Issuer": "Erp.Api",
    "Audience": "Erp.Client",
    "SigningKey": "",
    "AccessTokenMinutes": 15,
    "RefreshSlidingDays": 7
  }
}
```

> ℹ️ **No `RefreshAbsoluteDays` here.** A refresh token's *sliding* window is a configured span
> (`RefreshSlidingDays`), but its *absolute* hard cap is **not** a number in this file — it's the user's
> `PasswordExpiresUtc` from the database ([Part 3, §10](04c-jwt-refresh-apikey.md)). Password policy lives in
> the database, so the cap does too. A token can never outlive the password it was issued under.

Notice that the signing key is intentionally left blank.

For local development you can temporarily place it in
`appsettings.Development.json`:

```json
{
  "Jwt": {
    "SigningKey": "dev-only-super-secret-key-change-me-32chars-min"
  }
}
```

That key should never leave your machine.

> 🔐 **The signing key is a real secret.** Never commit it to source control.
>
> In later sections we'll cover where secrets belong in development and production (User Secrets,
> environment variables, cloud secret managers), how ASP.NET Core automatically loads them through
> `IConfiguration`, and what to do if the key is ever compromised.

---

### Implementing `IJwtTokenService`

Create `Erp.Shared.Infrastructure/Security/JwtTokenService.cs`.

```csharp
// src/Shared/Erp.Shared.Infrastructure/Security/JwtTokenService.cs
using System.IdentityModel.Tokens.Jwt;   // JwtSecurityToken, JwtSecurityTokenHandler, JwtRegisteredClaimNames
using System.Security.Claims;            // Claim, ClaimTypes
using System.Text;                       // Encoding
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;    // SymmetricSecurityKey, SigningCredentials, SecurityAlgorithms

namespace Erp.Shared.Infrastructure;

public interface IJwtTokenService
{
    string CreateAccessToken(long userId, string username, string role);
}

public sealed class JwtTokenService(IConfiguration config) : IJwtTokenService
{
    public string CreateAccessToken(long userId, string username, string role)
    {
        var jwt = config.GetSection("Jwt");

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwt["SigningKey"]!));

        var creds = new SigningCredentials(
            key,
            SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: jwt["Issuer"],
            audience: jwt["Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(
                int.Parse(jwt["AccessTokenMinutes"]!)),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

---

### Walking through `JwtTokenService`

Although the class creates a JWT, each part has a single responsibility.

#### Step A — Read the JWT configuration

```csharp
var jwt = config.GetSection("Jwt");
```

The service loads all of the JWT settings from the application's configuration.

These include:

- the issuer,
- the audience,
- the signing key,
- and the token lifetime.

The service doesn't care whether those values came from `appsettings.json`,
User Secrets, environment variables, or somewhere else.

It simply asks `IConfiguration` for them.

---

#### Step B — Create the signing key

```csharp
var key = new SymmetricSecurityKey(
    Encoding.UTF8.GetBytes(jwt["SigningKey"]!));
```

The signing key is the application's secret.

It is used to digitally sign every JWT that the server creates.

Anyone can read a JWT, but only someone who knows this key can generate a valid
signature.

---

#### Step C — Create the signing credentials

```csharp
var creds = new SigningCredentials(
    key,
    SecurityAlgorithms.HmacSha256);
```

The signing credentials tell .NET which algorithm should be used when signing
the JWT.

In this course we'll use **HMAC-SHA256**, one of the most common algorithms for
server-generated JWTs.

---

#### Step D — Build the claims

```csharp
var claims = new[]
{
    ...
};
```

These claims become the authenticated user's identity.

Every request carrying this JWT will contain these same claims until the token
expires.

Our token contains four claims.

```csharp
new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())
```

The user's unique identifier.

```csharp
new Claim(ClaimTypes.Name, username)
```

The user's username.

```csharp
new Claim(ClaimTypes.Role, role)
```

The user's role.

We'll use this later when protecting endpoints with role-based authorization.

```csharp
new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
```

A unique identifier for this particular JWT.

Even if the same user logs in twice, each token receives its own unique ID.

---

#### Step E — Create the JWT

```csharp
var token = new JwtSecurityToken(
    ...
);
```

This combines:

- the issuer,
- the audience,
- the claims,
- the expiration time,
- and the digital signature

into a JWT object.

Notice that the expiration time comes from:

```csharp
jwt["AccessTokenMinutes"]
```

We're deliberately making the access token short-lived (15 minutes).

If someone steals it, it becomes useless fairly quickly.

Later in this phase we'll introduce **refresh tokens**, allowing users to obtain
a new access token without logging in again.

---

#### Step F — Convert the JWT into a string

```csharp
return new JwtSecurityTokenHandler().WriteToken(token);
```

Up until this point we've been working with a .NET object.

The final step converts it into the compact string that the client stores and
sends in the `Authorization: Bearer <token>` header.

A finished JWT looks something like this:

```
eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiI0MiIsInJvbGUiOiJTYWxlc1JlcCJ9.3Rk...
```

---

> ℹ️ **A note on the API surface.**
>
> `JwtSecurityToken` and `JwtSecurityTokenHandler` (from
> `System.IdentityModel.Tokens.Jwt`) are the long-standing APIs for creating JWTs
> and remain fully supported.
>
> Newer applications may instead use `JsonWebTokenHandler`
> (`Microsoft.IdentityModel.JsonWebTokens`), which offers better performance and
> is Microsoft's recommended direction going forward.
>
> For this course we use `JwtSecurityTokenHandler` because it is easier to read
> and understand when learning JWT authentication for the first time.

---

## Checkpoint — mint a real token and read it back ✅

There's still no login endpoint (that's Part 2), so we prove the *service* the same way
[Phase 3b](03b-data-access.md) proved the connection factory: with a **throwaway endpoint** you delete
straight afterwards. Drop this in `Program.cs`:

```csharp
// Program.cs — TEMPORARY. Delete after this checkpoint.
// Add: using System.IdentityModel.Tokens.Jwt;  and  using Erp.Shared.Infrastructure;
app.MapGet("/dev/token", (IConfiguration config) =>
{
    var token = new JwtTokenService(config).CreateAccessToken(1, "rep", "SalesRep");

    // Read the token back WITHOUT validating it — proving anyone can read a JWT's payload.
    var decoded = new JwtSecurityTokenHandler().ReadJwtToken(token);
    return Results.Ok(new
    {
        token,
        claims = decoded.Claims.Select(c => $"{c.Type} = {c.Value}")
    });
});
```

We `new` up `JwtTokenService` by hand here on purpose — nothing has registered it with DI yet, and Part 2 is
where it joins the container. `IConfiguration` is the one thing ASP.NET registers for you.

Run the app and call it:

```bash
curl -s http://localhost:5234/dev/token
```

You should get a three-part, dot-separated string and a claim list very much like this:

```
token:  eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ...   ← header . payload . signature

sub = 1
http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name = rep
http://schemas.microsoft.com/ws/2008/06/identity/claims/role = SalesRep
jti = 3d90da04-…
exp = 1783695743
iss = Erp.Api
aud = Erp.Client
```

Three things just proved themselves, and each is worth a moment:

- **The signing key is being read and used.** Blank out `Jwt:SigningKey` in
  `appsettings.Development.json` and call again: you get
  `ArgumentException: IDX10703: Cannot create a 'SymmetricSecurityKey', key length is zero` — and if you
  delete the setting altogether, an `ArgumentNullException` from `Encoding.UTF8.GetBytes(null)`. Ugly, and
  exactly what you want: a missing secret should fail loudly and immediately, not sign tokens with nothing.
  ([Phase 14](14-going-further.md) shows how to make that failure happen at *startup* rather than on the
  first request.) Put the key back before moving on.
- **The payload is readable by anyone.** `ReadJwtToken` never checked a signature; it just base64-decoded the
  middle section. This is the "signed, not encrypted" point made concrete — **never put a secret in a JWT.**
  What the signature buys you is that nobody can *change* those claims without you noticing.
- **Look hard at those claim names.** `sub` came out as `sub`, but `name` and `role` came out as long
  `http://schemas…` URIs. That's not a bug and it's not random: `ClaimTypes.Name` and `ClaimTypes.Role` are
  C# constants whose *values* are those URIs, and we passed them straight into `new Claim(...)`. Remember
  this — in [Part 2](04b-jwt-login.md) it explains why token *validation* has to be told
  `RoleClaimType = ClaimTypes.Role`, and it's the single most common reason role checks mysteriously fail.

**Now delete the `/dev/token` endpoint.** It hands out a valid token for user 1 to anyone who asks; it was a
diagnostic, not a feature.

> ⚠️ **`exp = 1783695743`?** JWT timestamps are **Unix epoch seconds** (seconds since 1970-01-01 UTC), not a
> readable date — that's the spec, not our choice. It should land ~15 minutes ahead of now, matching
> `Jwt:AccessTokenMinutes`.

Both halves of Part 1 now work: passwords hash and verify (§3's checkpoint), and tokens mint and decode. Part
2 assembles them into a login.

---

## ✅ Check Yourself

1. A JWT's payload can be decoded by anyone. Why is that not a security hole — and what must you therefore
   never put in one?
2. Why is PBKDF2 deliberately *slow*, when every other function you write should be fast?
3. Two users pick the password `Password123!`. Why don't they end up with the same stored hash, and what
   attack does that prevent?
4. The stored hash starts with `600000.` — what is that number for, and what would break if we left it out?
5. `IPasswordHasher` and `IJwtTokenService` are both interfaces sitting in front of a single implementation.
   What does the login handler gain from that, given it's the only caller?

---

← Previous: [Phase 3b — Data Access: connections & transactions](03b-data-access.md) · [Tutorial overview](00-START-HERE.md) · Next: [Phase 4 (Part 2 of 5) — Login, Validation & Authorization](04b-jwt-login.md) →