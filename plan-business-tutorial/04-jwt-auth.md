# Phase 4 (Part 1 of 4) — Passwords & Access Tokens

> **Phase 4 is split into four parts** — do them in order:
> 1. **Passwords & access tokens** ← you are here
> 2. [Login, validation & authorization](04b-jwt-login.md)
> 3. [Refresh tokens & the API-key gate](04c-jwt-refresh-apikey.md)
> 4. [Request flow, checkpoint & common mistakes](04d-jwt-wrap-up.md)


> This is a **new topic** not covered in the earlier plans, so we go slowly and assume nothing. By the
> end you can log in, receive a token, refresh it safely, and protect endpoints — including "only a Manager
> may approve."

---

> ⏱️ **Time for all four parts:** ~2.5–3 hr (this Part 1 is ~45 min). This is a **new topic** — hashing,
> tokens, claims, middleware order, the `MapInboundClaims` gotcha, and refresh-token rotation with reuse
> detection ([Part 3, §10](04c-jwt-refresh-apikey.md)). Go slow; rushing auth is how subtle security bugs
> sneak in.

## 1. What we're building

By the end of this phase, you'll have a complete authentication system for your API.

You'll build:

- A `POST /auth/login` endpoint where a user submits a username and password and receives a **short-lived access token**.
- Token **validation** wired into ASP.NET Core so protected endpoints automatically reject invalid or expired tokens.
- **Role-based authorization** so specific endpoints can require particular roles, such as allowing only Managers to approve orders.
- **Refresh tokens** (§10), allowing users to obtain a new access token without logging in again. We'll also implement **rotation**, **reuse detection**, and logout endpoints that revoke active sessions.
- **An API key gate** (§11) that sits in front of the API as an outer layer of protection, ensuring only known client applications can access the API—even before user authentication occurs.

By the end of Part 4, you'll understand the complete authentication flow—from entering a username and password, to issuing tokens, validating requests, authorizing users, refreshing sessions, and protecting the API from anonymous clients.

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

To generate the seed hashes from [Phase 3](03-database-schema.md), run `Hash("Password123!")` once and paste
the results into `003_seed.sql`.

> 📎 Don't want to boot the app just to generate the seed hashes? The companion
> `04a-password-hashing` folder contains a Python implementation of the same algorithm. Run
> `python hash_password.py --seed`, then copy the generated hashes into
> `Identity/003_seed.sql`. It uses the same PBKDF2 algorithm and parameters, so the generated hashes verify
> correctly with the C# `Verify()` method above.
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

> 👉 Next: **Phase 4 (Part 2 of 4) — Login, Validation & Authorization**, where we'll build a complete login
> feature, validate JWTs on incoming requests, and use them to authenticate and authorize users.