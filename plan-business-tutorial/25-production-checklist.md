# Lesson 25 — The Production Checklist: What's Done, What's Left

Good day, developer! The ERP runs the whole business, on a real server, over HTTPS. Today we step back and
walk the production checklist, line by line, honestly: what's done and which lesson did it, the first day of
a new customer's install, the backup and restore drill that proves the data is safe, and what's still
outside the product. About an hour. Are you ready? Let's begin!

## What we'll build

- The production checklist, with the lesson behind every line.
- The first day of a new install, in order.
- The restore drill that proves last night's backup works.
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
| Passwords hashed by ASP.NET Core Identity; lockout after 5 wrong tries; 12-character minimum; no self sign-up | [06](06-sign-in.md) |
| Cookies for the browser (HttpOnly, SameSite=Strict), short-lived bearer tokens with refresh for programs, sign out everywhere; no API keys in the browser | [06](06-sign-in.md) |
| Role checks at the endpoint; data-dependent rules in the domain (nobody approves their own order or purchase) | [07](07-create-order.md), [10](10-approve-reject-cancel.md), [19](19-purchasing.md) |
| A sales rep sees only their own orders; anyone else's is a 404 | [08](08-find-orders.md) |
| Staff accounts by an admin only, temporary passwords, disable instead of delete, no lockout of the last admin, sign-in log | [16](16-users-email.md) |
| Errors as problem details with a trace id, never a stack trace | [07](07-create-order.md) |
| Rate limits per user and per IP, strict on sign-in | [23](23-security-hardening.md) |
| HTTPS with HSTS, security headers, a Content-Security-Policy with no inline scripts, no server banners | [23](23-security-hardening.md), [24](24-deploy-podman.md) |
| Sign-in keys in the database, encrypted with a certificate; the app refuses to start without one | [23](23-security-hardening.md) |
| The app runs as `erp_app` (rows only); only deployments run as `erp_owner` | [23](23-security-hardening.md), [24](24-deploy-podman.md) |

| Correctness | Lesson |
|---|---|
| The credit check counts the balance *and* orders waiting for approval, with the customer row locked | [09](09-submit.md) |
| Stock reserved, shipped and received in single atomic statements: two people can't take the last unit | [09](09-submit.md), [18](18-shipping.md), [19](19-purchasing.md) |
| Lost updates refused with 409, including edits that only change lines | [10](10-approve-reject-cancel.md), [11](11-update-order.md) |
| Approval is a workflow: pending, then approved or rejected, with a reason | [10](10-approve-reject-cancel.md) |
| Units of measure and decimal quantities; money rounded to centavos everywhere | [04](04-domain-model.md) |
| Every stock change audited, with who and why | [17](17-item-catalog.md) |
| No event lost on a crash: the outbox; handlers idempotent (upserts, the issuer's checks, the payments inbox) | [13](13-events-outbox.md), [17](17-item-catalog.md), [20](20-invoicing.md), [21](21-payments.md) |
| Invoice numbers without gaps; invoices never edited, only voided and reissued | [20](20-invoicing.md) |
| A customer's balance follows payments in and out, exactly once | [21](21-payments.md) |

| Tax and invoices | Lesson |
|---|---|
| The invoice prints what BIR RR 7-2024 Sec. 6(B) asks for: registered name, TIN statement, address, the word "Invoice", a serial number, space for the buyer's name, address and TIN, the VAT breakdown, and the permit details at the bottom | [20](20-invoicing.md) |
| The company's registered details, tax regime and VAT rate are business data, kept by the accountant on a screen and checked by the domain; each invoice keeps the seller details and rate it was issued with | [20](20-invoicing.md) |
| Nothing ships until the company profile is complete, so no invoice goes out without the seller's details | [20](20-invoicing.md) |
| Each item is VATable, zero-rated or VAT-exempt; the order line and the invoice line keep that choice | [20](20-invoicing.md) |
| A second print of the same invoice says "REPRINT" | [20](20-invoicing.md) |
| Tax withheld by customers settles their invoices; every BIR Form 2307 is tracked per customer and quarter, with what's still missing | [21](21-payments.md) |
| Senior citizen and PWD discounts (and any other program the business adds): VAT exemption, then 20%, on the order and the invoice alike, with the ID and signature line Sec. 6(B.18) asks for | [22](22-special-discounts.md) |

| Operations | Lesson |
|---|---|
| Structured logs, traces and metrics; a trace id on every response and log line | [14](14-observability.md) |
| `/health/live` and `/health/ready` | [14](14-observability.md) |
| Tests in every lesson: unit, property-based, integration on a throwaway PostgreSQL run as the least-privilege account, architecture, race, browser; coverage and mutation testing; CI on every push | [02](02-testing.md) to [23](23-security-hardening.md), CI in [12](12-containers-ci.md) |
| One-command install and upgrade with Compose on rootless Podman (Docker works unchanged): migrations as a step, HTTPS by Caddy, nightly backups | [24](24-deploy-podman.md) |
| The first admin from configuration, once | [24](24-deploy-podman.md) |
| A restore drill | this lesson |

## 2. The first day of a new install

A fresh install is empty: no customers, no items, no staff. Here's the order that works, because each step
needs the one before it.

| Step | Who | Where |
|---|---|---|
| 1. Fill in the SMTP settings and the initial admin | IT | `.env` |
| 2. Sign in as the initial admin, change the password, remove the `InitialAdmin` settings | IT | Lesson 24 |
| 3. Create the staff accounts, one role each | Admin | Administration → Users |
| 4. Fill in the company's registered name, TIN and address exactly as on its BIR certificate, and its tax regime. Nothing can ship until this is done | Accountant | Invoicing → Company and tax |
| 5. Set up item categories (who may sell what, how much per order) | Sales manager | Sales → Price list → Categories |
| 6. Add the items: SKU, name, unit, fractions or not | Inventory clerk | Inventory → Items and stock |
| 7. Count the opening stock, as adjustments with the reason "Opening count" | Inventory clerk | Adjust stock |
| 8. Price each item, choose its category and VAT treatment, mark what qualifies for special discounts, put it on sale | Sales manager | Sales → Price list |
| 9. Add the customers, with credit limits and terms. For a customer who already owes money, see below | Sales manager | Sales → Customers |
| 10. Add the suppliers | Purchaser | Purchasing → Suppliers |
| 11. Once the BIR has registered the system, fill in the permit number, series range and date issued | Accountant | Invoicing → Company and tax |

Opening balances are the one gap a business notices on day one: a customer who already owes ₱80,000 from
the old system starts at ₱0 here. The honest options today are to give them a lower credit limit until the
old invoices are paid, or to have a developer set `outstanding_balance` once, by SQL, as part of the
migration from the old system, with the change written down. A proper "opening balance" screen is on the
list in section 4.

## 3. Prove it: the restore drill

The install already backs itself up every night ([lesson 24](24-deploy-podman.md)). Now, let's be honest
about what that proves: nothing, until you've restored one. So restore last night's file into a scratch
database next to the real one, and look inside:

```bash
podman compose exec db createdb --username postgres --owner erp_owner erp_restore_check
podman compose exec backup pg_restore --host db --username erp_owner --dbname erp_restore_check --no-owner /backups/erp-2026-11-06.dump
podman compose exec backup psql --host db --username erp_owner --dbname erp_restore_check -c "SELECT count(*) FROM invoicing.invoice;"
podman compose exec db dropdb --username postgres erp_restore_check
```

If the count matches what you expect, the backup works. Do this once a month, and after every PostgreSQL
upgrade. Easy, right?

Two things to watch out for. First, a backup on the same server dies with the server. Have the business's IT
copy the backups somewhere else every day: another server, a NAS, cloud storage. Second, a dump holds
everything: customers, orders, password hashes, the (encrypted) sign-in keys. Protect the backup folder
exactly like the database, and keep the data-protection certificate *apart* from it.

On the day you need it for real, on a new server: install as on day one ([lesson 24](24-deploy-podman.md), with new
passwords if you like; the role names are what matter), then restore the latest dump over the empty
database (lesson 24, step 6, shows the command). No migrations are needed: the dump includes each module's
migration history, so the next upgrade's bundles know exactly where they left off.

## 4. What this product doesn't do yet

Now, let's be honest about the edges. None of these is a shortcut in what's built; each is a feature that
isn't built. Show this list to a buyer before they buy.

| Not built | What the business does meanwhile |
|---|---|
| **BIR registration of the system.** The invoice prints what RR 7-2024 asks for, but invoices from a computer system need the system registered with the BIR (a Computerized Accounting System, with a Permit to Use or an Acknowledgement Certificate) before the first one is issued | Register first, with a tax adviser. Then fill in the permit on **Company and tax**; if the BIR asks for a layout change, it's one file ([InvoicePdf.cs](../course-validation/business/src/Modules/Invoicing/Erp.Modules.Invoicing/Infrastructure/Pdf/InvoicePdf.cs)) |
| The seniors' 5% discount on basic necessities and prime commodities, with its weekly purchase cap and purchase booklet | A business that sells groceries needs it before go-live; others don't sell what it covers |
| Checking that a customer withheld the right rate | The accountant compares each Form 2307 with the payments it covers; the 2307 report shows the totals side by side |
| Opening balances for customers who already owe money | Section 2 |
| Partial shipments (shipping some lines of an order now and the rest later) | Split the order into two orders before approving |
| Returns and credit memos | Void and reissue the invoice for billing mistakes; a physical return is an inventory adjustment with a reason |
| Accounts payable (supplier invoices and payments) | Keep it in the accounting system the business already uses |
| Two-factor sign-in | Identity supports authenticator apps; switching it on needs a "scan this code" screen |
| Reports beyond receivables aging | Read-only SQL against the database, as `erp_app` or a read-only role |
| Multiple currencies, multiple warehouses, multiple companies | One company, one warehouse, pesos |

## 5. Where the logs go in production

Nobody watches a server's console, so the JSON logs from [lesson 14](14-observability.md) need a
destination. On the server, `podman compose logs api` keeps recent logs, which is enough for "what just
happened?". For anything more, the app already knows how to send everything: set one setting,
`OTEL_EXPORTER_OTLP_ENDPOINT` in `.env`, and logs, traces and metrics all go there over OTLP.

| What the business needs | What to run |
|---|---|
| "Tell us when it's down" | An uptime monitor that calls `/health/ready` every minute and emails someone when it fails |
| Search logs, follow one request through every step, alerts | An OTLP backend. Self-hosted, free: SigNoz, or the Grafana stack, each as a few more containers next to the ERP. Hosted, paid: most monitoring services accept OTLP directly |
| Secrets in a vault instead of `.env` | Compose secrets for more settings, or a vault (OpenBao, HashiCorp Vault) when the business has one |

These are separate programs the business runs next to the ERP. Nothing in the ERP's source depends on
them, so their licenses never pass to a buyer of your code. And the Aspire dashboard from lesson 14 stays a
development tool: it keeps everything in memory, forgets it on restart, and can't send an alert.

## 6. When you'd add the tools we left out

Every tool below solves a real problem. Each one also adds something to run, secure and learn. So the rule
is the one this course has followed since it chose no mediator in [lesson 03](03-features.md): add a tool
when you feel the pain it solves, not before.

| Tool | Add it when | Until then |
|---|---|---|
| A message broker (RabbitMQ and friends) | Another *system*, not another module, must receive our events | The outbox already guarantees delivery inside the app. With a broker, the outbox stays, and its dispatcher forwards to the broker. MassTransit 9 and later is paid, so use the broker's own client |
| Redis | You run several servers and per-server limits or caches stop being good enough, or you've *measured* one hot read | One server keeps rate limits in memory just fine, and a cache brings its own bugs: stale data |
| A second app server behind a load balancer | One server can't keep up, or a few seconds of downtime per upgrade cost too much | Mostly ready: sign-in keys are shared through the database, and the dispatcher's `SKIP LOCKED` lets two servers share the work. Per-server rate limits become per-server, though |
| Kubernetes | The business runs many apps on a cluster already | Compose on one host is the whole requirement. The same images run on a cluster unchanged |
| Microservices | Separate teams need to release separately | Our modules are already islands with one service window each, so a split would be mechanical. It would also bring network failures and no shared transaction |
| OpenIddict (Apache-2.0) | Other companies' apps must sign in *your* users (single sign-on, OAuth), or a machine client needs client credentials | Identity covers our own users and programs |

To remember it easily: every tool is the answer to a question. If nobody's asking the question yet, the tool
is just one more thing that can break.

## Mistakes to avoid

- Keeping backups on the same disk as the database.
- Keeping the data-protection certificate in the same place as the backups.
- Never restoring a backup until the day you need it.
- Issuing invoices before the system is registered with the BIR and its permit is in the company profile.
- Changing the VAT rate and expecting old invoices to change. They keep the rate they were issued with,
  which is exactly what the BIR expects.
- Calling it production-ready because it works on your machine.
- Adding a tool "because we'll need it later".
- Keeping the "not built yet" list in your head. Write it down, and show it to the buyer.

## Let's run it!

Run a backup now, instead of waiting for 1 a.m. (the `pg_dump` line from lesson 24), and run the restore
drill from section 3 against it. Then, one last time, on your own machine:

```bash
dotnet test
```

```
Test run summary: Passed!
  total: 297
  failed: 0
  succeeded: 289
  skipped: 8
```

That's 161 unit tests across the five modules, 14 architecture tests and 114 integration tests. The 8 skipped
are the browser tests, which wait for a running app:

```bash
ERP_UI_URL=http://localhost:5264 dotnet test --project tests/Erp.Web.UiTests
```

```
Test run summary: Passed!
  total: 8
  failed: 0
  succeeded: 8
  skipped: 0
```

Your counts will match these if you wrote every test the lessons showed, including the edge-case tables; the
reference build has exactly these.

## Let's check!

1. Why is a backup that's never been restored only a hope?
2. Why must the data-protection certificate be kept apart from the backups?
3. A new install: why are items added before prices, and prices before the first order?
4. What must a business do with the BIR before it issues invoices from this system, and where do the results go?
5. Where do the app's logs go in production if no OTLP endpoint is set?
6. Name one tool this course left out, the pain that would justify it, and what stays when you add it.

And that's where this course ends. You started with an empty folder, and you finished with an ERP a
business can run: sales with credit checks and approvals, a warehouse with an audited catalog, purchasing,
invoicing and payments, all as a modular monolith that keeps its rules in the domain, its modules on their
islands, its events in an outbox, and its data in a database that's backed up every night. Every piece has
a reason, and you know what it is. Good luck, developer!

---

← [Lesson 24 — Deploying with Podman](24-deploy-podman.md) · Back to [Start here](00-START-HERE.md)
