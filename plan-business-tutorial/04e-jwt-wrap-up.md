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
     before anything else runs (yes, even /auth/login). The only exceptions are CORS preflight (OPTIONS),
     /health and /swagger. Below, assume the key is present and valid.
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

## 14. Checkpoint — prove the whole gate, end to end ✅

This is a security phase, so the checkpoint is thorough on purpose: it walks **every** door you built — the
API-key gate, login, the protected-endpoint token check, refresh + rotation, reuse detection, logout, and
change-password's global logout — and tells you the exact status code each one must return. Do them **in
order**; several steps depend on a token captured by an earlier one.

### Before you start

Run the app, then note two things and set them once so you can paste the rest verbatim:

```bash
# 1) Your app's port — read it from the console banner or Properties/launchSettings.json.
# 2) An API key you configured in §11 (User Secrets / appsettings). We use the sample "web" key here.
BASE=http://localhost:5001         # ← replace 5001 with YOUR port
KEY=web-live-abc123                # ← replace with a key you actually configured
```

Three prerequisites, or the steps below won't behave:

- **Add `.RequireAuthorization()` to the `/sales/ping` endpoint** from [Phase 2](02-mediator-own-vs-free.md) —
  that's the protected endpoint Step 2 leans on.
- **Every command carries `-H "X-Api-Key: $KEY"`** — the §11 gate runs *first*, so a request without it never
  even reaches login (that's Step 0's whole point).
- The commands capture tokens with **`jq`**. If you don't have it, drop the `jq` lines and just copy the
  `accessToken` / `refreshToken` string out of the printed JSON into the next command by hand.
  *(On Windows, run these in Git Bash or WSL, or use the Swagger note at the end.)*

The seed users from [Phase 3](03-database-schema.md), for reference:

| Username | Password | Role |
|---|---|---|
| `rep` | `Password123!` | `SalesRep` |
| `manager` | `Password123!` | `SalesManager` |
| `admin` | `Password123!` | `Admin` |

---

### Step 0 — the API-key gate fires *before* anything (§11)

Prove the outer gate guards even the anonymous login door. Send login with **no** API key:

```bash
curl -i -X POST "$BASE/auth/login" \
     -H "Content-Type: application/json" \
     -d '{"username":"rep","password":"Password123!"}'
# → 401 Unauthorized   { "error": "Missing or invalid API key." }
#   Note: this 401 is the API-KEY gate, not the login logic — we never reached LoginHandler.
```

A wrong key is rejected the same way; only a configured key gets through:

```bash
curl -i -X POST "$BASE/auth/login" -H "X-Api-Key: not-a-real-key" \
     -H "Content-Type: application/json" \
     -d '{"username":"rep","password":"Password123!"}'
# → 401 Unauthorized   { "error": "Missing or invalid API key." }
```

### Step 1 — log in (and confirm the generic error)

First the failure path — a wrong password must return the **same** message as an unknown user (§5), never
"no such user" vs "bad password":

```bash
curl -i -X POST "$BASE/auth/login" -H "X-Api-Key: $KEY" \
     -H "Content-Type: application/json" \
     -d '{"username":"rep","password":"WRONG"}'
# → 401 Unauthorized   "Invalid username or password."
```

Now the happy path. Capture the pair into shell variables so later steps can reuse them:

```bash
resp=$(curl -s -X POST "$BASE/auth/login" -H "X-Api-Key: $KEY" \
       -H "Content-Type: application/json" \
       -d '{"username":"rep","password":"Password123!"}')
echo "$resp"
# → { "accessToken": "eyJ...", "refreshToken": "...", "username": "rep", "role": "SalesRep" }

ACCESS=$(echo "$resp"  | jq -r .accessToken)
REFRESH=$(echo "$resp" | jq -r .refreshToken)
```

### Step 2 — the access token actually *gates* the protected endpoint

Three calls to the **same** endpoint, all carrying the API key (so we isolate the *token* check, not the gate
from Step 0):

```bash
# No bearer token → 401 (authenticated? no).
curl -i "$BASE/sales/ping?name=world" -H "X-Api-Key: $KEY"
# → 401 Unauthorized

# A garbage/tampered token → 401 (signature fails).
curl -i "$BASE/sales/ping?name=world" -H "X-Api-Key: $KEY" -H "Authorization: Bearer not.a.jwt"
# → 401 Unauthorized

# The real token → 200.
curl -i "$BASE/sales/ping?name=world" -H "X-Api-Key: $KEY" -H "Authorization: Bearer $ACCESS"
# → 200 OK   "pong: world"
```

> 🔑 Role-based **403** (a valid token whose *role* is wrong) isn't demonstrable yet — `/sales/ping` only
> requires *authentication*, not a role. You'll see the 401-vs-403 split for real in [Phase 6](06-create-order.md)+,
> where the sales-order endpoints add `RequireRole(...)`.

### Step 3 — refresh rotates to a brand-new pair (§10)

Trade the refresh token for a fresh pair. The new tokens must be **different** strings — that's rotation:

```bash
resp=$(curl -s -X POST "$BASE/auth/refresh" -H "X-Api-Key: $KEY" \
       -H "Content-Type: application/json" \
       -d "{\"refreshToken\":\"$REFRESH\"}")
echo "$resp"
# → a NEW { "accessToken": "...", "refreshToken": "..." } — both different from Step 1.

OLD_REFRESH=$REFRESH                              # remember the now-SPENT token for Step 4
REFRESH=$(echo "$resp" | jq -r .refreshToken)     # the current live token
ACCESS=$(echo  "$resp" | jq -r .accessToken)
```

### Step 4 — reuse detection burns the whole family (§10)

Replay the token you *just spent* in Step 3. Because it's single-use, presenting it again means two parties
hold copies — the server revokes the **entire family**:

```bash
curl -i -X POST "$BASE/auth/refresh" -H "X-Api-Key: $KEY" \
     -H "Content-Type: application/json" \
     -d "{\"refreshToken\":\"$OLD_REFRESH\"}"
# → 401 Unauthorized   "Refresh token reuse detected — session revoked. Please log in again."
```

The trap also kills the *current* token you got in Step 3 — the family is gone, so even the good token now
fails:

```bash
curl -i -X POST "$BASE/auth/refresh" -H "X-Api-Key: $KEY" \
     -H "Content-Type: application/json" \
     -d "{\"refreshToken\":\"$REFRESH\"}"
# → 401 Unauthorized   (family revoked — the honest client must log in again)
```

### Step 5 — logout revokes a session

Log in fresh (Step 4 nuked the family), then log out and watch that refresh token die:

```bash
resp=$(curl -s -X POST "$BASE/auth/login" -H "X-Api-Key: $KEY" \
       -H "Content-Type: application/json" \
       -d '{"username":"rep","password":"Password123!"}')
REFRESH=$(echo "$resp" | jq -r .refreshToken)
ACCESS=$(echo  "$resp" | jq -r .accessToken)

curl -i -X POST "$BASE/auth/logout" -H "X-Api-Key: $KEY" \
     -H "Content-Type: application/json" \
     -d "{\"refreshToken\":\"$REFRESH\"}"
# → 204 No Content

curl -i -X POST "$BASE/auth/refresh" -H "X-Api-Key: $KEY" \
     -H "Content-Type: application/json" \
     -d "{\"refreshToken\":\"$REFRESH\"}"
# → 401 Unauthorized   (revoked)
```

> ℹ️ `POST /auth/logout-all` is the "log me out everywhere" variant — it acts on the authenticated caller, so
> unlike `/logout` it needs a **valid access token** and no body:
> `curl -i -X POST "$BASE/auth/logout-all" -H "X-Api-Key: $KEY" -H "Authorization: Bearer $ACCESS"` → **204**.

### Step 6 — change-password logs the user out everywhere (Part 4 + §10)

Log in once more to mint a token *under the old security stamp*, change the password, then watch that token
go void — this proves the `SecurityStamp` rotation, not a token sweep, is doing the logout:

```bash
resp=$(curl -s -X POST "$BASE/auth/login" -H "X-Api-Key: $KEY" \
       -H "Content-Type: application/json" \
       -d '{"username":"rep","password":"Password123!"}')
REFRESH=$(echo "$resp" | jq -r .refreshToken)

# Change the password — anonymous, gated on the CURRENT password (no access token needed).
curl -i -X POST "$BASE/auth/change-password" -H "X-Api-Key: $KEY" \
     -H "Content-Type: application/json" \
     -d '{"username":"rep","currentPassword":"Password123!","newPassword":"NewPassword456!"}'
# → 204 No Content

# The refresh token above was minted under the OLD stamp — the change rotated it, so now it's void:
curl -i -X POST "$BASE/auth/refresh" -H "X-Api-Key: $KEY" \
     -H "Content-Type: application/json" \
     -d "{\"refreshToken\":\"$REFRESH\"}"
# → 401 Unauthorized   "Credentials changed. Please log in again."

# The old password no longer works…
curl -i -X POST "$BASE/auth/login" -H "X-Api-Key: $KEY" \
     -H "Content-Type: application/json" \
     -d '{"username":"rep","password":"Password123!"}'
# → 401 Unauthorized   "Invalid username or password."

# …and the NEW one does.
curl -i -X POST "$BASE/auth/login" -H "X-Api-Key: $KEY" \
     -H "Content-Type: application/json" \
     -d '{"username":"rep","password":"NewPassword456!"}'
# → 200 OK   a fresh access + refresh pair
```

---

### You're done when

| # | You proved | Expected |
|---|---|---|
| 0 | The API-key gate fires before login | missing/bad key → **401** `Missing or invalid API key.` |
| 1 | Login is generic on failure, issues a pair on success | wrong password → **401**; valid → **200** + `accessToken` + `refreshToken` |
| 2 | The access token gates a protected endpoint | no/garbage token → **401**; valid → **200** `pong: world` |
| 3 | Refresh **rotates** | **200** with a *different* token pair |
| 4 | Reuse detection burns the family | replayed spent token → **401**; the rotated token dies too |
| 5 | Logout revokes | logout → **204**; that token then → **401** |
| 6 | Change-password is a global logout | change → **204**; old refresh → **401** (stamp); new password → **200** |

If every row matches, **Phase 4 is done and verifiable.** From Phase 6 on, every sales-order endpoint sits
behind exactly this gate (API key → authenticate → authorize).

> ℹ️ **Using Swagger instead of curl?** The §11 gate will 401 the Swagger UI too unless you allow-listed
> `/swagger` (see the ℹ️ note in [Part 3, §11](04c-jwt-refresh-apikey.md)) or send `X-Api-Key` via Swagger's
> header UI. Once through, paste an access token into the **Authorize** box to call protected endpoints from the
> browser.

> ℹ️ You just changed `rep`'s password to `NewPassword456!`. Either **re-seed** (re-run the Phase 3 seed) or
> **remember the new password** for the rest of the run, since later checkpoints assume `Password123!`.

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
