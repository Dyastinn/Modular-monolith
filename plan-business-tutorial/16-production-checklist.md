# Lesson 16 — The Production Checklist: What's Done, What's Next

Good day, developer! Our ERP runs on a real server, over HTTPS, with a real admin. Today we add the one
protection nothing else on the list can give: a backup, proven by a restore. Then we step back and walk the
whole production checklist, line by line, honestly: what's done, which lesson did it, and what's still to
build. About an hour. Are you ready? Let's begin!

## What we'll build

- A nightly database backup, kept for two weeks.
- A restore drill that proves the backup works.
- The production checklist, with the lesson behind every line.
- The honest list of what's still missing, and where each piece gets built.
- A short guide to the tools this course left out, and when you'd add them.

## Why we build it this way

"Is it production-ready?" is a feeling until it's a list. A list with a lesson next to every line can be
checked by anyone: you in six months, a buyer, their IT person.

And backups come first because everything else on that list protects the app from attackers and from
mistakes in the code. Nothing on it protects the business from a dead disk, a migration that went wrong, or
someone deleting the wrong thing. Only a backup does. And a backup that has never been restored isn't a
backup yet; it's a hope.

## 1. A nightly backup

PostgreSQL comes with **`pg_dump`**, which writes a whole database to one file. It reads from a consistent
snapshot, so it's safe to run while the app is busy: nobody is blocked, and the file never contains half of
a transaction. We run it as `erp_owner`, which owns every table and can read them all.

An unattended job can't type a password, so PostgreSQL reads it from a **password file**. On the server,
in an elevated PowerShell:

```powershell
New-Item -ItemType Directory C:\ErpBackup, D:\Backups\erp -Force | Out-Null

$ownerPassword = Read-Host 'erp_owner password'
Set-Content C:\ErpBackup\pgpass.conf "localhost:5432:*:erp_owner:$ownerPassword" -Encoding ascii
icacls C:\ErpBackup /inheritance:r /grant 'SYSTEM:(OI)(CI)F' 'Administrators:(OI)(CI)F' | Out-Null
```

The line reads *host : port : database : user : password*, with `*` meaning any database. `icacls` removes
everyone's access to that folder except Windows itself and administrators. Then the script:

```powershell
# C:\ErpBackup\backup.ps1
$ErrorActionPreference = 'Stop'
$env:PGPASSFILE = 'C:\ErpBackup\pgpass.conf'
$folder = 'D:\Backups\erp'
$file   = Join-Path $folder ('erp-{0:yyyy-MM-dd}.dump' -f (Get-Date))

& 'C:\Program Files\PostgreSQL\18\bin\pg_dump.exe' -h localhost -U erp_owner -d erp -w --format=custom --file=$file
if ($LASTEXITCODE -ne 0) { throw 'pg_dump failed' }

Get-ChildItem $folder -Filter 'erp-*.dump' |
    Where-Object LastWriteTime -lt (Get-Date).AddDays(-14) |
    Remove-Item
```

| Piece | Why |
|---|---|
| `PGPASSFILE` | Tells `pg_dump` where the password file is |
| `-w` | Never prompt. If the password is missing, fail now instead of waiting forever for someone to type it |
| `--format=custom` | Compressed, and `pg_restore` can restore all of it or just one table |
| 14 days | Long enough to notice a mistake made last week. Change it to suit the business |

And a scheduled task to run it every night at 1 a.m.:

```powershell
$action  = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument '-NoProfile -ExecutionPolicy Bypass -File C:\ErpBackup\backup.ps1'
$trigger = New-ScheduledTaskTrigger -Daily -At 1am
Register-ScheduledTask -TaskName 'ERP nightly backup' -Action $action -Trigger $trigger -User 'SYSTEM' -RunLevel Highest
```

Two things to watch out for. First, a backup on the same server dies with the server. Have the business's IT
copy `D:\Backups\erp` somewhere else every day: another server, a NAS, cloud storage. Second, a dump holds
everything: customers, orders, password hashes, sign-in keys. Protect the backup folder exactly like the
database.

## 2. Prove it: the restore drill

Let's restore last night's file into a scratch database, next to the real one, and look inside:

```powershell
$pg = 'C:\Program Files\PostgreSQL\18\bin'
& "$pg\createdb.exe"   -U postgres -O erp_owner erp_restore_check
& "$pg\pg_restore.exe" -U erp_owner -d erp_restore_check D:\Backups\erp\erp-2026-11-06.dump
& "$pg\psql.exe"       -U erp_owner -d erp_restore_check -c "SELECT count(*) FROM identity.users;"
& "$pg\dropdb.exe"     -U postgres erp_restore_check
```

If the count matches what you expect, the backup works. Do this once a month, and after every PostgreSQL
upgrade. Easy, right?

On the day you need it for real, on a new server, it's three steps you already know:

1. Install PostgreSQL and run `create-roles.sql` ([lesson 15](15-deploy-iis.md), step 4). New passwords
   are fine; the role names are what matter.
2. `pg_restore -U erp_owner -d erp` with the latest dump. Every table comes back owned by `erp_owner`, with
   `erp_app`'s rights included.
3. Put the new `erp_app` password into the app pool (lesson 15, step 6).

No migrations are needed. The dump includes each module's migration history, so the next deploy's bundles
know exactly where they left off.

## 3. The checklist: what's done

| Security | Lesson |
|---|---|
| Passwords hashed by ASP.NET Core Identity; lockout after 5 wrong tries; 12-character minimum; no self sign-up | [05](05-sign-in.md) |
| Short-lived bearer tokens with refresh, cookies for the browser, sign out everywhere | [05](05-sign-in.md) |
| Role checks at the endpoint; data-dependent rules in the domain (nobody approves their own order) | [06](06-create-order.md), [08](08-approve-reject-cancel.md) |
| Errors as problem details with a trace id, never a stack trace | [06](06-create-order.md) |
| Rate limits per user and per IP, strict on sign-in | [13](13-security-hardening.md) |
| HTTPS with HSTS, security headers, no server banners | [13](13-security-hardening.md), [15](15-deploy-iis.md) |
| An API key per client app | [13](13-security-hardening.md) |
| Sign-in keys in the database, so restarts sign nobody out | [13](13-security-hardening.md) |
| The app runs as `erp_app` (rows only); only deployments run as `erp_owner` | [13](13-security-hardening.md), [15](15-deploy-iis.md) |

| Correctness | Lesson |
|---|---|
| The credit check counts the balance *and* orders waiting for approval, with the customer row locked | [07](07-submit.md) |
| Stock reserved in one atomic statement: two people can't take the last unit | [07](07-submit.md) |
| Lost updates refused with 409, including edits that only change lines | [08](08-approve-reject-cancel.md), [09](09-update-order.md) |
| Approval is a workflow: pending, then approved or rejected, with a reason | [08](08-approve-reject-cancel.md) |
| Order numbers from a sequence, never `MAX() + 1` | [06](06-create-order.md) |
| No event lost on a crash: the outbox, with idempotent handlers | [11](11-events-outbox.md) |

| Operations | Lesson |
|---|---|
| Structured logs, traces and metrics; a trace id on every response and log line | [12](12-observability.md) |
| `/health/live` and `/health/ready` | [12](12-observability.md) |
| Unit, integration and architecture tests, also run as the least-privilege accounts | [14](14-testing.md) |
| Installs from the docs: migration bundles, first admin from configuration, IIS | [15](15-deploy-iis.md) |
| Nightly backups and a restore drill | 16 |

## 4. The checklist: what's still missing

Now, let's be honest about the rest. The app is solid, but a business can't run on it yet. Here is
everything that's missing, and where it gets built:

| Missing | Why it matters | Where |
|---|---|---|
| Maintaining customers, items and categories | A real install starts empty, so nobody can place an order | Next milestone |
| User management: create users, assign roles, disable, reset passwords | Today, only the first admin exists | Next milestone |
| Sending email | `/auth/forgotPassword` answers 200 and sends nothing | Next milestone |
| Stock receipts, shipments, adjustments | `OnHand` never changes, so reserved stock is held forever | Next milestone |
| Units of measure, with decimal quantities | You can't sell 2.5 kg of anything | Next milestone |
| A record of sign-ins and failed attempts | Nobody can answer "who signed in last night?" | Next milestone |
| Screens: Blazor and MudBlazor, with a content security policy for them | People use screens, not PowerShell | After that |
| Purchasing and Invoicing | The rest of the business | After that. Invoicing first needs a tax adviser to confirm what the BIR requires |
| Sign-in keys encrypted at rest | Today they sit in `identity.data_protection_keys` as plain XML. Anyone who can read that table could forge a sign-in. The fix is a certificate that protects them | Before the first sale |
| A monitoring destination with alerts | Under IIS, console logs go nowhere. Only warnings and errors reach the Event Log | Section 5 |

And two things that aren't defects, but decisions only the business can make:

- **Who sees which orders?** Today a sales rep can see and change every order. If the business wants "only
  your own", that rule depends on the data, so it lives in the handler, using `ICurrentUser`, exactly where
  the "where logic lives" table from [lesson 03](03-domain-model.md) puts it.
- **Two-factor sign-in.** Identity supports authenticator apps. We'll switch it on together with the
  screens, where people can scan the code.

## 5. Where the logs go in production

Under IIS, nobody watches the console, so the JSON logs from [lesson 12](12-observability.md) disappear.
Warnings and errors still reach the Windows Event Log. Everything else needs a destination, and the app
already knows how to send it: set one app pool variable, `OTEL_EXPORTER_OTLP_ENDPOINT`, and logs, traces
and metrics all go there over OTLP.

| What the business needs | What to run |
|---|---|
| "Tell us when it's down" | An uptime monitor that calls `/health/ready` every minute and emails someone when it fails |
| Search logs, follow one request through every step, alerts | An OTLP backend. Self-hosted, free: SigNoz, or the Grafana stack. Hosted, paid: most monitoring services accept OTLP directly |

These are separate programs the business runs next to the ERP. Nothing in the ERP's source depends on
them, so their licenses never pass to a buyer of your code. And the Aspire dashboard from lesson 12 stays a
development tool: it keeps everything in memory, forgets it on restart, and can't send an alert.

## 6. When you'd add the tools we left out

Every tool below solves a real problem. Each one also adds something to run, secure and learn. So the rule
is the one this course has followed since the mediator in [lesson 02](02-mediator.md): add a tool when you
feel the pain it solves, not before.

| Tool | Add it when | Until then |
|---|---|---|
| A message broker (RabbitMQ and friends) | Another *system*, not another module, must receive our events | The outbox already guarantees delivery inside the app. With a broker, the outbox stays, and its dispatcher forwards to the broker. MassTransit 9 and later is paid, so use the broker's own client |
| Redis | You run several servers and per-server limits or caches stop being good enough, or you've *measured* one hot read | One server keeps rate limits in memory just fine, and a cache brings its own bugs: stale data |
| A second server behind a load balancer | One server can't keep up, or a minute of downtime per deploy costs too much | We're mostly ready: sign-in keys are shared, and the outbox dispatcher's `SKIP LOCKED` lets two servers share the work. Add the built-in forwarded-headers middleware, or the rate limiter sees the load balancer's IP instead of the user's |
| Microservices | Separate teams need to release separately | Our modules are already islands with one service window each, so a split would be mechanical. It would also bring network failures and no shared transaction |
| Docker | The buyer's IT runs everything in containers | The product deploys to IIS. A Dockerfile can be offered as an option, never a requirement |
| OpenIddict (Apache-2.0) | Other companies' apps must sign in *your* users: single sign-on, OAuth | Identity covers our own clients |
| Testcontainers | Your build server has no PostgreSQL | The tests use a local PostgreSQL. Testcontainers needs Docker on the build server |

To remember it easily: every tool is the answer to a question. If nobody's asking the question yet, the tool
is just one more thing that can break.

## Mistakes to avoid

- Keeping backups on the same disk as the database.
- Never restoring a backup until the day you need it.
- Calling it production-ready because it works on your machine.
- Adding a tool "because we'll need it later".
- Keeping the "still missing" list in your head. Write it down, and show it to the buyer.

## Let's run it!

Run the backup now, instead of waiting for 1 a.m.:

```powershell
Start-ScheduledTask 'ERP nightly backup'
Get-ScheduledTaskInfo 'ERP nightly backup' | Select-Object LastRunTime, LastTaskResult   # 0 means success
Get-ChildItem D:\Backups\erp
```

There's today's `erp-yyyy-MM-dd.dump`. Now run the restore drill from section 2 against it, and check the
user count. Then, one last time, on your own machine:

```bash
dotnet test
```

```
Passed!  - Failed: 0, Passed: 22, ... Erp.Modules.Sales.UnitTests.dll
Passed!  - Failed: 0, Passed:  6, ... Erp.Architecture.Tests.dll
Passed!  - Failed: 0, Passed: 25, ... Erp.Integration.Tests.dll
```

## Let's check!

1. Why is a backup that's never been restored only a hope?
2. Why does the backup run as `erp_owner`, and why must the roles exist before a restore?
3. Which missing piece would a customer notice first, and why?
4. Why is "who sees which orders" a decision rather than a bug, and where would the rule live?
5. Under IIS, where do the app's logs go if no OTLP endpoint is set?
6. Name one tool this course left out, the pain that would justify it, and what stays when you add it.

And that's where this course ends, for now. You started with an empty folder, and you finished with a
modular monolith that keeps its rules in the domain, its modules on their islands, its events in an outbox,
and its data in a database that's backed up every night. Every piece has a reason, and you know what it is.
The next milestone builds on exactly these patterns. Good luck, developer!

---

← [Lesson 15 — Deploy to IIS](15-deploy-iis.md) · Back to [Start here](00-START-HERE.md)
