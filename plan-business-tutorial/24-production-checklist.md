# Lesson 24 — The Production Checklist: What's Done, What's Left

Good day, developer! The ERP runs the whole business, on a real server, over HTTPS. Today we step back and
walk the production checklist, line by line, honestly: what's done and which lesson did it, the first day of
a new customer's install, the backup and restore drill that proves the data is safe, and what's still
outside the product. About an hour. Are you ready? Let's begin!

## What we'll build

- The production checklist, with the lesson behind every line.
- The first day of a new install, in order.
- Backups for an IIS install, and a restore drill for either install.
- Where the logs go in production.
- The honest list of what this product doesn't do yet, and the tools it deliberately left out.

## Why we build it this way

"Is it production-ready?" is a feeling until it's a list. A list with a lesson next to every line can be
checked by anyone: you in six months, a buyer, their IT person.

And the backup drill is on it because everything else on the list protects the app from attackers and from
mistakes in the code. Nothing on it protects the business from a dead disk, a migration that went wrong, or
someone deleting the wrong thing. Only a backup does. And a backup that has never been restored isn't a
backup yet; it's a hope.

## 1. The checklist: what's done

| Security | Lesson |
|---|---|
| Passwords hashed by ASP.NET Core Identity; lockout after 5 wrong tries; 12-character minimum; no self sign-up | [05](05-sign-in.md) |
| Cookies for the browser (HttpOnly, SameSite=Strict), short-lived bearer tokens with refresh for programs, sign out everywhere; no API keys in the browser | [05](05-sign-in.md) |
| Role checks at the endpoint; data-dependent rules in the domain (nobody approves their own order or purchase) | [06](06-create-order.md), [09](09-approve-reject-cancel.md), [18](18-purchasing.md) |
| A sales rep sees only their own orders; anyone else's is a 404 | [07](07-find-orders.md) |
| Staff accounts by an admin only, temporary passwords, disable instead of delete, no lockout of the last admin, sign-in log | [15](15-users-email.md) |
| Errors as problem details with a trace id, never a stack trace | [06](06-create-order.md) |
| Rate limits per user and per IP, strict on sign-in | [21](21-security-hardening.md) |
| HTTPS with HSTS, security headers, a Content-Security-Policy with no inline scripts, no server banners | [21](21-security-hardening.md), [22](22-docker.md), [23](23-deploy-iis.md) |
| Sign-in keys in the database, encrypted with a certificate; the app refuses to start without one | [21](21-security-hardening.md) |
| The app runs as `erp_app` (rows only); only deployments run as `erp_owner` | [21](21-security-hardening.md), [22](22-docker.md) |

| Correctness | Lesson |
|---|---|
| The credit check counts the balance *and* orders waiting for approval, with the customer row locked | [08](08-submit.md) |
| Stock reserved, shipped and received in single atomic statements: two people can't take the last unit | [08](08-submit.md), [17](17-shipping.md), [18](18-purchasing.md) |
| Lost updates refused with 409, including edits that only change lines | [09](09-approve-reject-cancel.md), [10](10-update-order.md) |
| Approval is a workflow: pending, then approved or rejected, with a reason | [09](09-approve-reject-cancel.md) |
| Units of measure and decimal quantities; money rounded to centavos everywhere | [03](03-domain-model.md) |
| Every stock change audited, with who and why | [16](16-item-catalog.md) |
| No event lost on a crash: the outbox; handlers idempotent (upserts, the issuer's checks, the payments inbox) | [11](11-events-outbox.md), [16](16-item-catalog.md), [19](19-invoicing.md), [20](20-payments.md) |
| Invoice numbers without gaps; invoices never edited, only voided and reissued | [19](19-invoicing.md) |
| A customer's balance follows payments in and out, exactly once | [20](20-payments.md) |

| Operations | Lesson |
|---|---|
| Structured logs, traces and metrics; a trace id on every response and log line | [13](13-observability.md) |
| `/health/live` and `/health/ready` | [13](13-observability.md) |
| Unit, integration, architecture and browser tests, also run as the least-privilege accounts | [12](12-testing.md), [21](21-security-hardening.md) |
| One-command install and upgrade with Docker Compose: migrations as a step, HTTPS by Caddy, nightly backups | [22](22-docker.md) |
| The IIS alternative, from the docs alone | [23](23-deploy-iis.md) |
| The first admin from configuration, once | [22](22-docker.md) |
| A restore drill | this lesson |

## 2. The first day of a new install

A fresh install is empty: no customers, no items, no staff. Here's the order that works, because each step
needs the one before it.

| Step | Who | Where |
|---|---|---|
| 1. Fill in the company's name, TIN and address, and the SMTP settings | IT | `.env` (Docker) or the app pool (IIS) |
| 2. Sign in as the initial admin, change the password, remove the `InitialAdmin` settings | IT | Lessons 22 and 23 |
| 3. Create the staff accounts, one role each | Admin | Administration → Users |
| 4. Set up item categories (who may sell what, how much per order) | Sales manager | Sales → Price list → Categories |
| 5. Add the items: SKU, name, unit, fractions or not | Inventory clerk | Inventory → Items and stock |
| 6. Count the opening stock, as adjustments with the reason "Opening count" | Inventory clerk | Adjust stock |
| 7. Price each item, choose its category, put it on sale | Sales manager | Sales → Price list |
| 8. Add the customers, with credit limits and terms. For a customer who already owes money, see below | Sales manager | Sales → Customers |
| 9. Add the suppliers | Purchaser | Purchasing → Suppliers |

Opening balances are the one gap a business notices on day one: a customer who already owes ₱80,000 from
the old system starts at ₱0 here. The honest options today are to give them a lower credit limit until the
old invoices are paid, or to have a developer set `outstanding_balance` once, by SQL, as part of the
migration from the old system, with the change written down. A proper "opening balance" screen is on the
list in section 5.

## 3. Backups for an IIS install

The Docker install backs itself up every night (lesson 22). On IIS, Windows does it with a scheduled task.

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

Whichever install you chose, two things to watch out for. First, a backup on the same server dies with the
server. Have the business's IT copy the backups somewhere else every day: another server, a NAS, cloud
storage. Second, a dump holds everything: customers, orders, password hashes, the (encrypted) sign-in keys.
Protect the backup folder exactly like the database, and keep the data-protection certificate *apart* from
it.

## 4. Prove it: the restore drill

Restore last night's file into a scratch database next to the real one, and look inside. On IIS:

```powershell
$pg = 'C:\Program Files\PostgreSQL\18\bin'
& "$pg\createdb.exe"   -U postgres -O erp_owner erp_restore_check
& "$pg\pg_restore.exe" -U erp_owner -d erp_restore_check --no-owner D:\Backups\erp\erp-2026-11-06.dump
& "$pg\psql.exe"       -U erp_owner -d erp_restore_check -c "SELECT count(*) FROM invoicing.invoice;"
& "$pg\dropdb.exe"     -U postgres erp_restore_check
```

With Docker, the same four commands run inside the `db` container:

```bash
docker compose exec db createdb --username postgres --owner erp_owner erp_restore_check
docker compose exec backup pg_restore --host db --username erp_owner --dbname erp_restore_check --no-owner /backups/erp-2026-11-06.dump
docker compose exec backup psql --host db --username erp_owner --dbname erp_restore_check -c "SELECT count(*) FROM invoicing.invoice;"
docker compose exec db dropdb --username postgres erp_restore_check
```

If the count matches what you expect, the backup works. Do this once a month, and after every PostgreSQL
upgrade. Easy, right?

On the day you need it for real, on a new server: install as on day one (lesson 22 or 23, with new
passwords if you like; the role names are what matter), then restore the latest dump over the empty
database (lesson 22, step 6, shows the command). No migrations are needed: the dump includes each module's
migration history, so the next upgrade's bundles know exactly where they left off.

## 5. What this product doesn't do yet

Now, let's be honest about the edges. None of these is a shortcut in what's built; each is a feature that
isn't built. Show this list to a buyer before they buy.

| Not built | What the business does meanwhile |
|---|---|
| **BIR registration of the invoices.** Using the system's invoices as official receipts generally needs the system registered with the BIR (as a Computerized Accounting System) and its printed format approved | Have a tax adviser confirm what's required for the business before issuing invoices from it. The invoice layout is one file (`InvoicePdf.cs`), easy to adjust to what the BIR approves |
| Opening balances for customers who already owe money | Section 2 |
| Partial shipments (shipping some lines of an order now and the rest later) | Split the order into two orders before approving |
| Returns and credit memos | Void and reissue the invoice for billing mistakes; a physical return is an inventory adjustment with a reason |
| Accounts payable (supplier invoices and payments) | Keep it in the accounting system the business already uses |
| Two-factor sign-in | Identity supports authenticator apps; switching it on needs a "scan this code" screen |
| Reports beyond receivables aging | Read-only SQL against the database, as `erp_app` or a read-only role |
| Multiple currencies, multiple warehouses, multiple companies | One company, one warehouse, pesos |

## 6. Where the logs go in production

Nobody watches a server's console, so the JSON logs from [lesson 13](13-observability.md) need a
destination. In Docker, `docker compose logs api` keeps recent logs, which is enough for "what just
happened?". Under IIS, warnings and errors reach the Windows Event Log and the rest disappears. For anything
more, the app already knows how to send everything: set one setting, `OTEL_EXPORTER_OTLP_ENDPOINT` (in
`.env` or the app pool), and logs, traces and metrics all go there over OTLP.

| What the business needs | What to run |
|---|---|
| "Tell us when it's down" | An uptime monitor that calls `/health/ready` every minute and emails someone when it fails |
| Search logs, follow one request through every step, alerts | An OTLP backend. Self-hosted, free: SigNoz, or the Grafana stack, each as a few more containers next to the ERP. Hosted, paid: most monitoring services accept OTLP directly |
| Secrets in a vault instead of `.env` or the app pool | Docker secrets for more settings, or a vault (OpenBao, HashiCorp Vault) when the business has one |

These are separate programs the business runs next to the ERP. Nothing in the ERP's source depends on
them, so their licenses never pass to a buyer of your code. And the Aspire dashboard from lesson 13 stays a
development tool: it keeps everything in memory, forgets it on restart, and can't send an alert.

## 7. When you'd add the tools we left out

Every tool below solves a real problem. Each one also adds something to run, secure and learn. So the rule
is the one this course has followed since the mediator in [lesson 02](02-mediator.md): add a tool when you
feel the pain it solves, not before.

| Tool | Add it when | Until then |
|---|---|---|
| A message broker (RabbitMQ and friends) | Another *system*, not another module, must receive our events | The outbox already guarantees delivery inside the app. With a broker, the outbox stays, and its dispatcher forwards to the broker. MassTransit 9 and later is paid, so use the broker's own client |
| Redis | You run several servers and per-server limits or caches stop being good enough, or you've *measured* one hot read | One server keeps rate limits in memory just fine, and a cache brings its own bugs: stale data |
| A second app server behind a load balancer | One server can't keep up, or a few seconds of downtime per upgrade cost too much | Mostly ready: sign-in keys are shared through the database, and the dispatcher's `SKIP LOCKED` lets two servers share the work. Per-server rate limits become per-server, though |
| Kubernetes | The business runs many apps on a cluster already | Compose on one host is the whole requirement. The same images run on a cluster unchanged |
| Microservices | Separate teams need to release separately | Our modules are already islands with one service window each, so a split would be mechanical. It would also bring network failures and no shared transaction |
| OpenIddict (Apache-2.0) | Other companies' apps must sign in *your* users (single sign-on, OAuth), or a machine client needs client credentials | Identity covers our own users and programs |
| Testcontainers | Your build server has no PostgreSQL but has Docker | The tests use a local PostgreSQL |

To remember it easily: every tool is the answer to a question. If nobody's asking the question yet, the tool
is just one more thing that can break.

## Mistakes to avoid

- Keeping backups on the same disk as the database.
- Keeping the data-protection certificate in the same place as the backups.
- Never restoring a backup until the day you need it.
- Issuing official invoices before confirming the BIR requirements.
- Calling it production-ready because it works on your machine.
- Adding a tool "because we'll need it later".
- Keeping the "not built yet" list in your head. Write it down, and show it to the buyer.

## Let's run it!

Run a backup now, instead of waiting for 1 a.m. (`Start-ScheduledTask 'ERP nightly backup'` on IIS, or the
`pg_dump` line from lesson 22 with Docker), and run the restore drill from section 4 against it. Then, one
last time, on your own machine:

```bash
dotnet test
```

```
Passed!  - Failed: 0, Passed: 23, ... Erp.Modules.Sales.UnitTests.dll
Passed!  - Failed: 0, Passed:  5, ... Erp.Modules.Inventory.UnitTests.dll
Passed!  - Failed: 0, Passed:  6, ... Erp.Modules.Purchasing.UnitTests.dll
Passed!  - Failed: 0, Passed:  7, ... Erp.Modules.Invoicing.UnitTests.dll
Passed!  - Failed: 0, Passed:  4, ... Erp.Modules.Identity.UnitTests.dll
Passed!  - Failed: 0, Passed:  7, ... Erp.Architecture.Tests.dll
Passed!  - Failed: 0, Passed: 35, ... Erp.Integration.Tests.dll
```

and, with the app running, the browser tests:

```bash
ERP_UI_URL=http://localhost:5264 dotnet test tests/Erp.Web.UiTests
# Passed!  - Failed: 0, Passed: 3
```

Your counts will match these if you wrote every test the lessons showed; the reference build has exactly
these.

## Let's check!

1. Why is a backup that's never been restored only a hope?
2. Why must the data-protection certificate be kept apart from the backups?
3. A new install: why are items added before prices, and prices before the first order?
4. What should a business confirm with a tax adviser before it issues invoices from this system?
5. Where do the app's logs go in production if no OTLP endpoint is set, on Docker and on IIS?
6. Name one tool this course left out, the pain that would justify it, and what stays when you add it.

And that's where this course ends. You started with an empty folder, and you finished with an ERP a
business can run: sales with credit checks and approvals, a warehouse with an audited catalog, purchasing,
invoicing and payments, all as a modular monolith that keeps its rules in the domain, its modules on their
islands, its events in an outbox, and its data in a database that's backed up every night. Every piece has
a reason, and you know what it is. Good luck, developer!

---

← [Lesson 23 — Deploy to IIS](23-deploy-iis.md) · Back to [Start here](00-START-HERE.md)
