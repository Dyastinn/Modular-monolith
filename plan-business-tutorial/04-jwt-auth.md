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

- A `POST /auth/login` endpoint: give it a username + password, get back a **short-lived access token and a
  refresh token**.
- Token *validation* wired into ASP.NET Core so protected endpoints reject anyone without a valid token.
- **Refresh tokens** (§10): `POST /auth/refresh` trades a refresh token for a fresh access token, **rotating**
  it each time and **detecting replay** of a stolen one; plus `POST /auth/logout` and `POST /auth/logout-all`
  to revoke sessions.
- **Role-based authorization** so specific endpoints require specific roles (Manager to approve).
- An `ICurrentUser` abstraction so our handlers can ask "who is calling, and what's their role?" without
  depending on ASP.NET directly.
- **An API key gate** (§11) so only your known *client apps* reach the API at all — an outer, app-level gate
  that fires *before* user authentication (it's the one gate that can sit in front of `/auth/login` itself).

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
>   exactly why access tokens are short-lived, and why §10's refresh tokens (which the server *does* track, by
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

A password must **never** be stored directly in the database. If someone steals your database, every user's
password would be exposed.

Instead, we store a **one-way hash**. A hash lets us verify a password, but it cannot be reversed back into
the original password.

.NET already provides a secure password hashing implementation called `Rfc2898DeriveBytes`, which implements
the **PBKDF2 (Password-Based Key Derivation Function 2)** algorithm defined by **RFC 2898**.

Unlike a normal hash such as SHA-256, PBKDF2 intentionally performs hundreds of thousands of hashing rounds,
making brute-force attacks much slower.

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

        // Split the stored value back into:
        // iterations, salt and hash.
        var parts = storedHash.Split('.', 3);

        if (parts.Length != 3)
            return false;

        // Explicit version:
        // int iterations = 0;
        // bool parsed = int.TryParse(parts[0], out iterations);
        // if (!parsed)
        //     return false;
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

### Why do we use a salt?

A **salt** is a random value added to every password before hashing.

Without a salt:

```
Password123 → Hash A
Password123 → Hash A
```

With a random salt:

```
Password123 + Salt A → Hash X
Password123 + Salt B → Hash Y
```

Even identical passwords produce different hashes. This prevents attackers from using precomputed lookup tables (rainbow tables). The salt is **not secret**, so it is stored together with the hash.

### Why store the iteration count?

Notice we store:

```
iterations.salt.hash
```

instead of just the hash.

This allows us to increase the iteration count in the future as hardware becomes faster. Existing passwords still verify correctly because we read the original iteration count from the stored value.

### Why use `FixedTimeEquals()`?

A normal comparison (`==`) may stop as soon as it finds the first difference, creating tiny timing differences that attackers could measure.

`CryptographicOperations.FixedTimeEquals()` always compares every byte, making the comparison take approximately the same amount of time regardless of where the difference occurs.

To generate the seed hashes from [Phase 3](03-database-schema.md), run `Hash("Password123!")` once and
paste the results into `003_seed.sql`.

> 📎 Don't want to boot the app just to seed three hashes? The companion
> [`04a-password-hashing`](04a-password-hashing/README.md) folder has a standard-library Python port of
> this exact `Hash` method — run `python hash_password.py --seed` and paste the output straight over the
> placeholders in [`Identity/003_seed.sql`](03a-database-queries/Identity/003_seed.sql). Same algorithm,
> same parameters, so the hashes it writes verify against the C# `Verify` above.

## 4. Step 2 — Creating the access token (`IJwtTokenService`)

> 📦 **Packages & config for this phase — add these first.** JWT is where these are first used, so (per the
> "install only when needed" rule from [Phase 1](01-solution-setup.md)) this is where we add them:
>
> ```bash
> dotnet add src/Shared/Erp.Shared.Infrastructure package System.IdentityModel.Tokens.Jwt
> dotnet add src/Erp.Api package Microsoft.AspNetCore.Authentication.JwtBearer
> ```
>
> With Central Package Management on, each `dotnet add` also writes a `<PackageVersion>` line into
> `Directory.Packages.props` (known-good for .NET 10: `System.IdentityModel.Tokens.Jwt` `8.19.1`,
> `Microsoft.AspNetCore.Authentication.JwtBearer` `10.0.9`).
>
> Now add the **`Jwt` settings** these read. Put the **non-secret configuration** in `appsettings.json`:
>
> ```json
> {
>   "Jwt": {
>     "Issuer": "Erp.Api",
>     "Audience": "Erp.Client",
>     "SigningKey": "",
>     "AccessTokenMinutes": 15,   // short: a stolen access token dies fast (§10's refresh tokens make this practical)
>     "RefreshSlidingDays": 7,    // inactivity timeout — resets on each use
>     "RefreshAbsoluteDays": 30   // hard cap — an active session still re-logs-in eventually
>   }
> }
> ```
>
> For **local development**, you can place a temporary key in `appsettings.Development.json` while learning:
>
> ```json
> {
>   "Jwt": {
>     "SigningKey": "dev-only-super-secret-key-change-me-32chars-min"
>   }
> }
> ```
>
> That development key should never leave your machine.
>
> 🔐 **The `SigningKey` is a real secret** — it must **never** be committed to source control. The dev key
> above is fine while you are learning locally. **Where it belongs in every other environment** (User
> Secrets, environment variables, cloud secret managers), **how to generate one**, and **how to rotate it if
> it leaks** are all covered in **[Part 2, §7](04b-jwt-login.md)** — right where the app *reads* the key to
> validate tokens, so the whole "where configuration comes from" story lives in one place.

This mints the **access token** — the short-lived one (15 minutes, from the `Jwt:AccessTokenMinutes` setting
above) that the client sends on every request. It's deliberately brief because a stolen access token then
expires fast; §10's refresh token is what keeps the user from re-typing a password every 15 minutes.
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
>   "role": ["SalesRep", "SalesManager"],
>   "permissions": ["orders:read", "orders:approve", "customers:read"],
>   "tenant": "acme-corp",
>   "region": "APAC"
> }
> ```
>
> With **permissions** in the token, an endpoint can demand a *capability* (`orders:approve`) instead of a
> role — so you can change which roles hold which powers in one place, without editing every endpoint's role
> list. A `tenant` or `region` claim lets a handler scope queries to the caller's slice of the data. The
> tradeoff: every extra claim makes the token **bigger** (it rides on *every* request) and **staler** (claims
> only refresh when a new token is minted — §10 — so a permission revoked at 10:00 lingers until the current
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
>       "views":   ["orders", "customers"],
>       "buttons": ["order:create", "order:cancel"]
>     },
>     {
>       "moduleId": "INVENTORY",
>       "views":   ["stock"],
>       "buttons": []
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
> *UI*; the **server still enforces the real check** on the endpoint or in the handler (§9). Never let a
> hidden button be your only guard.
>
> ⚠️ **And watch the size — this is where the token tradeoff bites hardest.** A full module/view/button matrix
> can be *hundreds* of entries, and it rides on **every** request. The usual fix: keep only a compact **role or
> permission summary** in the token (what the server authorizes against, §9), and load the detailed UI matrix
> **once** after login from an endpoint like `GET /me/permissions`. Rule of thumb: put in the token what the
> *server* needs to authorize each call; fetch separately what the *UI* needs to render. Don't turn the
> wristband into a filing cabinet.

> ℹ️ **A note on the API surface.** `JwtSecurityToken`/`JwtSecurityTokenHandler` (from
> `System.IdentityModel.Tokens.Jwt`) is the long-standing, fully-supported way to create tokens and is
> perfectly fine here. The *newer* recommended type is `JsonWebTokenHandler` (from
> `Microsoft.IdentityModel.JsonWebTokens`), which is faster and the direction Microsoft is steering. If you
> start a fresh project, prefer it; we use the classic handler because it reads more simply for a first
> encounter. Either way, the `MapInboundClaims = false` fix below still applies.

---

> 👉 Next: [Phase 4 (Part 2 of 4) — Login, Validation & Authorization](04b-jwt-login.md), where we turn the password hasher and token service into a working `POST /auth/login`, wire token *validation* into the app, and add role-based authorization.
