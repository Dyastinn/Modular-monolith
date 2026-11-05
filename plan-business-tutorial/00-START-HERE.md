# 00 — Start Here: Build an ERP You Could Sell

Good day, developer! Welcome to the course. Over the next lessons, you'll build the sales side of an ERP —
the kind a real company runs every day. Customers with credit limits. Stock that two people can't sell
twice. Managers approving big orders. Sign-in with lockout. Logs and traces you can actually debug with.
Not a toy and not a sketch: every line is something you type and understand. Are you ready? Let's begin!

## What we'll build

A sales rep creates an order for a customer. The system checks the rules. Small orders are approved on the
spot; big ones wait for a manager. And the moment an order is submitted, its stock is reserved, so the
warehouse can ship it.

These are the endpoints you'll have at the end:

| Endpoint | Who can call it | What it does |
|---|---|---|
| `POST /auth/login` | anyone | Sign in; get an access token and a refresh token (or a cookie) |
| `POST /auth/refresh` | holder of a refresh token | Get a fresh access token |
| `GET /auth/me` | signed in | Who am I, and what's my role |
| `POST /auth/manage/info` | signed in | Change my password |
| `POST /auth/logout-everywhere` | signed in | End every session I have, on every device |
| `POST /sales-orders` | SalesRep, SalesManager, Admin | Create a draft order |
| `PUT /sales-orders/{id}` | SalesRep, SalesManager, Admin | Replace a draft's lines |
| `POST /sales-orders/{id}/submit` | SalesRep, SalesManager, Admin | Submit: reserve stock, approve it if it's small |
| `POST /sales-orders/{id}/approve` | SalesManager, Admin | Approve a big order (but never your own) |
| `POST /sales-orders/{id}/reject` | SalesManager, Admin | Reject with a reason; the stock is released |
| `POST /sales-orders/{id}/cancel` | SalesRep, SalesManager, Admin | Cancel; stock released, charge reversed |
| `GET /sales-orders/{id}` | signed in | One order in full |
| `GET /sales-orders` | signed in | Search, filter and page through orders |
| `GET /health/live`, `/health/ready` | anyone | Is it up? Can it reach the database? |

There are three roles, and these are their exact spellings: **`SalesRep`**, **`SalesManager`** and
**`Admin`**. When a lesson says "a manager", it always means `SalesManager`. `Admin` can do everything.

## The business rules

Here's the heart of the course. None of this is plain create-read-update-delete. Each rule raises the
same question: **where should this logic live?** Learning to answer that well is the skill we're here for.

1. **Sign-in** is required for every sales endpoint. Five wrong passwords lock an account for 15 minutes.
2. **Roles:** only a SalesManager (or an Admin) approves or rejects.
3. **Blocked customers** can't order.
4. **Credit limit:** what the customer owes, plus their orders still waiting for approval, plus this order,
   must fit within their limit.
5. **Stock** is reserved the moment an order is submitted, and two reps can never both take the last five
   units.
6. **Item categories** can be limited to a role, and capped per order.
7. **Approval:** an order of ₱50,000 or less is approved automatically when submitted. Anything bigger waits
   for a manager, and nobody approves their own order.
8. **Rejecting or cancelling** releases the reserved stock. Cancelling an approved order also takes the
   charge off the customer's balance.
9. **All or nothing:** reserving stock for five lines and changing the order's status succeed together, or
   not at all.
10. **No lost updates:** if two people edit the same draft at the same time, the second save is refused.

## The tools we'll use

You don't need to understand this table yet. Each tool is explained in the lesson that first needs it.

| What for | Tool |
|---|---|
| Language and runtime | **.NET 10**, C# |
| How the code is organized | A **modular monolith** (one app, walled-off modules) with **vertical slices** (one folder per feature) |
| Endpoints | **Carter**, on top of ASP.NET Core minimal APIs |
| Database | **PostgreSQL**, one schema per module |
| Saving data | **EF Core** |
| Reading data for screens | **Dapper**, calling SQL functions |
| Sending requests to handlers | **Mediator** (`martinothamar/Mediator`) |
| Checking requests | **FluentValidation** |
| Sign-in | **ASP.NET Core Identity** |
| Reacting to events reliably | A **transactional outbox** |
| Seeing what the app is doing | **OpenTelemetry**, with the **Aspire** dashboard while developing |
| Tests | **xUnit** and **AwesomeAssertions** |
| Hosting | **IIS** on Windows Server |

Every one of these is free and openly licensed (MIT, Apache-2.0 or the PostgreSQL license). That matters:
it means you can build this and sell it, and your buyer owes nobody a license fee. The reference build's
[THIRD-PARTY-NOTICES.md](../course-validation/business/THIRD-PARTY-NOTICES.md) lists them all.

A few popular tools are left out on purpose: Clean Architecture's four-project split, a generic
`Repository<T>`, message brokers like RabbitMQ, Redis, microservices, and Docker as a requirement. Each is
good somewhere. [Lesson 25](25-production-checklist.md) tells you when you'd actually reach for them.

If you came from WebForms, you'll feel at home faster than you think. Your `[WebMethod]` becomes a Carter
endpoint, your `Button_Click` code-behind becomes a handler, and your `SqlCommand` and `DataSet` become EF
Core for saving and Dapper for reading. The business rules you used to scatter across pages move into one
place: the domain model.

## The lessons

Each lesson builds on the one before, and each one ends with something you run and see working.

| # | Lesson | What you'll build |
|---|---|---|
| 01 | [Solution setup](01-solution-setup.md) | The projects, the module walls, central package management |
| 02 | [Mediator](02-mediator.md) | Commands, queries, handlers, pipeline behaviors |
| 03 | [Domain model](03-domain-model.md) | The sales rules in plain C#, with unit tests from day one |
| 04 | [Database](04-database.md) | PostgreSQL, EF Core, migrations, seed data |
| 05 | [Sign-in](05-sign-in.md) | ASP.NET Core Identity: login, refresh, lockout, roles |
| 06 | [Create an order](07-create-order.md) | The first real feature from end to end, with proper error responses |
| 07 | [Submit](09-submit.md) | The Inventory module, safe stock reservation, one transaction across two modules |
| 08 | [Approve, reject, cancel](10-approve-reject-cancel.md) | The approval workflow |
| 09 | [Update an order](11-update-order.md) | Lost updates, and how PostgreSQL's `xmin` stops them |
| 10 | [Reads](08-find-orders.md) | Dapper and SQL functions, shipped by migrations |
| 11 | [Events and the outbox](12-events-outbox.md) | Reacting to what happened, reliably: domain events and a transactional outbox |
| 12 | [Observability](14-observability.md) | Traces, metrics, logs, health checks, the Aspire dashboard |
| 13 | [Security hardening](22-security-hardening.md) | Rate limits, HTTPS, security headers, a least-privilege database account |
| 14 | [Testing](13-testing.md) | Integration tests and architecture tests |
| 15 | [Deploy to IIS](24-deploy-iis.md) | A real Windows Server install |
| 16 | [Production checklist](25-production-checklist.md) | What "done" means, and what comes next |

All together, it's about 20 to 25 hours of focused work. Lessons 03, 04, 05 and 07 are the heaviest, so
start those when you're fresh. If C# itself is new to you, read [00a-FOUNDATIONS.md](00a-FOUNDATIONS.md)
first, and give yourself half again as much time.

## How each lesson goes

Every lesson walks the same path, so you always know where you are:

1. **What we'll build** — the result.
2. **Why we build it this way** — the obvious approach first, why it hurts, then the better way.
3. **The steps** — the code, in full, with where each file goes.
4. **How a request flows** — following one request through the code.
5. **Mistakes to avoid.**
6. **Let's run it!** — you run it and see it work.
7. **Let's check!** — questions to answer from memory. The answers are in [ANSWER-KEY.md](ANSWER-KEY.md),
   but try first. Pulling the answer out of your own head is what makes it stick.

## A few tips before we start

- **Go in order.** Each lesson expects the one before it to build and run.
- **Type the code, don't paste it.** Fixing the compiler's complaints is where the learning happens.
- **The run commands use Git Bash** (`curl`, `$(...)`, `jq`), because that's the form you'll see in every
  API's documentation. Prefer PowerShell? [00b-WINDOWS-POWERSHELL.md](00b-WINDOWS-POWERSHELL.md) has every
  command translated.
- **Stuck on a word?** Look it up in [GLOSSARY.md](GLOSSARY.md).
- **Want to compare with the finished thing?** [`course-validation/business/`](../course-validation/business/)
  is the complete, tested reference build. The repo [README](../README.md) shows how to run it.
- **No front end yet?** Open [`test-console/index.html`](../course-validation/business/test-console/index.html)
  straight from disk while the API runs, and you can drive every endpoint from a browser.

## What you need installed

| Tool | How to check |
|---|---|
| **.NET 10 SDK** | `dotnet --version` prints `10.x` |
| **PostgreSQL 16 or newer** | [Lesson 04](04-database.md) walks you through installing it |
| **An editor** | Visual Studio 2022, VS Code with C# Dev Kit, or Rider |
| **Git Bash and `jq`** | `jq --version` (Git Bash comes with Git for Windows) |

And that's the whole plan. Next, we set up the solution and put up the walls between the modules. Good
luck, developer!

---

Next: [Lesson 01 — Solution setup](01-solution-setup.md) → · New to C#? [Foundations](00a-FOUNDATIONS.md)
