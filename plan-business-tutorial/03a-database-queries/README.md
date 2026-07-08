# Phase 3 — companion SQL (`03a-database-queries`)

> This folder is the **runnable, re-runnable** version of every SQL block in
> [Phase 3 — The Database](../03-database-schema.md). The lesson explains *why* each table, proc and
> constraint exists; these files are the *what* you actually run. Same SQL, extracted so you can execute it
> without copy-pasting out of the prose.

Two things differ from the inline SQL in the lesson, both on purpose — read this before you run anything.

## 1. Every statement is idempotent (guarded)

In the base lesson, scripts are applied by **DbUp**, which records what already ran in a `SchemaVersions`
table and skips it — so the individual `CREATE TABLE` statements don't *need* guards (§7 of the lesson).

These companion scripts assume **DbUp is optional**. You might redeploy with plain `sqlcmd`, a batch
file, or a CI step that just re-runs the folder. So every statement here is written to be safe to run
**any number of times**:

- Schemas → `IF SCHEMA_ID(...) IS NULL EXEC('CREATE SCHEMA ...')`
- Tables / sequences → `IF OBJECT_ID(...) IS NULL CREATE ...`
- Indexes → `IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE ...) CREATE INDEX ...`
- Procedures / triggers → `CREATE OR ALTER` (re-run replaces the definition — already idempotent)
- Seed rows → `IF NOT EXISTS (SELECT 1 FROM <table>) BEGIN INSERT ... END` (insert once, never duplicate)

> 🔑 Idempotent at the **statement** level (these files) vs. idempotent at the **runner** level (DbUp,
> the base lesson). Both get you "safe to re-run"; they just put the safety in different places. Use these
> guarded scripts when you *don't* have a tracking runner; use the plain lesson scripts + DbUp when you do.

## 2. Every table gets an audit trigger — **logs only, no business logic**

Each schema has a `ChangeLog` table, and each real table has one `AFTER INSERT, UPDATE, DELETE` trigger
that writes one row per change (which table, which key, insert/update/delete, when, and which DB login).
See `*/004_audit.sql`.

> ⚠️ **Read this carefully — it's the one place this course puts a trigger, and the boundary matters.**
> These triggers exist **only to write an append-only audit trail** — a safety net for the DBA ("who
> touched this row, and when?"). They contain **no business logic, no policy, no validation, no cascading
> writes to business tables**. Nothing about *whether* an order may be approved, whether a customer is
> over their credit limit, or any status rule lives in a trigger. All of that stays in the domain (C#),
> exactly as the rest of the course insists.
>
> 🔑 So the course's rule is intact: **no hidden *business* logic in the database.** A pure audit trigger
> isn't business behavior — it's the database logging its own changes, the same way a file server logs
> access. It doesn't change what the application does; it just records what happened. That's why it's a
> *good* example rather than a violation: it shows you can have an audit trail **without** smuggling
> decisions into the database.
>
> ❓ **"Then why not audit with domain events, like everything else that 'reacts'?"** You can, and for
> *business* facts ("order approved by user 42") you should — domain events carry the app user and intent
> ([Phase 8](../08-approval-cancellation.md)). A DB trigger captures something different and complementary:
> the **raw row change as the database saw it**, including writes that bypass the app entirely (a DBA
> hot-fix, a bulk `UPDATE`). One records *intent* (events); the other records *what physically changed*
> (triggers). `ChangedBy` here is the **database login**, not the app user — because a trigger can't see
> your JWT. App-user attribution is the domain event's job.

## Run order

Cross-schema references force an order (`sales` references `[identity].AppUser`; `inventory` seed uses
item ids from `sales`). Run each module's files `001 → 004`, identity first:

```bash
# Identity (host/shared)
sqlcmd -S "(localdb)\MSSQLLocalDB" -d Erp -i Identity/001_schema.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -d Erp -i Identity/002_procedures.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -d Erp -i Identity/003_seed.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -d Erp -i Identity/004_audit.sql
# Sales
sqlcmd -S "(localdb)\MSSQLLocalDB" -d Erp -i Sales/001_schema.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -d Erp -i Sales/002_procedures.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -d Erp -i Sales/003_seed.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -d Erp -i Sales/004_audit.sql
# Inventory
sqlcmd -S "(localdb)\MSSQLLocalDB" -d Erp -i Inventory/001_schema.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -d Erp -i Inventory/002_procedures.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -d Erp -i Inventory/003_seed.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -d Erp -i Inventory/004_audit.sql
```

Because every statement is guarded, re-running the whole folder is a no-op on an already-built database.

## Scope note

`002_procedures.sql` contains **only the procs whose bodies Phase 3 actually defines** (the insert, the
atomic reserve, the version-checked status update, the order-number sequence, the balance adjust, the
release, and the refresh-token procs). The remaining procs listed in the Phase 3 proc table
(`usp_Item_GetByIds`, `usp_SalesOrder_GetDetail`, `usp_SalesOrders_Search`, …) are defined in the later
phases that introduce them — append them to these files as you reach them, keeping the same append-only
habit the lesson teaches.
