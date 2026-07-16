# Every Checkpoint, in PowerShell (Windows Companion)

> 👨‍🏫 **From your senior:** the phases write their checkpoints in **bash + curl**, because that's the lingua
> franca of HTTP examples — you'll meet those exact commands in every API doc you ever read, so it's worth
> being able to read them. But if you're on Windows and would rather not leave PowerShell, this file is the
> same checkpoints, translated. Nothing here is extra work: it's the *same requests*, same order, same
> expected results.

**You have three ways to drive the checkpoints. All are legitimate — pick one and go:**

| Option | Cost | Why you'd pick it |
|---|---|---|
| **Git Bash** | already installed with [Git for Windows](https://git-scm.com/download/win) | The phase commands work **verbatim** — paste and run. Right-click a folder → *Open Git Bash here*. |
| **PowerShell** (this file) | none | You live here already and want native tooling. |
| **Swagger / the test console** | none | Click instead of type. `/swagger` has an **Authorize** button for both headers. |

> ⚠️ **Don't paste the phases' bash into PowerShell.** It looks like it should work and won't:
> `BASE=http://…` isn't valid PowerShell, `$(...)` means something different, and in **Windows PowerShell
> 5.1** `curl` is an *alias for `Invoke-WebRequest`* — so `curl -i -X POST …` fails with baffling parameter
> errors rather than doing what curl does. That single alias has cost more people more hours than any other
> thing on this page.

---

## The setup — run this once per session

Two variables and one helper. Paste this whole block into PowerShell before any checkpoint below.

```powershell
$Base = 'http://localhost:5234'      # ← your port, from the console banner
$Key  = 'web-live-abc123'            # ← an API key you configured (Phase 4c §11); ignore until then

function Invoke-Api {
    param(
        [Parameter(Mandatory)][string]$Path,
        [string]$Method = 'GET',
        $Body,                        # a hashtable — it gets converted to JSON for you
        [string]$Token                # the access token, when a route needs one
    )

    $headers = @{ 'X-Api-Key' = $Key }
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }

    # -UseBasicParsing is REQUIRED on Windows PowerShell 5.1 — see the trap note below.
    $req = @{ Uri = "$Base$Path"; Method = $Method; Headers = $headers
              UseBasicParsing = $true; ErrorAction = 'Stop' }
    if ($null -ne $Body) {
        $req['Body']        = $Body | ConvertTo-Json -Depth 10 -Compress
        $req['ContentType'] = 'application/json'
    }

    $status = 0; $text = $null
    try {
        $r      = Invoke-WebRequest @req
        $status = [int]$r.StatusCode
        $text   = $r.Content
    }
    catch {
        # PowerShell treats any 4xx/5xx as a terminating error, so the FAILURE cases — which are most of
        # what these checkpoints prove — land here. Dig the status and the body back out.
        $resp = $_.Exception.Response
        if ($resp) { $status = [int]$resp.StatusCode }

        # Where the error body lives depends on your PowerShell version:
        #   • PS 7   → $_.ErrorDetails.Message
        #   • PS 5.1 → still on the response stream; read it off manually
        $text = $_.ErrorDetails.Message
        if (-not $text -and $resp -and $resp.PSObject.Methods.Name -contains 'GetResponseStream') {
            try { $text = (New-Object IO.StreamReader($resp.GetResponseStream())).ReadToEnd() } catch { }
        }
    }

    $parsed = if ($text) { try { $text | ConvertFrom-Json } catch { $text } } else { $null }
    [pscustomobject]@{ Status = $status; Body = $parsed }
}
```

> 🔑 **Why a helper at all, when the phases just call `curl`?** Because of that `catch`. `curl -i` prints
> whatever comes back and shrugs at a 401; PowerShell's web cmdlets **throw** on any non-2xx, so the naive
> translation blows up on exactly the responses you're trying to *observe*. Nearly every checkpoint in this
> course proves something with a failure code — a 401, a 403, a 422 — so we need one place that catches that
> and hands back `.Status` and `.Body` either way. You paste it once, and every checkpoint below is then a
> single readable line.

> ⚠️ **The two PowerShell 5.1 traps baked into that helper — both cost an hour if you hit them cold.**
> 1. **`-UseBasicParsing`.** Without it, PS 5.1 tries to parse a *successful* response with the Internet
>    Explorer DOM engine, and on a machine where IE was never configured it throws — *after* the request
>    succeeded. The symptom is maddening: every **failure** reports its status correctly (errors throw before
>    the parse) while every **success** blows up. If your logins return nothing and your 401s look fine, this
>    is why.
> 2. **The error body isn't where you'd expect.** PS 7 hands it to you as `$_.ErrorDetails.Message`; PS 5.1
>    leaves it sitting on the response stream, so `$_.ErrorDetails.Message` is empty and you must read the
>    stream yourself. The helper tries both, so it works on either version.

> ℹ️ **On PowerShell 7** (`pwsh` — a free, separate install) you don't strictly need the helper:
> `Invoke-RestMethod` there takes `-SkipHttpErrorCheck -StatusCodeVariable code`, which does the same job
> natively. The helper above works on **both** 7 and the Windows PowerShell 5.1 that's already on your
> machine, so that's what we use.

> ✅ Every command in this file was run against the reference build on Windows PowerShell 5.1 — the expected
> outputs are real, not guessed.

Everything below assumes that block has run. `sqlcmd`, `dotnet`, and `git` are the same commands in every
shell — where a checkpoint uses those, this file just says so rather than repeating them.

---

## Phase 1 — health check

```powershell
Invoke-RestMethod "$Base/health"        # → healthy
```

Then open `$Base/swagger` in a browser. (No API key exists yet, so plain `Invoke-RestMethod` is fine here —
the helper only earns its keep once there are headers and failures.)

## Phase 3 — the database

`sqlcmd` is a native executable: **every command in the phase works as-is in PowerShell.** The only thing to
know is that a path with a backslash is already what PowerShell wants:

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -Q "IF DB_ID('Erp') IS NULL CREATE DATABASE Erp;"
sqlcmd -S "(localdb)\MSSQLLocalDB" -d Erp -i src/Modules/Sales/Infrastructure/Sql/001_schema.sql
# ...and the rest of the phase's scripts, in the same order.

sqlcmd -S "(localdb)\MSSQLLocalDB" -d Erp -Q "SELECT Username, Role FROM [identity].AppUser;"
```

## Phase 3b — prove C# can reach the database

```powershell
Invoke-RestMethod "$Base/db-ping"       # → users : 3
```

## Phase 4 (Part 1) — mint a token and read it back

```powershell
$t = Invoke-RestMethod "$Base/dev/token"
$t.token          # the three-part string
$t.claims         # sub / name / role / jti / exp / iss / aud
```

## Phase 4b — log in, and watch the token gate an endpoint

There's **no API key yet** (that's Part 3), so for this one part, drop it from the helper's headers — or just
set `$Key = $null` and ignore the empty header.

```powershell
# 1) Protected endpoint refuses an anonymous caller
(Invoke-Api '/sales/ping?name=world').Status                                  # → 401
(Invoke-Api '/sales/ping?name=world' -Token 'not.a.jwt').Status               # → 401

# 2) Log in
$login = Invoke-Api '/auth/login' -Method Post -Body @{ username = 'rep'; password = 'Password123!' }
$login.Status                                                                 # → 200
$access = $login.Body.accessToken

# 3) The same endpoint now lets you through
$ping = Invoke-Api '/sales/ping?name=world' -Token $access
$ping.Status; $ping.Body                                                      # → 200, pong: world

# 4) A wrong password is refused — and tells you nothing useful
$bad = Invoke-Api '/auth/login' -Method Post -Body @{ username = 'rep'; password = 'WRONG' }
$bad.Status; $bad.Body.detail            # → 401, Invalid username or password.
$nouser = Invoke-Api '/auth/login' -Method Post -Body @{ username = 'nobody'; password = 'WRONG' }
$nouser.Status; $nouser.Body.detail      # → 401, the SAME message — that's the point
```

## Phase 4c — the API-key gate, rotation, and the reuse trap

From here on **every** call carries the key, which the helper already does.

```powershell
# 1) The gate fires before login itself. Temporarily blank the key to prove it:
$real = $Key; $Key = 'not-a-real-key'
(Invoke-Api '/auth/login' -Method Post -Body @{ username = 'rep'; password = 'Password123!' })
# → Status 401, Body.error = Missing or invalid API key.
$Key = $real

# 2) Log in with the key — note the refreshToken that's now in the response
$s = (Invoke-Api '/auth/login' -Method Post -Body @{ username = 'rep'; password = 'Password123!' }).Body
$access = $s.accessToken; $refresh = $s.refreshToken

# 3) The access token opens a protected door
(Invoke-Api '/sales/ping?name=world' -Token $access).Status                   # → 200

# 4) Rotation: spend the refresh token, get a brand-new pair
$r2 = (Invoke-Api '/auth/refresh' -Method Post -Body @{ refreshToken = $refresh }).Body
$refresh2 = $r2.refreshToken
$refresh2 -eq $refresh                                                        # → False. That difference IS rotation.

# 5) Reuse detection: replay the token you just spent
(Invoke-Api '/auth/refresh' -Method Post -Body @{ refreshToken = $refresh }).Body.detail
# → Refresh token reuse detected — session revoked. Please log in again.

#    ...and the honest client's good token is dead too — the whole family burned:
(Invoke-Api '/auth/refresh' -Method Post -Body @{ refreshToken = $refresh2 }).Body.detail
# → Refresh token reuse detected — session revoked. Please log in again.

# 6) Logout revokes the session (log in fresh first — the family above is gone)
$s3 = (Invoke-Api '/auth/login' -Method Post -Body @{ username = 'rep'; password = 'Password123!' }).Body
(Invoke-Api '/auth/logout' -Method Post -Body @{ refreshToken = $s3.refreshToken }).Status   # → 204
(Invoke-Api '/auth/refresh' -Method Post -Body @{ refreshToken = $s3.refreshToken }).Status  # → 401
```

## Phase 4d — change a password, and watch every session die

```powershell
# 1) A refresh token minted under the CURRENT security stamp
$s = (Invoke-Api '/auth/login' -Method Post -Body @{ username = 'rep'; password = 'Password123!' }).Body

# 2) Change the password — note there is no token anywhere in this call
(Invoke-Api '/auth/change-password' -Method Post -Body @{
    username = 'rep'; currentPassword = 'Password123!'; newPassword = 'Temp_Pass_123!' }).Status   # → 204

# 3) The refresh token from step 1 is now dead — the stamp rotation did that, not a sweep
(Invoke-Api '/auth/refresh' -Method Post -Body @{ refreshToken = $s.refreshToken }).Body.detail
# → Credentials changed. Please log in again.

# 4) Old password gone, new one works
(Invoke-Api '/auth/login' -Method Post -Body @{ username = 'rep'; password = 'Password123!' }).Status    # → 401
(Invoke-Api '/auth/login' -Method Post -Body @{ username = 'rep'; password = 'Temp_Pass_123!' }).Status  # → 200

# 5) Put rep back — every later phase assumes Password123!
(Invoke-Api '/auth/change-password' -Method Post -Body @{
    username = 'rep'; currentPassword = 'Temp_Pass_123!'; newPassword = 'Password123!' }).Status    # → 204
```

## Phase 4e — the whole gate, end to end

Same steps as 4c/4d above, run in one pass. The phase's `jq` lines become plain property access — that's the
one place PowerShell is genuinely nicer than bash, since `Invoke-RestMethod` has already parsed the JSON:

```powershell
# bash:  ACCESS=$(echo "$resp" | jq -r .accessToken)
# PowerShell:
$access = $login.Body.accessToken
```

## Phase 5 and Phase 12 — tests

`dotnet` is a native executable, so these are identical in every shell:

```powershell
dotnet test tests/Erp.Modules.Sales.UnitTests
dotnet test
```

## Phase 6 — create a real order

```powershell
$s = (Invoke-Api '/auth/login' -Method Post -Body @{ username = 'rep'; password = 'Password123!' }).Body
$token = $s.accessToken

# Happy path → 201 Created
$created = Invoke-Api '/sales-orders' -Method Post -Token $token -Body @{
    customerId = 1
    lines      = @( @{ itemId = 1; quantity = 2 } )       # note @( ) — see the array gotcha below
}
$created.Status; $created.Body.id                          # → 201, the new order id

# A blocked customer (Gamma) → 422, and nothing is written
$blocked = Invoke-Api '/sales-orders' -Method Post -Token $token -Body @{
    customerId = 3; lines = @( @{ itemId = 1; quantity = 1 } ) }
$blocked.Status; $blocked.Body.detail
# → 422, Customer 'Gamma Traders' is blocked and cannot place orders.
```

> ⚠️ **The one real gotcha in this file: single-element arrays.** `ConvertTo-Json` turns a *one-item* array
> into a bare object, not an array — so `lines = @{ itemId = 1 }` sends `"lines": {…}` and the API rejects it
> with a binding error that looks like your JSON is fine. Always wrap lines in `@( … )`, and if a payload
> misbehaves, look at what you actually sent:
>
> ```powershell
> @{ customerId = 1; lines = @( @{ itemId = 1; quantity = 2 } ) } | ConvertTo-Json -Depth 10
> ```

## Phase 7 — submit reserves stock

```powershell
$id = $created.Body.id
(Invoke-Api "/sales-orders/$id/submit" -Method Post -Token $token).Status     # → 204
```

Then verify in SQL exactly as the phase shows (`sqlcmd` is unchanged), and try over-ordering the Restricted
Kit to watch the whole submit roll back:

```powershell
$greedy = Invoke-Api '/sales-orders' -Method Post -Token $mgrToken -Body @{
    customerId = 1; lines = @( @{ itemId = 3; quantity = 8 } ) }
(Invoke-Api "/sales-orders/$($greedy.Body.id)/submit" -Method Post -Token $mgrToken).Body.detail
# → Insufficient stock for item 3. Submission cancelled.     (after another order already holds some)
```

## Phase 8 — approve (manager only) and cancel (releases)

```powershell
$rep = (Invoke-Api '/auth/login' -Method Post -Body @{ username = 'rep'; password = 'Password123!' }).Body.accessToken
$mgr = (Invoke-Api '/auth/login' -Method Post -Body @{ username = 'manager'; password = 'Password123!' }).Body.accessToken

# The role gate: a perfectly valid rep token, refused before the handler runs
(Invoke-Api "/sales-orders/$id/approve" -Method Post -Token $rep).Status      # → 403
(Invoke-Api "/sales-orders/$id/approve" -Method Post -Token $mgr).Status      # → 204
(Invoke-Api "/sales-orders/$id/cancel"  -Method Post -Token $mgr).Status      # → 204
```

## Phase 9 — edit a draft

```powershell
(Invoke-Api "/sales-orders/$draftId" -Method Put -Token $token -Body @{
    lines = @( @{ itemId = 1; quantity = 3 } ) }).Status                      # → 204

# A submitted order refuses the same edit — the DOMAIN refuses it, not the handler
(Invoke-Api "/sales-orders/$submittedId" -Method Put -Token $token -Body @{
    lines = @( @{ itemId = 1; quantity = 3 } ) }).Body.detail
# → Lines can only be changed on a draft order.        (422)
```

## Phase 10 — read one, and search

```powershell
(Invoke-Api "/sales-orders/1" -Token $token).Body
(Invoke-Api '/sales-orders?status=PendingApproval&page=1&pageSize=20' -Token $token).Body

# Bad input → 400, not 500 (the Enum.TryParse guard + the handler's ValidationException arm)
(Invoke-Api '/sales-orders?status=Banana' -Token $token).Status               # → 400
```

## Phase 11 — errors are clean and consistent

```powershell
# NEW this phase: the validator finally runs → 400 with field errors
$empty = Invoke-Api '/sales-orders' -Method Post -Token $token -Body @{ customerId = 2; lines = @() }
$empty.Status; $empty.Body.errors        # → 400, An order needs at least one line.

# NEW: the paging cap is enforced
(Invoke-Api '/sales-orders?pageSize=5000' -Token $token).Body.errors
# → 'Page Size' must be between 1 and 100. You entered 5000.

# Regression check: business rules still 422 (as they have since Phase 6)
(Invoke-Api '/sales-orders' -Method Post -Token $token -Body @{
    customerId = 2; lines = @( @{ itemId = 1; quantity = 100 } ) }).Status    # → 422
```

The dev-vs-prod switch is an environment variable rather than a request. In PowerShell:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Production'
dotnet run --project src/Erp.Api
# ...force an unexpected error: the 500 detail is now "An unexpected error occurred."
$env:ASPNETCORE_ENVIRONMENT = 'Development'    # put it back
```

---

## The translation table (for anything not listed above)

| bash / curl | PowerShell |
|---|---|
| `BASE=http://localhost:5234` | `$Base = 'http://localhost:5234'` |
| `curl "$BASE/health"` | `Invoke-RestMethod "$Base/health"` |
| `curl -i …` (show status) | `(Invoke-Api '/path').Status` — PowerShell throws on 4xx/5xx, hence the helper |
| `-H "X-Api-Key: $KEY"` | `-Headers @{ 'X-Api-Key' = $Key }` (the helper does this) |
| `-X POST -d '{"a":1}'` | `-Method Post -Body @{ a = 1 }` (the helper converts to JSON) |
| `jq -r .accessToken` | `.Body.accessToken` — already parsed, no tool needed |
| `AUTH=(-H "…" -H "…")` | pass `-Token $access`; the helper assembles the headers |
| `echo $VAR` | `$VAR` (just name it) |
| `export FOO=bar` | `$env:FOO = 'bar'` |

> 📌 **If a command here ever disagrees with a phase, the phase wins.** These are a translation of the
> checkpoints, kept alongside them — but the lesson is the source of truth, and a translation is exactly the
> kind of second copy that drifts. If you spot a mismatch, that's a bug worth reporting.

---

← Back to the [tutorial overview](00-START-HERE.md) · Answers to the phase questions: [ANSWER-KEY.md](ANSWER-KEY.md)
