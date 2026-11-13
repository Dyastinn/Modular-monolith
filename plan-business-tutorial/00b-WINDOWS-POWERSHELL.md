# Every Terminal Checkpoint, in PowerShell

Good day, developer! Most "Let's run it!" checkpoints happen in the app, in your browser. The ones that use
the terminal are written in bash and `curl`, because that's how almost every API document on the internet
writes them, and it's worth being able to read them. But if you're on Windows and would rather stay in
PowerShell, this page has the same terminal checks, translated: the same requests, in the same order, with
the same results. Are you ready? Let's begin!

You have three ways to run a checkpoint. All of them are fine:

| Option | Why you'd pick it |
|---|---|
| **Git Bash** | Comes with Git for Windows. The lessons' commands work exactly as written. Right-click a folder, *Open Git Bash here* |
| **PowerShell**, with this page | You already live there |
| **Scalar** (`/scalar`, development only) | You'd rather click than type. If you're signed in to the app in the same browser, its cookie works there too |

One thing to watch out for: don't paste the lessons' bash into PowerShell. It looks as if it should work,
and it won't. `TOKEN=$(...)` isn't PowerShell, and in Windows PowerShell 5.1, `curl` is a nickname for
`Invoke-WebRequest`, so `curl -i -X POST ...` fails with confusing parameter errors.

## The setup: once per PowerShell window

Paste this whole block before any checkpoint below:

```powershell
$Base = 'http://localhost:5264'     # the port `dotnet run` printed
$Psql = 'C:\Program Files\PostgreSQL\18\bin\psql.exe'
$env:PGPASSWORD = 'erp_dev_only'     # the development superuser password from lesson 05

function Invoke-Api {
    param(
        [Parameter(Mandatory)][string]$Path,
        [string]$Method = 'GET',
        $Body,                        # a hashtable; converted to JSON for you
        [string]$Token                # the access token, when the route needs one
    )

    $headers = @{}
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }

    $req = @{ Uri = "$Base$Path"; Method = $Method; Headers = $headers; UseBasicParsing = $true; ErrorAction = 'Stop' }
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
        $resp = $_.Exception.Response
        if ($resp) { $status = [int]$resp.StatusCode }
        $text = $_.ErrorDetails.Message
        if (-not $text -and $resp -and $resp.PSObject.Methods.Name -contains 'GetResponseStream') {
            try { $text = (New-Object IO.StreamReader($resp.GetResponseStream())).ReadToEnd() } catch { }
        }
    }

    $parsed = if ($text) { try { $text | ConvertFrom-Json } catch { $text } } else { $null }
    [pscustomobject]@{ Status = $status; Body = $parsed }
}

function Get-Token([string]$Email) {
    (Invoke-Api '/api/auth/login' -Method Post -Body @{ email = $Email; password = 'Password123!' }).Body.accessToken
}

function Show([pscustomobject]$Result) {
    '{0}  {1}' -f $Result.Status, $Result.Body.detail
}

[Console]::OutputEncoding = [Text.Encoding]::UTF8     # so ₱ prints as ₱, not ?
```

Why a helper, when the lessons just call `curl`? Because PowerShell's web commands *throw* on any 4xx or 5xx
answer, and most checkpoints prove something with exactly those: a 401, a 403, a 422. `Invoke-Api` catches
them and always hands back `.Status` and `.Body`. Two more traps of Windows PowerShell 5.1 are handled
inside it. Without `UseBasicParsing`, a *successful* response can fail while being parsed, on machines where
Internet Explorer was never set up. And on 5.1, the error body isn't in `$_.ErrorDetails.Message`, so the
helper reads it off the response itself. It works the same on PowerShell 7.

`dotnet`, `git`, `podman` and `psql` are the same commands in every shell. Where a checkpoint only uses
those, it isn't repeated here.

## Lesson 01 — the app and the API

```powershell
(Invoke-Api '/api/nothing').Status      # 404: an API address that doesn't exist
(Invoke-Api '/sales-orders').Status     # 200, and the body is the app's HTML
```

## Lesson 02 — testing

`dotnet test`, its filters and `dotnet watch test` are the same in PowerShell. Quote a filter in single
quotes, so PowerShell leaves the `*` alone:

```powershell
dotnet test --filter-class '*HostTests'
dotnet test --output Detailed
```

## Lesson 03 — features

```powershell
(Invoke-Api '/api/ping?message=hello').Body          # pong: hello
(Invoke-Api '/api/ping?message=').Status             # 400
(Invoke-Api '/api/ping?message=').Body.errors        # Message: 'Message' must not be empty.
```

## Lesson 05 — the database

```powershell
& $Psql -h localhost -U postgres -d erp -c "SELECT id, name, credit_limit, outstanding_balance, is_blocked FROM sales.customer;"
```

## Lesson 06 — sign-in

```powershell
$token = Get-Token 'manager@erp.local'
(Invoke-Api '/api/auth/me' -Token $token).Body        # id 2, manager@erp.local, SalesManager

(Invoke-Api '/api/auth/me').Status                    # 401: no token
(Invoke-Api '/api/auth/register' -Method Post -Body @{ email = 'intruder@example.com'; password = 'LongEnoughPassword1' }).Status   # 404
(Invoke-Api '/api/auth/login' -Method Post -Body @{ email = 'rep@erp.local'; password = 'wrong' }).Status                           # 401
```

The browser tests, against a running app:

```powershell
Start-Process dotnet 'run --project src/Erp.Api'      # the app, in its own window
$env:ERP_UI_URL = 'http://localhost:5264'; $env:ERP_UI_SCREENSHOTS = '.\screens'
dotnet test --project tests/Erp.Web.UiTests
Remove-Item Env:ERP_UI_URL, Env:ERP_UI_SCREENSHOTS
```

Bash sets variables for one command only; PowerShell keeps them for the whole window. That's why the last
line removes them.

## Lesson 07 — create an order

```powershell
$token = Get-Token 'rep@erp.local'
(Invoke-Api '/api/sales-orders' -Method Post -Token $token -Body @{ customerId = 1; lines = @(@{ itemId = 1; quantity = 2 }) }).Body   # id

$tries = @(
    @{ customerId = 1;  lines = @() },                                  # 400, the validator
    @{ customerId = 99; lines = @(@{ itemId = 1; quantity = 1 }) },     # 422 SALES-0026, unknown customer
    @{ customerId = 3;  lines = @(@{ itemId = 1; quantity = 1 }) },     # 422, Gamma is blocked
    @{ customerId = 2;  lines = @(@{ itemId = 1; quantity = 100 }) },   # 422, over Beta's credit
    @{ customerId = 1;  lines = @(@{ itemId = 3; quantity = 1 }) },     # 422, Controlled needs a manager
    @{ customerId = 1;  lines = @(@{ itemId = 1; quantity = 1.5 }) },   # 422, Widgets are whole pieces
    @{ customerId = 1;  lines = @(@{ itemId = 5; quantity = 2.5 }) }    # 201, 2.5 kg of wire is fine
)
foreach ($body in $tries) { Show (Invoke-Api '/api/sales-orders' -Method Post -Token $token -Body $body) }   # each refusal shows its code

& $Psql -h localhost -U postgres -d erp -c "SELECT id, order_number, status FROM sales.sales_order;"
```

## Lesson 08 — find orders, and who sees what

```powershell
$rep = Get-Token 'rep@erp.local'
$mgr = Get-Token 'manager@erp.local'

(Invoke-Api '/api/sales-orders?search=acme&pageSize=5' -Token $rep).Body.totalCount
(Invoke-Api '/api/sales-orders?page=9999' -Token $rep).Body | Select-Object items, totalCount    # no items, but a real total
Show (Invoke-Api '/api/sales-orders?status=Shipped' -Token $rep)                                   # 400 for now; lesson 18 adds it
(Invoke-Api '/api/customers?search=beta' -Token $rep).Body.items[0].availableCredit               # 500.0

$id = (Invoke-Api '/api/sales-orders' -Method Post -Token $mgr -Body @{ customerId = 1; lines = @(@{ itemId = 1; quantity = 1 }) }).Body.id
(Invoke-Api "/api/sales-orders/$id" -Token $mgr).Status    # 200
(Invoke-Api "/api/sales-orders/$id" -Token $rep).Status    # 404: not the rep's order

& $Psql -h localhost -U postgres -d erp -c "SELECT * FROM sales.search_sales_orders(NULL, NULL, NULL, NULL, 1, 5);"
```

## Lesson 09 — submit reserves stock

```powershell
$mgr  = Get-Token 'manager@erp.local'
$kits = @{ customerId = 5; lines = @(@{ itemId = 3; quantity = 5 }) }
$a = (Invoke-Api '/api/sales-orders' -Method Post -Token $mgr -Body $kits).Body.id
$b = (Invoke-Api '/api/sales-orders' -Method Post -Token $mgr -Body $kits).Body.id

(Invoke-Api "/api/sales-orders/$a/submit" -Method Post -Token $mgr).Status    # 204
Show (Invoke-Api "/api/sales-orders/$b/submit" -Method Post -Token $mgr)      # 422  Insufficient stock for Restricted Kit

& $Psql -h localhost -U postgres -d erp `
    -c "SELECT id, on_hand, reserved FROM inventory.item WHERE id = 3;" `
    -c "SELECT id, status FROM sales.sales_order WHERE customer_id = 5;"
```

## Lesson 10 — approve, reject, cancel

```powershell
$rep  = Get-Token 'rep@erp.local'
$mgr  = Get-Token 'manager@erp.local'
$rack = @{ customerId = 5; lines = @(@{ itemId = 4; quantity = 1 }) }

$id = (Invoke-Api '/api/sales-orders' -Method Post -Token $rep -Body $rack).Body.id
(Invoke-Api "/api/sales-orders/$id/submit"  -Method Post -Token $rep).Status    # 204, waiting for a manager
(Invoke-Api "/api/sales-orders/$id/approve" -Method Post -Token $rep).Status    # 403: reps can't approve
(Invoke-Api "/api/sales-orders/$id/approve" -Method Post -Token $mgr).Status    # 204: approved
(Invoke-Api "/api/sales-orders/$id/approve" -Method Post -Token $mgr).Status    # 422: already approved

$own = (Invoke-Api '/api/sales-orders' -Method Post -Token $mgr -Body $rack).Body.id
(Invoke-Api "/api/sales-orders/$own/submit" -Method Post -Token $mgr).Status    # 204
Show (Invoke-Api "/api/sales-orders/$own/approve" -Method Post -Token $mgr)     # 422  can't be approved by the person who created it
```

## Lesson 11 — edit a draft

```powershell
$token = Get-Token 'rep@erp.local'
$id = (Invoke-Api '/api/sales-orders' -Method Post -Token $token -Body @{ customerId = 1; lines = @(@{ itemId = 1; quantity = 2 }) }).Body.id

(Invoke-Api "/api/sales-orders/$id" -Method Put -Token $token -Body @{ lines = @(@{ itemId = 2; quantity = 3 }) }).Status    # 204
(Invoke-Api "/api/sales-orders/$id/submit" -Method Post -Token $token).Status                                              # 204
Show (Invoke-Api "/api/sales-orders/$id" -Method Put -Token $token -Body @{ lines = @(@{ itemId = 1; quantity = 1 }) })     # 422  Lines can only be changed on a draft order.
```

## Lesson 12 — containers and CI

Install Podman Desktop, then open it once to create and start the Podman machine, and turn on **Settings →
Docker compatibility** (that's what lets Testcontainers find it):

```powershell
winget install -e --id RedHat.Podman-Desktop
podman run --rm docker.io/library/hello-world
```

Stop the PostgreSQL from lesson 05, in a PowerShell window opened **as administrator**:

```powershell
Stop-Service postgresql-x64-18; Set-Service postgresql-x64-18 -StartupType Disabled
```

Then, in an ordinary window:

```powershell
podman run -d --name erp-postgres `
  -e POSTGRES_PASSWORD=erp_dev_only `
  -p 5432:5432 `
  -v erp-pgdata:/var/lib/postgresql `
  docker.io/library/postgres:18

podman exec -it erp-postgres psql -U postgres -d erp -c "SELECT name, credit_limit FROM sales.customer;"
```

From here on, `psql` runs inside the container, so every `& $Psql -h localhost -U postgres -d erp ...` below
is `podman exec -i erp-postgres psql -U postgres -d erp ...`. Git and GitHub work the same in PowerShell.

## Lesson 13 — events and the outbox

```powershell
podman exec -i erp-postgres psql -U postgres -d erp -c "SELECT type, payload, processed_utc, attempts FROM sales.outbox_message ORDER BY occurred_utc DESC LIMIT 1;"
```

## Lesson 14 — observability

```powershell
Invoke-RestMethod "$Base/health/live"     # Healthy
Invoke-RestMethod "$Base/health/ready"    # Healthy; stop PostgreSQL and it says Unhealthy
(Invoke-WebRequest "$Base/health/live" -UseBasicParsing).Headers['X-Trace-Id']    # nothing: health isn't traced
(Invoke-WebRequest "$Base/api/sales-orders" -Headers @{ Authorization = "Bearer $token" } -UseBasicParsing).Headers['X-Trace-Id']
```

## Lessons 15 to 20

```powershell
# 14: reps don't maintain customers; the validator checks terms
$rep = Get-Token 'rep@erp.local'; $mgr = Get-Token 'manager@erp.local'
(Invoke-Api '/api/customers/1/block' -Method Post -Token $rep).Status          # 403
(Invoke-Api '/api/customers' -Method Post -Token $mgr -Body @{ name = 'Bad Terms'; creditLimit = 0; paymentTermsDays = 400 }).Body.errors

# 17: a shipped order can't ship again
$clerk = Get-Token 'clerk@erp.local'
Show (Invoke-Api '/api/shipments' -Method Post -Token $clerk -Body @{ salesOrderId = 1 })

# 19 and 20: replay an event; nothing changes
podman exec -i erp-postgres psql -U postgres -d erp -c "UPDATE inventory.outbox_message SET processed_utc = NULL WHERE type LIKE '%ShipmentCreated%';"
podman exec -i erp-postgres psql -U postgres -d erp -c "UPDATE invoicing.outbox_message SET processed_utc = NULL WHERE type LIKE '%InvoicePaymentRecorded%';"
podman exec -i erp-postgres psql -U postgres -d erp -c "SELECT number, status FROM invoicing.invoice ORDER BY id;" -c "SELECT * FROM sales.applied_payment;"
```

## Lesson 23 — security hardening

```powershell
$rep = Get-Token 'rep@erp.local'
(Invoke-WebRequest "$Base/api/sales-orders" -Headers @{ Authorization = "Bearer $rep" } -UseBasicParsing).Headers
# X-Frame-Options and Content-Security-Policy are there; Server is not
(Invoke-WebRequest "$Base/" -UseBasicParsing).Headers['Content-Security-Policy']    # the app's policy

1..11 | ForEach-Object { (Invoke-Api '/api/auth/login' -Method Post -Body @{ email = 'rep@erp.local'; password = 'wrong' }).Status }
# 401 until this minute's ten sign-ins are used up, then 429
```

Production mode without a certificate, which must refuse to start:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ConnectionStrings__ErpDb = 'Host=localhost;Database=erp;Username=postgres;Password=erp_dev_only'
dotnet run --project src/Erp.Api --no-launch-profile     # InvalidOperationException: ... must be encrypted at rest.
Remove-Item Env:ASPNETCORE_ENVIRONMENT, Env:ConnectionStrings__ErpDb
```

And the least-privilege accounts: every `dotnet test` already creates them in its own container and runs
the whole suite as `erp_app`, so there's nothing extra to type.

## Lesson 24 — deploying with Podman

The server is Linux, so its commands are bash. To try the stack on your Windows machine, the `podman compose`
commands are the same in PowerShell; Podman Desktop's machine runs the containers, and it needs a Compose
program to hand the file to (Podman Desktop offers to install one; look for **Compose** in its settings).

## The translation table

| bash / curl | PowerShell |
|---|---|
| `TOKEN=$(curl ... \| jq -r .accessToken)` | `$token = Get-Token 'rep@erp.local'` |
| `curl -s URL` | `Invoke-RestMethod URL`, or `(Invoke-Api '/path').Body` |
| `curl -i ...` or `-w "%{http_code}"` | `(Invoke-Api '/path').Status` |
| `-H "Authorization: Bearer $TOKEN"` | `-Token $token` |
| `-X POST -d '{"a":1}'` | `-Method Post -Body @{ a = 1 }` |
| `jq .detail` | `.Body.detail`, or `Show (...)` |
| `PGPASSWORD=x psql ...` | `$env:PGPASSWORD = 'x'; & $Psql ...` (from lesson 12: `podman exec -i erp-postgres psql ...`) |
| `NAME=value command` | `$env:NAME = 'value'; command; Remove-Item Env:NAME` |
| a line ending in `\` | a line ending in a backtick `` ` `` |

If a command here ever disagrees with a lesson, the lesson wins. This page is a translation, and a second
copy is exactly the kind of thing that drifts. If you spot a mismatch, it's a bug worth fixing.

---

← [Start here](00-START-HERE.md) · [Foundations](00a-FOUNDATIONS.md) · [Answer key](ANSWER-KEY.md)
