# Every Checkpoint, in PowerShell

Good day, developer! The lessons write their "Let's run it!" checkpoints in bash and `curl`, because that's
how almost every API document on the internet writes them, and it's worth being able to read them. But if
you're on Windows and would rather stay in PowerShell, this page has the same checkpoints, translated: the
same requests, in the same order, with the same results. Are you ready? Let's begin!

You have three ways to run a checkpoint. All of them are fine:

| Option | Why you'd pick it |
|---|---|
| **Git Bash** | Comes with Git for Windows. The lessons' commands work exactly as written. Right-click a folder, *Open Git Bash here* |
| **PowerShell**, with this page | You already live there |
| **Swagger** or the test console | You'd rather click than type. Swagger's **Authorize** button takes the token, and from lesson 13 the API key |

One thing to watch out for: don't paste the lessons' bash into PowerShell. It looks as if it should work,
and it won't. `TOKEN=$(...)` isn't PowerShell, and in Windows PowerShell 5.1, `curl` is a nickname for
`Invoke-WebRequest`, so `curl -i -X POST ...` fails with confusing parameter errors.

## The setup: once per PowerShell window

Paste this whole block before any checkpoint below:

```powershell
$Base = 'http://localhost:5264'     # the port `dotnet run` printed
$Key  = $null                        # from lesson 13: 'SIUgiguVpxcKJ4Ohk5l00kYJXEDPe8rFO8Do59AEloY='
$Psql = 'C:\Program Files\PostgreSQL\18\bin\psql.exe'
$env:PGPASSWORD = 'erp_dev_only'     # the development superuser password from lesson 04

function Invoke-Api {
    param(
        [Parameter(Mandatory)][string]$Path,
        [string]$Method = 'GET',
        $Body,                        # a hashtable; converted to JSON for you
        [string]$Token                # the access token, when the route needs one
    )

    $headers = @{}
    if ($Key)   { $headers['X-Api-Key'] = $Key }
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
    (Invoke-Api '/auth/login' -Method Post -Body @{ email = $Email; password = 'Password123!' }).Body.accessToken
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

`dotnet`, `git` and `psql` are the same commands in every shell. Where a checkpoint only uses those, the
command below is the lesson's, with PowerShell's way of naming the program.

## Lesson 01 — the empty API

```powershell
dotnet build
dotnet run --project src/Erp.Api     # leave it running, and open a second PowerShell for the rest
Invoke-RestMethod "$Base/"            # Erp API
```

## Lesson 02 — the mediator

```powershell
Invoke-RestMethod "$Base/ping?message=hello"     # pong: hello
(Invoke-Api '/ping?message=').Status              # 500, for now; lesson 06 turns it into a 400
```

## Lesson 03 — the domain

```powershell
dotnet test                                       # Passed: 21
```

## Lesson 04 — the database

```powershell
& $Psql -h localhost -U postgres -d erp -c "SELECT id, name, credit_limit, outstanding_balance, is_blocked FROM sales.customer;"
```

## Lesson 05 — sign-in

```powershell
$token = Get-Token 'manager@erp.local'
(Invoke-Api '/auth/me' -Token $token).Body        # id 2, manager@erp.local, SalesManager

(Invoke-Api '/auth/me').Status                    # 401: no token
(Invoke-Api '/auth/register' -Method Post -Body @{ email = 'intruder@example.com'; password = 'LongEnoughPassword1' }).Status   # 404
(Invoke-Api '/auth/login' -Method Post -Body @{ email = 'rep@erp.local'; password = 'wrong' }).Status                           # 401
```

## Lesson 06 — create an order

```powershell
$token = Get-Token 'rep@erp.local'
(Invoke-Api '/sales-orders' -Method Post -Token $token -Body @{ customerId = 1; lines = @(@{ itemId = 1; quantity = 2 }) }).Body   # id 1

$tries = @(
    @{ customerId = 1;  lines = @() },                                  # 400, the validator
    @{ customerId = 99; lines = @(@{ itemId = 1; quantity = 1 }) },     # 404, unknown customer
    @{ customerId = 3;  lines = @(@{ itemId = 1; quantity = 1 }) },     # 422, Gamma is blocked
    @{ customerId = 2;  lines = @(@{ itemId = 1; quantity = 100 }) },   # 422, over Beta's credit
    @{ customerId = 1;  lines = @(@{ itemId = 3; quantity = 1 }) }      # 422, Controlled needs a manager
)
foreach ($body in $tries) { Show (Invoke-Api '/sales-orders' -Method Post -Token $token -Body $body) }

& $Psql -h localhost -U postgres -d erp -c "SELECT id, order_number, status FROM sales.sales_order;"
```

## Lesson 07 — submit reserves stock

```powershell
$token = Get-Token 'manager@erp.local'
$kits  = @{ customerId = 5; lines = @(@{ itemId = 3; quantity = 5 }) }
$a = (Invoke-Api '/sales-orders' -Method Post -Token $token -Body $kits).Body.id
$b = (Invoke-Api '/sales-orders' -Method Post -Token $token -Body $kits).Body.id

(Invoke-Api "/sales-orders/$a/submit" -Method Post -Token $token).Status    # 204
Show (Invoke-Api "/sales-orders/$b/submit" -Method Post -Token $token)      # 422  Insufficient stock for item 3

& $Psql -h localhost -U postgres -d erp `
    -c "SELECT item_id, on_hand, reserved FROM inventory.stock_item WHERE item_id = 3;" `
    -c "SELECT id, status FROM sales.sales_order WHERE customer_id = 5;"
```

## Lesson 08 — approve, reject, cancel

```powershell
$rep  = Get-Token 'rep@erp.local'
$mgr  = Get-Token 'manager@erp.local'
$rack = @{ customerId = 5; lines = @(@{ itemId = 4; quantity = 1 }) }

$id = (Invoke-Api '/sales-orders' -Method Post -Token $rep -Body $rack).Body.id
(Invoke-Api "/sales-orders/$id/submit"  -Method Post -Token $rep).Status    # 204, waiting for a manager
(Invoke-Api "/sales-orders/$id/approve" -Method Post -Token $rep).Status    # 403: reps can't approve
(Invoke-Api "/sales-orders/$id/approve" -Method Post -Token $mgr).Status    # 204: approved
(Invoke-Api "/sales-orders/$id/approve" -Method Post -Token $mgr).Status    # 422: already approved

$other = (Invoke-Api '/sales-orders' -Method Post -Token $rep -Body $rack).Body.id
(Invoke-Api "/sales-orders/$other/submit" -Method Post -Token $rep).Status                                  # 204
(Invoke-Api "/sales-orders/$other/reject" -Method Post -Token $mgr -Body @{ reason = 'Price not agreed' }).Status   # 204
(Invoke-Api "/sales-orders/$id/cancel" -Method Post -Token $mgr).Status                                     # 204

$own = (Invoke-Api '/sales-orders' -Method Post -Token $mgr -Body $rack).Body.id
(Invoke-Api "/sales-orders/$own/submit" -Method Post -Token $mgr).Status    # 204
Show (Invoke-Api "/sales-orders/$own/approve" -Method Post -Token $mgr)     # 422  can't be approved by the person who created it
```

## Lesson 09 — edit a draft

```powershell
$token = Get-Token 'rep@erp.local'
$id = (Invoke-Api '/sales-orders' -Method Post -Token $token -Body @{ customerId = 1; lines = @(@{ itemId = 1; quantity = 2 }) }).Body.id

(Invoke-Api "/sales-orders/$id" -Method Put -Token $token -Body @{ lines = @(@{ itemId = 2; quantity = 3 }) }).Status    # 204
(Invoke-Api "/sales-orders/$id/submit" -Method Post -Token $token).Status                                              # 204
Show (Invoke-Api "/sales-orders/$id" -Method Put -Token $token -Body @{ lines = @(@{ itemId = 1; quantity = 1 }) })     # 422  Lines can only be changed on a draft order.
```

## Lesson 10 — reads

```powershell
(Invoke-Api '/sales-orders/1' -Token $token).Body
(Invoke-Api '/sales-orders?status=Approved&pageSize=5' -Token $token).Body.items
(Invoke-Api '/sales-orders?page=9999' -Token $token).Body | Select-Object items, totalCount    # no items, but a real total
Show (Invoke-Api '/sales-orders?status=Shipped' -Token $token)                                   # 400  Unknown status 'Shipped'.

& $Psql -h localhost -U postgres -d erp -c "SELECT * FROM sales.search_sales_orders(NULL, NULL, 1, 5);"
```

## Lesson 11 — events and the outbox

```powershell
$id = (Invoke-Api '/sales-orders' -Method Post -Token $token -Body @{ customerId = 1; lines = @(@{ itemId = 1; quantity = 1 }) }).Body.id
(Invoke-Api "/sales-orders/$id/submit" -Method Post -Token $token).Status    # 204; the audit line appears in the API's console

& $Psql -h localhost -U postgres -d erp -c "SELECT type, payload, processed_utc, attempts FROM sales.outbox_message ORDER BY occurred_utc DESC LIMIT 1;"
```

## Lesson 12 — observability

```powershell
dotnet run --project src/Erp.AppHost     # open the login link it prints

Invoke-RestMethod "$Base/health/live"     # Healthy
Invoke-RestMethod "$Base/health/ready"    # Healthy; stop PostgreSQL and it says Unhealthy
(Invoke-WebRequest "$Base/health/live" -UseBasicParsing).Headers['X-Trace-Id']    # nothing: health isn't traced
(Invoke-WebRequest "$Base/sales-orders" -Headers @{ Authorization = "Bearer $token" } -UseBasicParsing).Headers['X-Trace-Id']
```

## Lesson 13 — security hardening

From here on, every request needs the API key, so set it once:

```powershell
$Key = 'SIUgiguVpxcKJ4Ohk5l00kYJXEDPe8rFO8Do59AEloY='

$saved = $Key; $Key = $null
(Invoke-Api '/sales-orders').Status           # 401: no API key
$Key = $saved
(Invoke-Api '/health/ready').Status           # 200: health is exempt

$token = Get-Token 'rep@erp.local'
(Invoke-WebRequest "$Base/sales-orders" -Headers @{ 'X-Api-Key' = $Key; Authorization = "Bearer $token" } -UseBasicParsing).Headers
# X-Frame-Options and Content-Security-Policy are there; Server is not

1..11 | ForEach-Object { (Invoke-Api '/auth/login' -Method Post -Body @{ email = 'rep@erp.local'; password = 'Password123!' }).Status }
# 200 until this minute's ten sign-ins are used up (the Get-Token above was one of them), then 429
```

And the least-privilege account, refusing to drop a table:

```powershell
$env:PGPASSWORD = '<the erp_app password>'
& $Psql -h localhost -U erp_app -d erp -c "DROP TABLE sales.customer;"     # ERROR:  must be owner of table customer
$env:PGPASSWORD = 'erp_dev_only'
```

## Lesson 14 — testing

```powershell
dotnet test

& $Psql -h localhost -U postgres -v db=erp_roles_check -v owner_password=owner_dev_only -v app_password=app_dev_only `
    -f deploy/postgres/create-roles.sql

$env:ERP_TEST_DB       = 'Host=localhost;Database=erp_roles_check;Username=erp_app;Password=app_dev_only'
$env:ERP_TEST_DB_OWNER = 'Host=localhost;Database=erp_roles_check;Username=erp_owner;Password=owner_dev_only'
dotnet test tests/Erp.Integration.Tests
Remove-Item Env:ERP_TEST_DB, Env:ERP_TEST_DB_OWNER     # so the next `dotnet test` uses its own database again
```

Bash sets those variables for one command only; PowerShell keeps them for the whole window. That's why the
last line removes them.

## Lessons 15 and 16

They're already written in PowerShell, because they run on a Windows server.

## The translation table

| bash / curl | PowerShell |
|---|---|
| `TOKEN=$(curl ... \| jq -r .accessToken)` | `$token = Get-Token 'rep@erp.local'` |
| `curl -s URL` | `Invoke-RestMethod URL`, or `(Invoke-Api '/path').Body` |
| `curl -i ...` or `-w "%{http_code}"` | `(Invoke-Api '/path').Status` |
| `-H "Authorization: Bearer $TOKEN"` | `-Token $token` |
| `-H "X-Api-Key: $KEY"` | set `$Key` once; the helper sends it |
| `-X POST -d '{"a":1}'` | `-Method Post -Body @{ a = 1 }` |
| `jq .detail` | `.Body.detail`, or `Show (...)` |
| `PGPASSWORD=x psql ...` | `$env:PGPASSWORD = 'x'; & $Psql ...` |
| `NAME=value command` | `$env:NAME = 'value'; command; Remove-Item Env:NAME` |
| a line ending in `\` | a line ending in a backtick `` ` `` |

If a command here ever disagrees with a lesson, the lesson wins. This page is a translation, and a second
copy is exactly the kind of thing that drifts. If you spot a mismatch, it's a bug worth fixing.

---

← [Start here](00-START-HERE.md) · [Foundations](00a-FOUNDATIONS.md) · [Answer key](ANSWER-KEY.md)
