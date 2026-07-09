# Phase 4 (Part 5 of 5) — Request Flow, Checkpoint & Common Mistakes

> **Phase 4 is split into five parts** — do them in order:
> 1. [Passwords & access tokens](04-jwt-auth.md)
> 2. [Login, validation & authorization](04b-jwt-login.md)
> 3. [Refresh tokens & the API-key gate](04c-jwt-refresh-apikey.md)
> 4. [Changing & resetting a password](04d-jwt-change-password.md)
> 5. **Request flow, checkpoint & common mistakes** ← you are here

> Builds on Parts [1](04-jwt-auth.md)–[4](04d-jwt-change-password.md). Everything is now in place: passwords, tokens, login, validation, `ICurrentUser`, authorization, refresh tokens, the API-key gate, and change/reset password. This part traces how they combine on a real request, gives you a checkpoint to prove it works, and collects the pitfalls.

---

## 13. How a full authenticated request flows

```
0. EVERY request carries  X-Api-Key: <key>  → ApiKeyMiddleware checks it FIRST; unknown client → 401
     before anything else runs (yes, even /auth/login). Below, assume it's present and valid.
1. POST /auth/login {rep, Password123!}  → LoginHandler verifies hash → returns { accessToken, refreshToken }
2. Client stores both: the access token for requests, the refresh token for later.
3. POST /sales-orders   X-Api-Key: <key>   Authorization: Bearer <accessToken>
     → ApiKeyMiddleware OKs the client → UseAuthentication validates signature+expiry → HttpContext.User claims
     → UseAuthorization checks the endpoint's role requirement (here: SalesRep or Manager — ok)
     → endpoint calls sender.Send(command)
     → handler injects ICurrentUser → reads UserId to stamp CreatedByUserId
4. ...15 minutes pass; the access token expires → the next request gets 401.
5. POST /auth/refresh {refreshToken}  → RefreshHandler reloads the user, checks the security stamp + the
     password-expiry cap, then rotates → returns a new { accessToken, refreshToken }
     → client swaps in the new pair and retries. No re-login.
6. (If the password had been changed anywhere) the user's SecurityStamp rotated → step 5's stamp check fails
     → refresh is refused on every device at once → the user must log in again with the new password.
7. POST /auth/change-password {user, oldPassword, newPassword}  → verifies the CURRENT password (NO token
     needed — the old password is the credential) → the proc rotates hash + SecurityStamp + expiry → 204.
     This is also the ONLY way back in once a password has expired: login refuses it and points here (Part 4).
```

## 14. Checkpoint — log in, get a token, prove it protects ✅

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

Trade the refresh token for a fresh pair:

```bash
curl -X POST http://localhost:5xxx/auth/refresh -H "Content-Type: application/json" \
     -d '{"refreshToken":"<the refresh token above>"}'                 # → a NEW accessToken + refreshToken
```

Finally, prove change-password (Part 4) works **and** logs the user out everywhere. Change `rep`'s password,
then watch the *old* refresh token stop working:

```bash
curl -X POST http://localhost:5xxx/auth/change-password -H "Content-Type: application/json" \
     -d '{"username":"rep","currentPassword":"Password123!","newPassword":"NewPassword456!"}'   # → 204 No Content

# The refresh token from the login above was minted under the OLD security stamp — now it's void:
curl -X POST http://localhost:5xxx/auth/refresh -H "Content-Type: application/json" \
     -d '{"refreshToken":"<the refresh token from before>"}'          # → 401 (stamp changed / credentials changed)

curl -X POST http://localhost:5xxx/auth/login -H "Content-Type: application/json" \
     -d '{"username":"rep","password":"NewPassword456!"}'             # → 200 with the NEW password
```

If a bad/missing token gives 401, a valid one gives 200, refresh returns a *different* token pair, and a
password change both returns 204 **and** kills the old refresh token, **Phase 4 is done and verifiable.** From
Phase 6 on, every sales-order endpoint sits behind exactly this gate. (You can also paste the access token into
Swagger's **Authorize** box and call endpoints from the browser.)

> ℹ️ Re-seed or change it back afterward if other checkpoints expect `Password123!` — or just remember `rep`'s
> new password for the rest of the run.

## 15. Common mistakes & best practices

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
  tokens (§10).
- ❌ **Refreshing without rotating** (handing back the same refresh token). ✅ Rotate every time — that's
  what makes reuse detectable.
- ❌ **A long-lived access token "to save round-trips."** ✅ Keep the access token short (15 min) and lean on
  the refresh token; a stolen short-lived token expires before it's useful.
- ✅ **Best practice: reload the user on refresh.** It's your chance to honor a role change or a deactivation
  since login, instead of trusting stale claims for days — and to compare the **security stamp** so a password
  change instantly voids every outstanding refresh token (§10).
- ❌ **Configuring the refresh token's absolute cap as a fixed span** (`Jwt:RefreshAbsoluteDays`) when a
  database-driven password policy already governs how long credentials live. ✅ Cap it at the user's
  `PasswordExpiresUtc`, read from the DB — one policy, one home; a token can't outlive its password (§10).
- ❌ **Relying on a "revoke all tokens" sweep to log a user out after a password change** (a token mid-flight
  can slip past it). ✅ Rotate a `SecurityStamp` the tokens are compared against — the value they vouched for
  simply stops agreeing, no hunt required (§10).
- ❌ **Treating a browser-shipped API key as a real secret, or letting it replace user auth.** ✅ Use it as a
  coarse *outer* gate — block anonymous traffic off the login door, tell clients apart, rate-limit/revoke one.
  Keep JWT + the §9 checks as the real security. An API key is a genuine secret only for
  server-to-server/partner clients (§11).
- ❌ **Putting the API key gate *after* `UseAuthentication`.** ✅ Wire it *first* — reject unknown clients
  before you spend cycles validating tokens, and so it can guard the anonymous `/auth/login` too.
- ❌ **Gating change-password behind `.RequireAuthorization()`.** It re-creates the expiry dead-end — the
  expired-password user can't get a token, so they can't reach the endpoint that would fix it. ✅ Make it
  anonymous and verify the **current password** in the handler; that's both the credential *and* the escape
  hatch (Part 4).
- ❌ **Hand-writing a "log out everywhere" loop after a password change.** ✅ Let the `usp_AppUser_ChangePassword`
  proc rotate the `SecurityStamp`; every outstanding refresh token voids itself on next use — no loop (Part 4/§10).

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
7. A user changes their password. Which column makes their existing refresh tokens stop working on the *next*
   refresh — and why is comparing that value better than running a "revoke all their tokens" sweep?
8. Why is a refresh token's absolute expiry taken from the user's `PasswordExpiresUtc` instead of a
   `Jwt:RefreshAbsoluteDays` setting — and what would go wrong if both existed?
9. What question does an API key answer that a JWT doesn't — why must its gate run *before* authentication,
   and why can't a browser SPA treat its API key as a true secret?
10. Why is `/auth/change-password` **anonymous** rather than `.RequireAuthorization()` — and why does verifying
    the *current* password make that safe (arguably safer than a token check)?

> 👉 Next: [Phase 5 — The SalesOrder domain model and its business rules](05-domain-model.md).
