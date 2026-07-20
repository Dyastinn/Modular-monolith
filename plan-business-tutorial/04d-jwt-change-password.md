# Phase 4 (Part 4 of 5) — Changing & Resetting a Password

> **Phase 4 is split into five parts** — do them in order:
> 1. [Passwords & access tokens](04-jwt-auth.md)
> 2. [Login, validation & authorization](04b-jwt-login.md)
> 3. [Refresh tokens & the API-key gate](04c-jwt-refresh-apikey.md)
> 4. **Changing & resetting a password** ← you are here
> 5. [Request flow, end-to-end checkpoint & common mistakes](04e-jwt-wrap-up.md)

> Builds on [Part 3](04c-jwt-refresh-apikey.md). The database already ships `usp_AppUser_ChangePassword`
> ([Phase 3](03-database-schema.md)), which rotates the password hash, the `SecurityStamp`, and the 90-day
> `PasswordExpiresUtc` in **one atomic write**. Everything in this part is a thin, careful slice over that
> proc — plus one design decision that quietly turns *"change my password"* into *"recover an expired one."*

---

## 12. Changing a password — and the reset it unlocks

> **In one sentence:** expose `usp_AppUser_ChangePassword` through a slice that proves the *current* password
> before setting a new one — and make it work **without** a valid access token, because the user who most
> needs it (an expired password) can't get one.

### First, feel the hole

Two loose ends from Part 3 point at the *same* missing endpoint:

- **The expiry dead-end.** Login refuses an expired password — *"Your password has expired. Please reset it
  before signing in"* (§10) — but there's nowhere to reset it, and refresh fails too (its hard cap **is** that
  expiry date). A user whose 90 days lapse is simply locked out. Forever.
- **The compromised-password case.** A logged-in user who suspects their password leaked has no way to rotate
  it and boot every other session — even though the machinery for that (the `SecurityStamp`, §10) already exists.

Both are the same feature: **change the password.** The proc already does the hard part atomically. What's
left is the slice — and one non-obvious call about *who is allowed to call it.*

### The decision: the current password is the credential

Your first instinct is probably *"changing your password should require being logged in"* — i.e.
`.RequireAuthorization()`, like every other write. Follow that instinct and watch it fail the exact case that
matters most:

> ⚠️ **The authenticated-only trap.** If change-password needs a valid access token, the expired-password user
> is *still* stuck: they can't log in, so they can't get a token, so they can't reach the one endpoint that
> would fix their password. Gating change-password behind authentication **re-creates the very dead-end we're
> trying to close.**

The fix is to gate the endpoint on the **current password itself**, not on a JWT. Verify the old password
*inside the handler* (the same PBKDF2 check as login, §3), and the endpoint can safely be `.AllowAnonymous()` —
the credential travels *in the request body*. This one choice makes the endpoint serve **both** jobs at once:
a routine password change *and* the reset that rescues an expired account.

> 🔑 **Anonymous ≠ unprotected.** Three things still guard this door: the **API-key gate** (§11) fires first,
> the caller must present the **correct current password** (a stolen *access token* alone is useless here — it
> never reveals the password), and the deliberately **slow PBKDF2** verify (§3) makes guessing expensive.
> Requiring the old password is *stronger* than a bare access-token check, not weaker.

### The repository write

`usp_AppUser_ChangePassword` is a **write**, so — unlike the two pure reads from [Part 2](04b-jwt-login.md) — it
runs on a **transaction** (the [Phase 3b](03b-data-access.md) pattern). Add one method to `IUserRepository`:

```csharp
public interface IUserRepository
{
    Task<AppUser?> GetByUsernameAsync(string username, CancellationToken ct);
    Task<AppUser?> GetByIdAsync(long id, CancellationToken ct);
    Task ChangePasswordAsync(long userId, string newPasswordHash, IDbTransaction tx, CancellationToken ct);  // NEW — a write
}
```

and implement it in `UserRepository` — the same one-proc-call shape as the refresh-token writes (§10), running
on the caller's transaction so it's atomic:

```csharp
// WRITE — runs on the handler's transaction. The proc rotates PasswordHash + SecurityStamp +
// PasswordExpiresUtc atomically; the new stamp invalidates every outstanding refresh token (a global logout).
public async Task ChangePasswordAsync(long userId, string newPasswordHash, IDbTransaction tx, CancellationToken ct) =>
    await tx.Connection!.ExecuteAsync(new CommandDefinition(
        "[identity].usp_AppUser_ChangePassword",
        new { UserId = userId, NewPasswordHash = newPasswordHash },
        transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));
```

### The handler

It's a host/platform slice like login and refresh, so it lives in `Erp.Api/Auth/ChangePassword/`. Read the
guards top to bottom — they mirror login's, with one **deliberate omission**:

```csharp
// src/Erp.Api/Auth/ChangePassword/ChangePasswordHandler.cs
using System.Security.Authentication;   // AuthenticationException
using Erp.Shared.BuildingBlocks;                // IUserRepository
using Erp.Shared.BuildingBlocks;           // IDbConnectionFactory
using Erp.Shared.Infrastructure;        // IPasswordHasher
using Erp.Shared.BuildingBlocks;             // ICommand, ICommandHandler, Unit
using FluentValidation;

namespace Erp.Api.Auth;

public sealed record ChangePasswordCommand(string Username, string CurrentPassword, string NewPassword)
    : ICommand<Unit>;

public sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.Username).NotEmpty();
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8)
            .WithMessage("New password must be at least 8 characters.");
        RuleFor(x => x.NewPassword).NotEqual(x => x.CurrentPassword)
            .WithMessage("New password must be different from the current one.");
    }
}

public sealed class ChangePasswordHandler(
    IUserRepository users,
    IPasswordHasher hasher,
    IDbConnectionFactory factory) : ICommandHandler<ChangePasswordCommand, Unit>
{
    public async Task<Unit> Handle(ChangePasswordCommand cmd, CancellationToken ct)
    {
        var user = await users.GetByUsernameAsync(cmd.Username, ct);

        // Same generic error whether the user is missing OR the current password is wrong — never reveal which.
        if (user is null || !user.IsActive || !hasher.Verify(cmd.CurrentPassword, user.PasswordHash))
            throw new AuthenticationException("Invalid username or password.");

        // NOTE: deliberately NO PasswordExpiresUtc check here. An expired password is *exactly* the case this
        // endpoint exists to fix, so the current credential must still get the user through (unlike login, §10).

        var newHash = hasher.Hash(cmd.NewPassword);

        // The proc rotates hash + SecurityStamp + PasswordExpiresUtc atomically. The new stamp voids every
        // outstanding refresh token on every device — an instant global logout — so the client logs in afresh after.
        using var conn = factory.Create();
        await conn.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        try
        {
            await users.ChangePasswordAsync(user.Id, newHash, tx, ct);
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

> 🔑 **The logout is a side effect of the proc, not extra code.** Notice the handler never touches the refresh
> token table. It doesn't have to: rotating the `SecurityStamp` (§10) is what invalidates every outstanding
> token — the value they all vouched for simply stops agreeing. One `UPDATE` logs the user out everywhere; the
> handler just changes the password and lets the stamp do its job.

> ❓ **"If they're already logged in, why re-check the old password?"** Two reasons. First, defense-in-depth:
> an access token that leaked (§10's whole worry) shouldn't be enough to *lock a user out of their own account*
> by changing the password from under them — knowing the current password proves it's really them. Second, it's
> *precisely* what lets the endpoint be anonymous and so rescue an expired login. One rule, both payoffs.

> ⚠️ **Where the password *policy* would live.** The validator enforces **shape** — non-empty, a length floor,
> new ≠ current — which is fair game for FluentValidation. A richer *policy* (banned-password lists, complexity
> scoring, "can't reuse your last 5") is closer to a business rule and, in a bigger system, belongs in the
> domain or a dedicated service — not smuggled into the validator. We keep the shape rule here and name the line.

> ❓ **"Then why isn't change-password a domain method?"** Because **identity isn't a domain in this build.**
> `AppUser` is a plain record Dapper reads, living in `Erp.Shared`/`Erp.Api` as platform plumbing every module
> leans on — not a module whose domain owns its rules and enforces them from *inside* an entity. Contrast that
> with `SalesOrder`: the **next phase** gives *it* a rich domain, where a rule like *"can this order be
> approved?"* becomes a method on the entity itself, throwing if the move is illegal. Change-password has no
> such home here, and that's on purpose — the one part of it that's genuinely *behavior*, the password
> **policy**, is exactly the piece left out just above; the rest is shape (the validator), an atomic write (the
> proc), and coordination (this handler). The thing that *looks* like behavior — "rotating the stamp logs you
> out everywhere" — is an **atomicity** invariant (it has to be one write), which is why it sits in the proc and
> not a business `if`. Spend the domain machinery on `SalesOrder`, where the course is actually teaching it, and
> keep auth a thin, honest slice.

> 🚫 **When identity outgrows "plumbing," give it its own module.** The day account security becomes a
> first-class concern — real password policy, lockout, MFA, an audit trail of every credential change — the
> clean move is to carve an **`Identity` module** shaped exactly like `Sales`: its own schema, its own domain,
> and a `user.ChangePassword(newHash)` method that enforces policy and rotates the `SecurityStamp` from inside
> the entity. This slice would then shrink to the same *load → call the domain method → save* shape every
> `Sales` handler has. Nothing here blocks that later: the seam this endpoint already leans on — `IUserRepository`
> and the proc — is right where the module wall would fall.

### The endpoint

```csharp
// src/Erp.Api/Auth/ChangePassword/ChangePasswordEndpoint.cs
using Carter;
using Erp.Shared.BuildingBlocks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Api.Auth;

public sealed record ChangePasswordRequest(string Username, string CurrentPassword, string NewPassword);

public sealed class ChangePasswordEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        // Anonymous like /auth/login: the CURRENT password is the credential, and this must work even when the
        // password has expired — so a valid access token can't be a prerequisite. The X-Api-Key gate still applies.
        app.MapPost("/auth/change-password", async (ChangePasswordRequest body, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new ChangePasswordCommand(body.Username, body.CurrentPassword, body.NewPassword), ct);
            return Results.NoContent();
        })
        .AllowAnonymous();
}
```

### Register it

```csharp
// Program.cs — alongside the other auth slices (they live in the host assembly, so register them explicitly).
builder.Services.AddScoped<IRequestHandler<ChangePasswordCommand, Unit>, ChangePasswordHandler>();
builder.Services.AddScoped<IValidator<ChangePasswordCommand>, ChangePasswordValidator>();
```

Every dependency traces back to something already built: `IUserRepository` (§5, now with one write method),
`IPasswordHasher` (§3), `IDbConnectionFactory` ([Phase 3b](03b-data-access.md)), `Unit` and the mediator
plumbing (§10's logout used the same). No forward references.

### Trace: an expired account climbs back in

The turnstile, walked end to end:

1. `rep`'s 90 days lapse — `PasswordExpiresUtc` is now in the past.
2. `POST /auth/login {rep, <old password>}` → **401** *"Your password has expired. Please reset it before
   signing in."* (§10). The door login points at now exists.
3. `POST /auth/change-password {rep, <old password>, <new password>}` → API key OK → current password verifies
   → the proc runs: new hash, **new `SecurityStamp`**, `PasswordExpiresUtc` pushed 90 days out → **204 No Content**.
4. Any refresh tokens `rep` still held are now dead on their next use (stamp mismatch, §10) — a clean slate.
5. `POST /auth/login {rep, <new password>}` → **200**, a fresh access + refresh pair. Back in.

> ℹ️ Step 2's **401** is your `GlobalExceptionHandler` from [Part 2 §6b](04b-jwt-login.md) mapping the
> `AuthenticationException` that login throws — no new error-handling code in this part either.

> 💡 The same *change, then log in* dance is what a logged-in user does to rotate a **suspected-leaked**
> password — the stamp rotation in step 3 voids every *other* device's **refresh** tokens the instant the change
> commits, so a thief can no longer renew their access, and their foothold ends when the current access token does.

> ⚠️ **"Instant" has a ≤15-minute tail — say so plainly.** Rotating the stamp kills every outstanding *refresh*
> token immediately, but it does **not** reach back and cancel an *access* token that's already been minted.
> Nothing re-checks the `SecurityStamp` on an ordinary authenticated request — that's the whole bargain of
> stateless auth ([Part 1](04-jwt-auth.md)): the token is trusted on signature + `exp` alone, no database hit.
> So a thief holding a stolen **access token** keeps using it for up to its remaining lifetime — **≤15 min**
> (`Jwt:AccessTokenMinutes`) — *after* the victim changes the password; they just can't refresh into a new one
> once it lapses. That residual window is exactly why the access token is kept deliberately short: the shorter
> it is, the smaller the slice of time a change-password (or a logout) can't retroactively close. Erasing the
> tail entirely means re-checking the stamp in the database on *every* request — which hands back the
> statelessness we chose in Part 1. The base build doesn't take that trade; it accepts a ≤15-minute tail and
> keeps the token short. So "boot every other session" and "instant global logout," above, are true for
> **refresh** — read them as *"every session dies within one access-token lifetime,"* not *"the same instant."*

> ⚠️ **What a production reset flow adds — and why we stop here.** This is a *change*-password endpoint: it
> needs the **old** password. That covers the expired-but-remembered case and deliberate rotation, which is
> what unblocks this tutorial. A complete system also needs the **forgot**-password path — a time-boxed,
> single-use token delivered over email/SMS, so someone who *doesn't* know the old password can still recover —
> plus **rate limiting** on this route (it runs a slow hash on an anonymous request) and often an **admin
> reset**. Those need a notification channel and a token table this course doesn't build; they're named on the
> [Phase 13 checklist](13-wrap-up-checklist.md). The shape here — *verify a credential, call the proc in a
> transaction* — is the same either way.

> 🧭 **From WebForms.** This is your old **Change Password** page (the `ChangePassword` control) minus the
> server-rendered form: the same *old-password + new-password* contract, now a JSON endpoint. The "Forgot
> Password?" wizard that emailed a reset link is the piece we're explicitly leaving for later.

---

## Checkpoint — change a password, and watch every session die ✅

Two claims to prove, and the second is the interesting one: that the endpoint works **without a token**, and
that a password change silently voids every refresh token the user holds — *without* the handler ever touching
the refresh-token table.

```bash
BASE=http://localhost:5234
KEY=web-live-abc123
```

**1) Take a refresh token minted under the *current* security stamp.**

```bash
curl -s -X POST "$BASE/auth/login" -H "X-Api-Key: $KEY" \
     -H "Content-Type: application/json" \
     -d '{"username":"rep","password":"Password123!"}'
# save the refreshToken from the response as REFRESH
```

**2) Change the password — note there is no `Authorization` header anywhere.**

```bash
curl -i -X POST "$BASE/auth/change-password" -H "X-Api-Key: $KEY" \
     -H "Content-Type: application/json" \
     -d '{"username":"rep","currentPassword":"Password123!","newPassword":"Temp_Pass_123!"}'
# → 204 No Content
```

Anonymous, and it worked. The **current password** was the credential; the API key was the only header
standing between this call and the open internet.

**3) The refresh token from step 1 is now dead.**

```bash
curl -i -X POST "$BASE/auth/refresh" -H "X-Api-Key: $KEY" \
     -H "Content-Type: application/json" -d "{\"refreshToken\":\"$REFRESH\"}"
# → "Credentials changed. Please log in again."
```

Sit with that for a second. `ChangePasswordHandler` never opened the `RefreshToken` table. It changed one
row in `AppUser` — and because `usp_AppUser_ChangePassword` rotated `SecurityStamp`, the stamp the token
recorded at mint time no longer agrees with the user's. **That disagreement is the logout**, and it applies
to every device at once, with no sweep and no bookkeeping.

**4) The old password is gone; the new one works.**

```bash
curl -i -X POST "$BASE/auth/login" -H "X-Api-Key: $KEY" -H "Content-Type: application/json" \
     -d '{"username":"rep","password":"Password123!"}'
# → "Invalid username or password."   (the same generic message as an unknown user)

curl -i -X POST "$BASE/auth/login" -H "X-Api-Key: $KEY" -H "Content-Type: application/json" \
     -d '{"username":"rep","password":"Temp_Pass_123!"}'
# → 200 OK   a fresh access + refresh pair
```

**5) Put `rep` back, because every later phase assumes `Password123!`.**

```bash
curl -i -X POST "$BASE/auth/change-password" -H "X-Api-Key: $KEY" \
     -H "Content-Type: application/json" \
     -d '{"username":"rep","currentPassword":"Temp_Pass_123!","newPassword":"Password123!"}'
# → 204 No Content
```

> ⚠️ **Change it *back with the endpoint* — don't try to "just re-run the seed."** The seed inserts users; it
> doesn't update them. Re-running it either fails on the `UNIQUE` constraint or (if you added the
> `IF NOT EXISTS` guards) does nothing at all. The row already exists, and the only sanctioned way to change a
> password is the proc you just built. Two round-trips through your own endpoint is the correct answer, and
> it's a small taste of why "just restore from the script" is rarely the fix in a real system.
>
> Notice the round-trip also pushed `rep`'s `PasswordExpiresUtc` **90 days into the future**, which is exactly
> how you'd rescue an account whose password had lapsed.

### You're done when

| # | You proved | Expected |
|---|---|---|
| 1 | Change-password needs **no** access token | anonymous call → **204 No Content** |
| 2 | The current password is the credential | wrong `currentPassword` → the generic *Invalid username or password.* |
| 3 | The stamp rotation *is* the logout | a refresh token minted before the change → *Credentials changed.* |
| 4 | The new password is live, the old one isn't | old → generic failure; new → **200** with a fresh pair |
| 5 | You restored `rep` to `Password123!` | **204**, and login with `Password123!` → **200** |

> ⚠️ The failures above are thrown `AuthenticationException`s, so they arrive as **401**s via the
> [Part 2 §6b](04b-jwt-login.md) handler. But the validator rules (`MinimumLength(8)`, new ≠ current) still
> **don't fire yet** — writing a validator isn't enough on its own; the `ValidationBehavior` that actually
> *runs* it is a pipeline behavior, and that lands in [Phase 11](11-cross-cutting.md). Try a 3-character new
> password today and it'll be accepted.

> 💡 **Want to see the expiry rescue for real?** Push the date into the past and watch the dead-end open, then
> close: `UPDATE [identity].AppUser SET PasswordExpiresUtc = DATEADD(day,-1,SYSUTCDATETIME()) WHERE Username='rep';`
> Now login says *"Your password has expired"* and **refresh fails too** — but change-password still lets `rep`
> back in, and pushes the expiry 90 days out. That's the whole reason this endpoint is anonymous, demonstrated
> in three commands.

---

← Previous: [Phase 4 (Part 3 of 5) — Refresh Tokens & the API-key Gate](04c-jwt-refresh-apikey.md) · [Tutorial overview](00-START-HERE.md) · Next: [Phase 4 (Part 5 of 5) — Request Flow, Checkpoint & Common Mistakes](04e-jwt-wrap-up.md) →
