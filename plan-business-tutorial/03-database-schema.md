# Phase 3 — The Database: Schema, Stored Procedures, Seed Data

> Self-contained: this phase explains everything it uses. All monetary values are in **Philippine Pesos
> (₱)** — this ERP is for a company that bills in PHP.
>
> Two ideas run underneath this whole phase, and both are explained in full where we first need them: the
> **"integrity in SQL, policy in the domain"** split (a `CHECK` constraint and a business rule are not the
> same thing, and confusing them is a classic mistake), and **ordered SQL scripts instead of EF migrations**
> (why we version the schema by hand, in numbered files).

---

> ⏱️ **Time for this phase:** ~1–1.5 hr. Mostly typing SQL (tables, procs, seed) and running the scripts.
> Add time if installing/configuring SQL Server or LocalDB is new to you.

## 1. What we're building

The complete SQL Server database for our modules: tables for users, customers, item categories, items,
inventory, sales orders and their lines — plus the stored procedures our repositories will call, and seed
data so we have something to test with. Everything lives as **ordered `.sql` scripts kept in each module's
`Infrastructure/Sql/` folder** so the same files build your dev database, your test database, and (later)
production.

> 🧱 **One database, several schemas.** This is a modular monolith, so it's *one* SQL Server database, but
> each module owns its own **schema**: `sales` (customers, items, orders), `inventory` (stock), and
> `identity` (users, a host/shared concern). The schema prefix is the database-level echo of the module
> walls — and no module's queries reach across into another's schema (that's the rule
> [Phase 13](13-wrap-up-checklist.md) protects). In Phase 7 the `inventory` schema becomes the Inventory
> module's exclusively.

> 🧭 **From WebForms:** this is your `App_Data` / DB project, but organized so each department owns its own
> tables instead of one big shared `dbo` everyone pokes at.

## 2. Why it's designed this way

Three principles decide what goes in this schema, and — just as importantly — what doesn't:

- **Integrity lives in SQL.** Foreign keys, `NOT NULL`, `CHECK` constraints, and unique indexes enforce
  data correctness *no matter what code touches the database*. We don't trust the app to never insert
  garbage — the schema refuses it.
- **Policy lives in the domain (C#).** "Only draft orders can be edited," "an order over ₱50,000 is flagged
  high-value" — those are *not* in the database. If you enforced them **both** in the domain *and* in SQL,
  you'd have the same rule written in two languages that must be kept in perfect sync forever — a
  **"split-brain"**: change the threshold in C# but forget the `CHECK`, and the two halves now disagree and
  fight. So each *policy* rule gets exactly **one home**, the domain; the database enforces *integrity*
  (shapes and relationships), not policy.
- **One exception, on purpose:** the *atomic inventory guard* (reserve only if available) **is** in SQL,
  because only the database can make it safe under concurrency. That's [Phase 7](07-submit-reserve-concurrency.md)'s
  whole lesson, and its proc lives here.

> ❓ **You might be wondering: "Why stored procedures instead of just writing SQL strings in C#?"** Three
> reasons a senior cares about: (1) the SQL lives in *one place* the DBA can review and tune; (2) it
> keeps SQL out of your C# so the two don't tangle; (3) it gives a clean, named boundary — your
> repository calls `usp_SalesOrder_Insert`, not a wall of inline text. It's the same "explicit, visible
> data access" value Dapper gives us, taken one step further.

## 3. The tables (each module's `Infrastructure/Sql/001_schema.sql`)

### The big picture first (a map, not a memory test)

Before any DDL, here's the whole design at a glance — three schemas, and what each table is *for*. Read this
as a map; you don't need to memorize it, and you'll meet each table again in the phase that actually uses it:

```
identity  (host/shared: who can log in)
  ├─ AppUser         users + hashed password + role + stamp    → used at login (Phase 4)
  │                  + password-expiry date (90-day policy)
  └─ RefreshToken    long-lived session tokens (rotate/revoke) → used at login (Phase 4)

sales     (the Sales module: customers, catalogue, orders)
  ├─ Customer        blocking flag + credit limit + balance    → create/submit checks (Phase 6–8)
  ├─ ItemCategory    role restriction + per-order qty cap      → create checks (Phase 6)
  ├─ Item            SKU, price, category                      → create (Phase 6)
  ├─ SalesOrder      the order header + status + total         → create/submit/approve/cancel (Phase 6–9)
  └─ SalesOrderLine  one row per item on an order              → create/update (Phase 6, 9)

inventory (the Inventory module: stock levels)
  └─ Inventory       OnHand / Reserved per item                → reserve on submit (Phase 7)
```

> 🔑 **You have two equally valid ways to build this.** (1) Create the whole schema now as one foundation —
> the app's ordered scripts apply it in one go, and you never think about it again; or (2) create each
> table's script only when you reach the phase that needs it (the scripts are **append-only and applied in
> order**, so adding `001_schema.sql` now and more later works the same either way). This tutorial writes the
> full foundation here because it's one file to reason about — but nothing breaks if you'd rather add tables
> phase-by-phase. Either way, **don't try to hold all of it in your head now**; the map above is enough.

> 📦 **Create two script folders now.** The DDL below spans two modules, so it lands in two places, and
> you'll create both in this phase:
>
> ```
> src/Modules/Sales/Infrastructure/Sql/       ← [identity] + sales tables, procs, seed
> src/Modules/Inventory/Infrastructure/Sql/   ← inventory table, procs, seed
> ```
>
> The `Sales` project already exists from Phase 1. The **`Inventory` project doesn't yet** —
> [Phase 7](07-submit-reserve-concurrency.md) creates it, when Sales first needs to reserve stock. That's
> fine: make the plain folder `src/Modules/Inventory/Infrastructure/Sql/` now and put the inventory scripts
> in it. `.sql` files don't need a `.csproj` to be run by `sqlcmd`, and in Phase 7 the new project simply
> grows around the scripts that are already sitting there. **We write the schema once, here**, so this phase
> ends with a complete, seeded database you can actually query.

Now, the DDL. First create the schemas — `CREATE SCHEMA sales;` and `CREATE SCHEMA [identity];` at the top of
**Sales'** `001_schema.sql`, and `CREATE SCHEMA inventory;` at the top of **Inventory's**. Then build the
tables — going one at a time, explaining the columns that encode business meaning.

> ⚠️ **`identity` is a T-SQL reserved keyword — bracket it as `[identity]` everywhere.** Because `IDENTITY`
> is the keyword for auto-increment columns, SQL Server rejects a bare `identity` used as a schema name:
> `CREATE SCHEMA identity;` fails with *`Msg 156: Incorrect syntax near the keyword 'identity'`*. Wrap it in
> square brackets **in every place it appears** — the `CREATE SCHEMA`, the `CREATE TABLE [identity].AppUser`,
> every `REFERENCES [identity].AppUser(Id)`, the seed `INSERT`, **and every C# stored-proc call string**
> (`"[identity].usp_AppUser_GetByUsername"`, [Phase 4](04-jwt-auth.md)). A bare `identity.` in a proc name
> string fails the same way at call time. (Prefer not to bracket things? Rename the schema to something
> non-reserved like `auth` — but then rename it *consistently* across Phases 3–4. This tutorial keeps
> `[identity]` because the name is descriptive and bracketing a reserved word is a real skill worth having.)

### Users (for JWT auth — Phase 4)

```sql
CREATE TABLE [identity].AppUser (   -- [identity] bracketed: it's a reserved keyword (see ⚠️ above)
    Id                 BIGINT IDENTITY PRIMARY KEY,
    Username           NVARCHAR(100) NOT NULL UNIQUE,
    PasswordHash       NVARCHAR(500) NOT NULL,        -- NEVER store plain passwords
    Role               NVARCHAR(50)  NOT NULL,        -- 'SalesRep' | 'SalesManager' | 'Admin'
    IsActive           BIT NOT NULL DEFAULT 1,
    SecurityStamp      UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),  -- changes on every password change
    PasswordExpiresUtc DATETIME2 NOT NULL,            -- DB-driven policy: 90 days from the last password change
    CONSTRAINT CK_AppUser_Role CHECK (Role IN ('SalesRep','SalesManager','Admin'))
);
```

The `CHECK` on `Role` is integrity: the database itself refuses an unknown role. That's a backstop even
if buggy code tries to insert `'Wizard'`.

Two columns carry the **password-policy** design that [Phase 4](04-jwt-auth.md) leans on — read them
together, because refresh tokens depend on both:
- **`SecurityStamp`** is a random GUID that gets a **new value every time the password changes**. A refresh
  token records the stamp it was minted under; when the two no longer match, the token is dead. That's how
  one password change **instantly invalidates every refresh token** the user holds, on every device — no
  bulk `UPDATE`, no revocation sweep, just a value that no longer agrees.
- **`PasswordExpiresUtc`** is the DB-driven expiry date. Passwords last **90 days**; changing one pushes this
  90 days into the future. Phase 4 uses this exact timestamp as a refresh token's **hard cap**, so a refresh
  token can never outlive the password it was born under.

### Refresh tokens (for the rotating login sessions — Phase 4)

Access tokens are short-lived; a **refresh token** is the long-lived credential a client trades for a fresh
access token ([Phase 4](04-jwt-auth.md)). Each one is stored here so the server can rotate and revoke it:

```sql
CREATE TABLE [identity].RefreshToken (
    Id                 BIGINT IDENTITY PRIMARY KEY,
    TokenHash          NVARCHAR(200) NOT NULL UNIQUE,   -- SHA-256 of the raw token; we NEVER store the raw value
    UserId             BIGINT NOT NULL REFERENCES [identity].AppUser(Id),
    FamilyId           UNIQUEIDENTIFIER NOT NULL,       -- one login/device lineage; rotation keeps this constant
    SecurityStamp      UNIQUEIDENTIFIER NOT NULL,       -- the user's stamp AT MINT; a mismatch later = password changed
    ExpiresUtc         DATETIME2 NOT NULL,              -- sliding: reset every rotation (inactivity timeout)
    AbsoluteExpiresUtc DATETIME2 NOT NULL,              -- hard cap = the user's PasswordExpiresUtc at mint
    CreatedUtc         DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    RevokedUtc         DATETIME2 NULL,                  -- set when rotated, logged out, or revoked on breach
    ReplacedByHash     NVARCHAR(200) NULL,              -- the token that superseded this one (rotation audit chain)
    CONSTRAINT CK_RefreshToken_Expiry CHECK (AbsoluteExpiresUtc >= ExpiresUtc)
);
CREATE INDEX IX_RefreshToken_UserId   ON [identity].RefreshToken(UserId);
CREATE INDEX IX_RefreshToken_FamilyId ON [identity].RefreshToken(FamilyId);
```

Four columns carry the security design, and Phase 4 explains each in action:
- **`TokenHash`, not the token.** Like a password, the raw refresh token is never stored — only its hash. A
  database leak then hands out no usable tokens. (A plain SHA-256 is enough here; unlike a password, the token
  is already long and random, so it needs no salt or slow hash.)
- **`FamilyId`** groups every token descended from one login. Rotation issues a new token in the *same* family;
  detecting a replayed token revokes the *whole* family in one statement.
- **`SecurityStamp`** snapshots the user's `AppUser.SecurityStamp` at the moment the token is minted. On every
  refresh, Phase 4 reloads the user and compares the two: if they differ, the password changed since this token
  was issued, so the token is refused. This is the mechanism that lets a single password change kill every
  outstanding refresh token at once — across every device — without touching the `RefreshToken` table.
- **`ExpiresUtc` vs `AbsoluteExpiresUtc`** encode two timeouts at once: a *sliding* inactivity window that
  resets on each use, and an *absolute* cap that doesn't. Here the absolute cap is **not** a fixed
  configured span — it's copied from the user's `PasswordExpiresUtc`, so a refresh token can never outlive
  the password it belongs to.

> ❓ **"Won't this table grow forever?"** Every login and every rotation inserts a row, and revoking sets
> `RevokedUtc` rather than deleting — so yes, left alone it grows. We keep spent rows *on purpose* (Phase 4's
> reuse detection needs a revoked row to still be there when a stolen token is replayed), but only until they
> can no longer be replayed. A retention job then archives the past-window rows out of this hot table.
> [Phase 4 §10](04c-jwt-refresh-apikey.md) builds it — and explains why deleting *on revoke* would quietly
> break the theft trap.

### Customers (blocking + credit limit)

```sql
CREATE TABLE sales.Customer (
    Id             BIGINT IDENTITY PRIMARY KEY,
    Name           NVARCHAR(200) NOT NULL,
    IsBlocked      BIT NOT NULL DEFAULT 0,           -- customer blocking rule
    CreditLimit    DECIMAL(18,2) NOT NULL DEFAULT 0, -- max they can owe
    OutstandingBalance DECIMAL(18,2) NOT NULL DEFAULT 0, -- what they currently owe
    CONSTRAINT CK_Customer_CreditLimit CHECK (CreditLimit >= 0),
    CONSTRAINT CK_Customer_Balance     CHECK (OutstandingBalance >= 0)
);
```

`IsBlocked`, `CreditLimit`, and `OutstandingBalance` are the raw data our **credit-limit** and
**blocking** rules read. The *decision* ("does this order exceed the limit?") happens in the domain; the
database just stores the numbers honestly (the `CHECK`s stop negatives).

### Item categories (role restrictions + quantity limits)

```sql
CREATE TABLE sales.ItemCategory (
    Id                 BIGINT IDENTITY PRIMARY KEY,
    Name               NVARCHAR(100) NOT NULL UNIQUE,
    MaxQuantityPerOrder INT NOT NULL DEFAULT 1000,   -- category quantity cap per order
    RequiredRole       NVARCHAR(50) NULL,            -- NULL = anyone; else 'SalesManager' etc.
    CONSTRAINT CK_ItemCategory_MaxQty CHECK (MaxQuantityPerOrder > 0)
);
```

`MaxQuantityPerOrder` and `RequiredRole` encode the **category restriction** rules: a "Controlled"
category might require the `SalesManager` role and cap quantity at 5 per order.

### Items and inventory

```sql
CREATE TABLE sales.Item (
    Id         BIGINT IDENTITY PRIMARY KEY,
    Sku        NVARCHAR(50) NOT NULL UNIQUE,
    Name       NVARCHAR(200) NOT NULL,
    CategoryId BIGINT NOT NULL REFERENCES sales.ItemCategory(Id),
    UnitPrice  DECIMAL(18,2) NOT NULL,
    IsActive   BIT NOT NULL DEFAULT 1,
    CONSTRAINT CK_Item_UnitPrice CHECK (UnitPrice >= 0)
);

CREATE TABLE inventory.Inventory (
    ItemId   BIGINT PRIMARY KEY,        -- a REFERENCE to sales.Item, but NOT an FK (see note)
    OnHand   INT NOT NULL DEFAULT 0,    -- physically in the warehouse
    Reserved INT NOT NULL DEFAULT 0,    -- promised to orders, not yet shipped
    CONSTRAINT CK_Inventory_OnHand   CHECK (OnHand >= 0),
    CONSTRAINT CK_Inventory_Reserved CHECK (Reserved >= 0),
    CONSTRAINT CK_Inventory_NotOver  CHECK (Reserved <= OnHand)  -- can't reserve more than you have
);
```

> 🧱 **Notice `ItemId` has no `REFERENCES sales.Item(Id)`.** Items live in the `sales` schema; this table
> lives in `inventory` (owned by the Inventory module from Phase 7). A **foreign key across module schemas**
> would weld the two modules together at the database level and make the "extract Inventory later" option
> expensive. So a cross-module link is a plain **id you resolve through the other module**, not an FK you
> join on. Within a single module's schema, use FKs freely (as `sales.SalesOrderLine → sales.SalesOrder`
> does).

> 🔑 **Look at `CK_Inventory_NotOver`.** This constraint (`Reserved <= OnHand`) is a *final safety net*
> for the concurrency lesson. Even if some future bug tried to over-reserve, the database itself would
> reject the row. Available stock = `OnHand - Reserved`. We'll compute reservation atomically in
> [Phase 7](07-submit-reserve-concurrency.md), but this constraint means "oversold" is impossible at the
> data level. **Belt and suspenders — this is what production code looks like.**

> ⚠️ **What this tutorial does *not* do with `OnHand` (know the boundary).** In the flow we build, submitting
> **reserves** stock (`Reserved` goes up) and cancelling **releases** it (`Reserved` goes down) — but
> **`OnHand` never decreases.** There is no "ship it" step in the base build, so reserved stock is a *hold*,
> not a shipment. That's a deliberate scope cut to keep the lifecycle focused on the two rules that teach
> concurrency (reserve/release). Closing the loop — an `Approved → Fulfilled` transition that atomically
> *deducts* `OnHand` and clears the matching `Reserved` — is the fully-worked
> [Phase 13 extension exercise #3](13-wrap-up-checklist.md). Mentioning it now so "why does OnHand just sit
> there?" has an answer.

### Sales orders and lines

```sql
CREATE TABLE sales.SalesOrder (
    Id             BIGINT IDENTITY PRIMARY KEY,
    OrderNumber    NVARCHAR(30) NOT NULL UNIQUE,      -- human-friendly, e.g. SO-2026-000123
    CustomerId     BIGINT NOT NULL REFERENCES sales.Customer(Id),
    Status         INT NOT NULL,                       -- enum: 0 Draft,1 PendingApproval,2 Approved,3 Cancelled,4 Rejected
    TotalAmount    DECIMAL(18,2) NOT NULL DEFAULT 0,   -- persisted snapshot of the total
    CreatedByUserId BIGINT NOT NULL REFERENCES [identity].AppUser(Id),
    CreatedUtc     DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    ApprovedByUserId BIGINT NULL REFERENCES [identity].AppUser(Id),
    ApprovedUtc    DATETIME2 NULL,
    RowVersion     ROWVERSION,                          -- optimistic concurrency stamp
    CONSTRAINT CK_SalesOrder_Status CHECK (Status BETWEEN 0 AND 4),
    CONSTRAINT CK_SalesOrder_Total  CHECK (TotalAmount >= 0)
);

CREATE TABLE sales.SalesOrderLine (
    Id           BIGINT IDENTITY PRIMARY KEY,
    SalesOrderId BIGINT NOT NULL REFERENCES sales.SalesOrder(Id) ON DELETE CASCADE,
    ItemId       BIGINT NOT NULL REFERENCES sales.Item(Id),
    Quantity     INT NOT NULL,
    UnitPrice    DECIMAL(18,2) NOT NULL,               -- price captured at order time
    CONSTRAINT CK_SalesOrderLine_Qty   CHECK (Quantity > 0),
    CONSTRAINT CK_SalesOrderLine_Price CHECK (UnitPrice >= 0)
);
CREATE INDEX IX_SalesOrderLine_OrderId ON sales.SalesOrderLine(SalesOrderId);
CREATE INDEX IX_SalesOrder_CustomerId  ON sales.SalesOrder(CustomerId);
CREATE INDEX IX_SalesOrder_Status      ON sales.SalesOrder(Status);
```

> ❓ **"Hold on — `CreatedByUserId` has an FK into `[identity]`. Didn't you just forbid cross-schema FKs?"**
> Good catch, and the difference is worth being precise about. The rule is *no FK between two **modules***,
> because an FK is a hard weld and modules are supposed to be separable. `[identity]` is not a module — it's
> a **host-owned** table that every module's data can lean on, the same way every module leans on
> `Erp.Shared`. It will never be extracted into its own service independently of the host, so the weld costs
> nothing. `inventory` *is* a module, and `Inventory.ItemId` therefore stays a bare id. When you're unsure,
> ask: *"could these two ever be pulled apart into separate deployables?"* If yes, no FK.

Details worth catching here:
- **`RowVersion ROWVERSION`** — an auto-changing stamp SQL Server bumps on every update to the row. It
  powers *optimistic concurrency*: when you save, you say "update this row **only if** its stamp is still
  the one I read," so you can never silently overwrite a change someone else made in between. We build that
  guard in [Phase 8](08-approval-cancellation.md).
- **`UnitPrice` is copied onto the line.** We snapshot the price *at order time* so that later price
  changes don't silently alter historical orders. Real ERPs always do this.
- **`Status` stored as `INT`** — the C# enum maps to these numbers (0–4). The `CHECK` keeps it in range.
- **Indexes** on the columns we filter/join by (customer, status, order id) so search stays fast.

## 4. The stored procedures (each module's `Infrastructure/Sql/002_procedures.sql`)

Each repository method calls one proc. We'll add procs as phases need them; here's the full set with
what each is *for*. (Full bodies for the tricky ones — insert and the atomic reserve — shown now; the
rest follow the same shape.)

> 🧱 **Every proc is schema-qualified — and every *call* is too.** These procs live in their module's
> schema (`sales.*`, `inventory.*`, `identity.*`), and your C# always calls them **with the prefix**
> (`"sales.usp_SalesOrder_Insert"`, not `"usp_SalesOrder_Insert"`). A bare name resolves against the
> connection's *default* schema (usually `dbo`) and will fail to find a proc you put in `sales`. This table
> is the **single source of truth** for proc names; if a later phase calls one, it's listed here.

| Proc | Used by | Does |
|---|---|---|
| `[identity].usp_AppUser_GetByUsername` | Login (Phase 4) | Fetch a user + hash, stamp & password-expiry for the login check |
| `[identity].usp_AppUser_GetById` | Refresh (Phase 4) | Reload a user (role/active/stamp/password-expiry) when minting a new access token |
| `[identity].usp_AppUser_ChangePassword` | Change password (Phase 4) | Set a new hash, **rotate `SecurityStamp`**, push `PasswordExpiresUtc` 90 days out — atomically |
| `[identity].usp_RefreshToken_Insert` | Login/Refresh (Phase 4) | Store a new refresh token (hash, family, stamp, expiries) |
| `[identity].usp_RefreshToken_GetByHash` | Refresh/Logout (Phase 4) | Look a refresh token up by its hash |
| `[identity].usp_RefreshToken_Revoke` | Refresh (Phase 4) | Revoke one token, recording its replacement |
| `[identity].usp_RefreshToken_RevokeFamily` | Refresh/Logout (Phase 4) | Revoke a whole family (rotation breach or logout) |
| `[identity].usp_RefreshToken_RevokeAllForUser` | Logout-all (Phase 4) | Revoke every token for one user |
| `sales.usp_Customer_GetById` | Create/Submit | Load blocking + credit numbers |
| `sales.usp_Customer_AdjustBalance` | Approve/Cancel (Phase 8) | Add (or reverse) the order total on the customer's balance |
| `sales.usp_Item_GetByIds` | Create | Load items + category + price for the requested lines |
| `sales.usp_SalesOrder_NextNumber` | Create (Phase 6) | Hand out the next unique order number (sequence-backed) |
| `sales.usp_SalesOrder_Insert` / `sales.usp_SalesOrderLine_Insert` | Create | Persist header + lines |
| `sales.usp_SalesOrder_GetById` / `sales.usp_SalesOrderLine_GetByOrder` | Load aggregate | Rehydrate a `SalesOrder` (write side) |
| `sales.usp_SalesOrder_GetDetail` | Get one (Phase 10) | Read side: header + lines + names, joined for a detail screen |
| `sales.usp_SalesOrderLine_DeleteByOrder` | Update (Phase 9) | Remove a draft's existing lines before re-inserting |
| `sales.usp_SalesOrder_UpdateTotal` | Update (Phase 9) | Refresh the persisted total after re-lining |
| `sales.usp_SalesOrder_UpdateStatus` | Submit/Approve/Cancel | Version-checked status change |
| `inventory.usp_Inventory_Reserve` | Submit (Phase 7) | **Atomic** reserve-if-available |
| `inventory.usp_Inventory_Release` | Cancel (Phase 8) | Give reserved stock back |
| `sales.usp_SalesOrders_Search` | Search (Phase 10) | Filtered, paged read for the grid |

### The insert proc (persistence + returns the new id)

```sql
CREATE OR ALTER PROCEDURE sales.usp_SalesOrder_Insert
    @OrderNumber     NVARCHAR(30),
    @CustomerId      BIGINT,
    @Status          INT,
    @TotalAmount     DECIMAL(18,2),
    @CreatedByUserId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO sales.SalesOrder (OrderNumber, CustomerId, Status, TotalAmount, CreatedByUserId)
    VALUES (@OrderNumber, @CustomerId, @Status, @TotalAmount, @CreatedByUserId);
    SELECT CAST(SCOPE_IDENTITY() AS BIGINT);   -- return the new Id to the repository
END
```

Notice what's **not** here: no credit check, no blocking check, no approval logic. This proc *only*
persists. All policy is enforced in the domain *before* we ever call it.

### The atomic reservation proc (the one genuine SQL-owns-it rule)

```sql
CREATE OR ALTER PROCEDURE inventory.usp_Inventory_Reserve
    @ItemId BIGINT,
    @Qty    INT
AS
BEGIN
    SET NOCOUNT ON;
    -- The check and the write are ONE statement -> safe under concurrency.
    UPDATE inventory.Inventory
    SET Reserved = Reserved + @Qty
    WHERE ItemId = @ItemId
      AND (OnHand - Reserved) >= @Qty;   -- only if enough is available
    SELECT @@ROWCOUNT;                    -- 0 = not enough stock, 1 = reserved
END
```

> 🔑 **Why one statement, and not a check followed by an update?** If C# reads "8 available," decides that's
> enough, and *then* issues the update, another request can slip between the read and the write and reserve
> the same units — both callers saw 8, both thought they were safe, and stock goes negative. Here the `WHERE`
> clause does the checking *inside the same statement* that does the writing, so the database can't let
> anyone interleave. [Phase 7](07-submit-reserve-concurrency.md) walks through that race step by step, and
> calls this proc inside a transaction across all of the order's lines.

### The version-checked status update (optimistic concurrency)

```sql
CREATE OR ALTER PROCEDURE sales.usp_SalesOrder_UpdateStatus
    @Id         BIGINT,
    @Status     INT,
    @ApprovedByUserId BIGINT = NULL,
    @RowVersion BINARY(8)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE sales.SalesOrder
    SET Status = @Status,
        ApprovedByUserId = COALESCE(@ApprovedByUserId, ApprovedByUserId),
        ApprovedUtc = CASE WHEN @Status = 2 THEN SYSUTCDATETIME() ELSE ApprovedUtc END
    WHERE Id = @Id AND RowVersion = @RowVersion;   -- only if nobody changed it since we loaded it
    SELECT @@ROWCOUNT;   -- 0 => stale, someone else changed it first
END
```

### Order numbers: a concurrency-safe sequence (not `MAX()+1`)

Human-friendly order numbers (`SO-2026-000123`) must be **unique even when two salespeople create orders
at the same instant**. A naive `SELECT MAX(...)+1` races — two callers can read the same max and mint the
same number. SQL Server's `SEQUENCE` object hands out unique values atomically, so use one:

```sql
CREATE SEQUENCE sales.SalesOrderNumberSeq AS BIGINT START WITH 1 INCREMENT BY 1;

CREATE OR ALTER PROCEDURE sales.usp_SalesOrder_NextNumber
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @n BIGINT = NEXT VALUE FOR sales.SalesOrderNumberSeq;   -- atomic; no two callers get the same @n
    SELECT CONCAT('SO-', FORMAT(SYSUTCDATETIME(), 'yyyy'), '-', FORMAT(@n, '000000'));
END
```

`ISalesOrderRepository.NextOrderNumberAsync` ([Phase 6](06-create-order.md)) just calls this proc. Same
"the database owns concurrency-safe operations" idea as the atomic reservation — a sequence is the right
tool for "give me a unique number under concurrency."

### The balance adjustment proc (signed delta — add on approve, reverse on cancel)

```sql
CREATE OR ALTER PROCEDURE sales.usp_Customer_AdjustBalance
    @CustomerId BIGINT,
    @Delta      DECIMAL(18,2)   -- + on approval (they now owe), − when an approved order is cancelled
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE sales.Customer
    SET OutstandingBalance = OutstandingBalance + @Delta
    WHERE Id = @CustomerId;
    -- The CK_Customer_Balance CHECK (>= 0) is the backstop: reversing can't drive the balance negative.
END
```

> 🔑 **Why a signed `DECIMAL`, not `Money`?** The domain's `Money` value object forbids negatives — good
> for a *price*, wrong for a *delta* that must go both ways. Approval passes `+total`; cancelling an
> approved order passes `−total` ([Phase 8](08-approval-cancellation.md)). Matching every "charge" with a
> "reverse" is what keeps `OutstandingBalance` honest.

### The reservation-release proc (the matched pair for reserve)

```sql
CREATE OR ALTER PROCEDURE inventory.usp_Inventory_Release
    @ItemId BIGINT,
    @Qty    INT
AS
BEGIN
    SET NOCOUNT ON;
    -- Never let Reserved go below 0, even if a buggy caller over-releases (the CHECK also backstops this).
    UPDATE inventory.Inventory
    SET Reserved = CASE WHEN Reserved - @Qty < 0 THEN 0 ELSE Reserved - @Qty END
    WHERE ItemId = @ItemId;
END
```

### The refresh-token procs (rotate & revoke)

Persistence only — the *policy* (is it expired? is this a replay?) is decided in the Phase 4 handler; these
procs just read and write rows. Each revoke is guarded `AND RevokedUtc IS NULL` so revoking twice is a safe
no-op:

```sql
CREATE OR ALTER PROCEDURE [identity].usp_RefreshToken_Insert
    @TokenHash NVARCHAR(200), @UserId BIGINT, @FamilyId UNIQUEIDENTIFIER,
    @SecurityStamp UNIQUEIDENTIFIER, @ExpiresUtc DATETIME2, @AbsoluteExpiresUtc DATETIME2
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [identity].RefreshToken (TokenHash, UserId, FamilyId, SecurityStamp, ExpiresUtc, AbsoluteExpiresUtc)
    VALUES (@TokenHash, @UserId, @FamilyId, @SecurityStamp, @ExpiresUtc, @AbsoluteExpiresUtc);
END

CREATE OR ALTER PROCEDURE [identity].usp_RefreshToken_GetByHash
    @TokenHash NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, TokenHash, UserId, FamilyId, SecurityStamp, ExpiresUtc, AbsoluteExpiresUtc, RevokedUtc
    FROM [identity].RefreshToken WHERE TokenHash = @TokenHash;
END

CREATE OR ALTER PROCEDURE [identity].usp_RefreshToken_Revoke
    @TokenHash NVARCHAR(200), @ReplacedByHash NVARCHAR(200) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [identity].RefreshToken
    SET RevokedUtc = SYSUTCDATETIME(), ReplacedByHash = @ReplacedByHash
    WHERE TokenHash = @TokenHash AND RevokedUtc IS NULL;
END

CREATE OR ALTER PROCEDURE [identity].usp_RefreshToken_RevokeFamily
    @FamilyId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [identity].RefreshToken SET RevokedUtc = SYSUTCDATETIME()
    WHERE FamilyId = @FamilyId AND RevokedUtc IS NULL;   -- one statement kills the whole lineage
END

CREATE OR ALTER PROCEDURE [identity].usp_RefreshToken_RevokeAllForUser
    @UserId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [identity].RefreshToken SET RevokedUtc = SYSUTCDATETIME()
    WHERE UserId = @UserId AND RevokedUtc IS NULL;
END
```

(`[identity].usp_AppUser_GetById` is the obvious one-row `SELECT … WHERE Id = @Id` — the same shape as
`usp_AppUser_GetByUsername`. Both now also return the new `SecurityStamp` and `PasswordExpiresUtc` columns,
because Phase 4's login and refresh checks need them.)

### Changing a password (where invalidation and the expiry cap come from)

Both new behaviours — instant token invalidation and the moving expiry cap — originate in **one** place: the
proc that changes a password. It does three things in a single atomic write. This is the **database-driven
password policy** the rest of Phase 4 relies on:

```sql
CREATE OR ALTER PROCEDURE [identity].usp_AppUser_ChangePassword
    @UserId BIGINT, @NewPasswordHash NVARCHAR(500)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [identity].AppUser
    SET PasswordHash       = @NewPasswordHash,
        SecurityStamp      = NEWID(),                              -- (1) rotate → every old refresh token is now stale
        PasswordExpiresUtc  = DATEADD(day, 90, SYSUTCDATETIME())   -- (2) DB policy: 90 days from this change
    WHERE Id = @UserId;
END
```

> 🔑 **One write, two guarantees.** Rotating `SecurityStamp` means every refresh token minted under the old
> stamp fails its next refresh — the user is logged out everywhere the instant the password changes. Setting
> `PasswordExpiresUtc` to 90 days out means the *next* login's refresh tokens get a fresh 90-day hard cap.
> Neither guarantee is a configured number in the app — both live in the database, exactly where the password
> policy lives.

> ℹ️ **You'll wire an endpoint onto this proc.** [Phase 4 Part 4](04d-jwt-change-password.md) builds
> `POST /auth/change-password` on top of it — including the surprising reason that endpoint has to be
> *anonymous*. What this tutorial does **not** build is a *forgot*-password flow (emailed reset links are
> their own topic, with their own token lifecycle); that one's an
> [extension exercise](13-wrap-up-checklist.md).

## 5. Seed data (`Infrastructure/Sql/003_seed.sql`)

So you have something to log in as and order against:

```sql
-- Password hashes are placeholders; Phase 4 shows how to generate real ones.
-- PasswordExpiresUtc is seeded 90 days out (the DB policy); SecurityStamp defaults to a fresh NEWID().
INSERT INTO [identity].AppUser (Username, PasswordHash, Role, PasswordExpiresUtc) VALUES
 ('rep',     '<hash-of-Password123!>', 'SalesRep',     DATEADD(day, 90, SYSUTCDATETIME())),
 ('manager', '<hash-of-Password123!>', 'SalesManager', DATEADD(day, 90, SYSUTCDATETIME())),
 ('admin',   '<hash-of-Password123!>', 'Admin',        DATEADD(day, 90, SYSUTCDATETIME()));

INSERT INTO sales.Customer (Name, IsBlocked, CreditLimit, OutstandingBalance) VALUES
 ('Acme Corp',        0, 50000, 10000),   -- healthy: ₱40,000 of headroom
 ('Beta Industries',  0,  5000,  4500),   -- almost maxed: only ₱500 left
 ('Gamma Traders',    1, 20000,     0);   -- BLOCKED

INSERT INTO sales.ItemCategory (Name, MaxQuantityPerOrder, RequiredRole) VALUES
 ('General',    1000, NULL),           -- anyone, high cap
 ('Controlled',    5, 'SalesManager'); -- managers only, cap 5 per order

INSERT INTO sales.Item (Sku, Name, CategoryId, UnitPrice) VALUES
 ('GEN-001', 'Widget',        1, 25.00),
 ('GEN-002', 'Gadget',        1, 40.00),
 ('CTL-001', 'Restricted Kit',2, 500.00);

-- This last block belongs to the INVENTORY module's own 003_seed.sql, because inventory.Inventory is the
-- Inventory module's table. It runs after the sales seed above, since it refers to the item ids that seed
-- just created.
INSERT INTO inventory.Inventory (ItemId, OnHand, Reserved) VALUES
 (1, 100, 0),
 (2,  50, 0),
 (3,   8, 0);   -- only 8 of the restricted kit exist
```

This seed is *designed for testing the rules*: Beta is near its credit limit, Gamma is blocked, the
Controlled category is manager-only and capped at 5, and the restricted kit has thin stock. Every
business rule has a scenario waiting for it.

## 6. How this fits the request flow

No request flows through *this* phase directly — it's the foundation. But every later request ends here:

```
Command handler → repository → Dapper → stored procedure → these tables
Query handler   → Dapper → read stored procedure → these tables → DTO
```

The domain decides *whether* to write; these tables and procs are *how* it's stored, and the `CHECK`
constraints are the last line of defense if anything upstream is wrong.

## 7. Applying the scripts (no EF migrations)

### First, tell the app where its database is (the connection string)

This is the phase that first needs a database, so it's where we add the **connection string** (remember,
Phase 1 deliberately configured nothing we weren't using yet). One database, `Erp`, holds every module's
schema. In `appsettings.Development.json` (local dev only):

```json
{
  "ConnectionStrings": {
    "ErpDb": "Server=(localdb)\\MSSQLLocalDB;Database=Erp;Trusted_Connection=True;TrustServerCertificate=True"
  }
}
```

Leave an empty `"ConnectionStrings": { "ErpDb": "" }` in `appsettings.json` so the *shape* is documented but
no environment-specific value is committed. (The C# packages for querying — Dapper and
`Microsoft.Data.SqlClient` — arrive in the very next phase, [Phase 3b](03b-data-access.md), where we build
the connection factory and the transaction pattern; right now we just need somewhere to point the scripts.)

### Run the scripts

Create the `Erp` database if it doesn't exist yet, then run each module's scripts in dependency order. **The
`sales` scripts go first**, because Inventory's seed refers to the item ids that Sales' seed creates:

```bash
# Create the database once (the scripts assume it exists):
sqlcmd -S "(localdb)\MSSQLLocalDB" -Q "IF DB_ID('Erp') IS NULL CREATE DATABASE Erp;"

# Sales first — it owns [identity] and sales:
sqlcmd -S "(localdb)\MSSQLLocalDB" -d Erp -i src/Modules/Sales/Infrastructure/Sql/001_schema.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -d Erp -i src/Modules/Sales/Infrastructure/Sql/002_procedures.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -d Erp -i src/Modules/Sales/Infrastructure/Sql/003_seed.sql

# Then Inventory — its seed needs the item ids the sales seed just inserted:
sqlcmd -S "(localdb)\MSSQLLocalDB" -d Erp -i src/Modules/Inventory/Infrastructure/Sql/001_schema.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -d Erp -i src/Modules/Inventory/Infrastructure/Sql/002_procedures.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -d Erp -i src/Modules/Inventory/Infrastructure/Sql/003_seed.sql
```

> ❓ **"No `sqlcmd`?"** It ships with SQL Server and with the free
> [SQL Server Command Line Utilities](https://aka.ms/sqlcmd). If you'd rather not install it, open the six
> files in SQL Server Management Studio or Azure Data Studio and execute them against `Erp` **in the order
> above** — the tool doesn't matter, the order does.

> ⚠️ **Where do the `[identity]` tables live?** There is no `Identity` *module* project — authentication is a
> host concern, not a business capability, so we never gave it one. Its DDL therefore has to ride along
> somewhere, and the pragmatic answer is the **Sales module's `001_schema.sql`**: `CREATE SCHEMA [identity]`
> and both its tables sit at the top of that file, above the `sales` tables. That's a genuine wrinkle, not a
> pattern — the tidy version is a third script folder owned by the host, and it's a fine thing to refactor
> toward once you've seen why the wrinkle exists — in fact the [03a-database-queries/](03a-database-queries/README.md)
> companion folder already lays them out that way, with `Identity/`, `Sales/`, and `Inventory/` side by side.
> What matters is the rule that *does* hold: no `sales` proc ever reads an `[identity]` table, and no
> `[identity]` proc ever reads a `sales` one.

The *same* scripts set up the test database in [Phase 12](12-testing.md). One source of truth per module
for schema, procs, seed — used by dev, test, and prod. That's the "migrations without EF" approach: numbered
scripts, applied in order, each one run exactly once. It's genuinely production-grade.

### A concrete runner (DbUp) so scripts apply once, in order, and are tracked

Running `sqlcmd` by hand is fine to see it work, but for a repeatable deploy you want something that
**remembers which scripts already ran** so re-running is safe (idempotent). The free **DbUp** library does
exactly this — it records every applied script in a `SchemaVersions` table it manages, and skips ones
already run:

```csharp
// A tiny console tool (or a startup step) — points at each module's Sql/ folder, applies new scripts only.
var upgrader = DeployChanges.To
    .SqlDatabase(connectionString)
    .WithScriptsFromFileSystem("src/Modules/Sales/Infrastructure/Sql")       // 001_schema, 002_procedures, 003_seed…
    .WithScriptsFromFileSystem("src/Modules/Inventory/Infrastructure/Sql")   // inventory scripts (Phase 7)
    .JournalToSqlTable("dbo", "SchemaVersions")   // DbUp creates/uses this to track what ran
    .LogToConsole()
    .Build();

var result = upgrader.PerformUpgrade();
if (!result.Successful) { /* log result.Error and stop the deploy */ }
```

Because scripts run **in filename order** and each runs **once**, name them with ordered prefixes
(`001_`, `002_`…) and treat applied scripts as immutable — add a *new* script to change the schema, don't
edit an old one. That "append-only, tracked" habit is what makes the same folder safe for dev, test, and
prod. (`CREATE OR ALTER PROCEDURE`, below, is the one friendly exception — proc scripts you *can* re-run.)

### If you're *not* using DbUp: fully guarded scripts + an audit log

DbUp is optional. If you'd rather redeploy with plain `sqlcmd`, a batch file, or a CI step that just
re-runs the folder, you don't have a runner tracking what already ran — so each *statement* has to be safe
to run again on its own. The companion folder **[03a-database-queries/](03a-database-queries/README.md)**
holds exactly that: every block from this phase, extracted into runnable `.sql` files where **every
statement is guarded** —

- schemas → `IF SCHEMA_ID('sales') IS NULL EXEC('CREATE SCHEMA sales;')`
- tables / sequences → `IF OBJECT_ID('sales.Customer','U') IS NULL CREATE TABLE …`
- indexes → `IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE …) CREATE INDEX …`
- procs → `CREATE OR ALTER` (already re-runnable)
- seed → `IF NOT EXISTS (SELECT 1 FROM <table>) BEGIN INSERT … END`

> 🔑 **Two places to put "safe to re-run."** DbUp puts it in the **runner** (it records applied scripts and
> skips them); the `03a` scripts put it in each **statement** (the `IF … IS NULL` guards). Same outcome —
> pick the one that matches your deploy. Don't stack both on the same script: with DbUp, the guards are
> just noise; without it, they're the whole safety net.

> ℹ️ **Where our build ends up.** For this phase, running the scripts by hand is exactly right — one
> database, run once, move on. But know now that the by-hand step is scaffolding, not the final shape: in
> [Phase 12](12-testing.md), a *second* database (the test one) makes by-hand tedious, and we'll make the
> **app apply these same scripts itself at startup** — a small `DatabaseInitializer` in the host that embeds
> each module's `Sql/` folder and re-runs it on every boot. Re-running on every boot only works with the
> statement-guarded `03a` style, which is the practical reason to adopt those guarded files as your real
> `Sql/` scripts rather than treating them as a side reference. Nothing to do about it yet.

**The `03a` scripts also add one thing this base schema doesn't: an audit trigger on the low-churn tables.**
Each schema gets a `ChangeLog` table, and each such table gets one `AFTER INSERT, UPDATE, DELETE` trigger that
writes a row saying *which table, which key, insert/update/delete, when, and which DB login*. The one table
that *doesn't* get a trigger is `[identity].RefreshToken` — it churns too fast, and its rows have to be kept
(not deleted) while they're still replay-relevant, so its history is handled by a retention job instead of a
trigger. That's [Phase 4 §10](04c-jwt-refresh-apikey.md)'s story, and it's what keeps that table small.

> ⚠️ **A trigger — in a course that's told you all logic belongs in the domain? Read the boundary
> carefully, because it's exactly where "no hidden DB logic" gets precise.** The rule this course actually
> protects is **no hidden *business* logic in the database**: no approval `IF`, no credit check, no status
> rule where the app can't see it. These triggers break none of that. They hold **zero** business logic —
> no policy, no validation, no writes to business tables. All they do is append a log row: the database
> recording its *own* changes, the way a file server logs access. That doesn't change what the application
> *does*, so it isn't business behavior — it's an **audit trail for the DBA**, a safety net for "who
> touched this row, and when?" That's why it's a *good* example to see: you can have full auditing
> **without** smuggling a single decision into SQL.
>
> ❓ **"Isn't 'react after a change' supposed to be a domain event?"** For *business* facts, yes — "order
> approved by user 42, because…" is a domain event ([Phase 8](08-approval-cancellation.md)); it carries the
> app user and the intent. A trigger captures something different and complementary: the **raw row change
> as the database saw it**, including writes that never went through your app (a DBA hot-fix, a bulk
> `UPDATE`). Events record *intent*; triggers record *what physically changed*. Note `ChangedBy` is the
> **database login**, not the app user — a trigger can't read your JWT. App-user attribution stays the
> event's job. Keep the two straight and neither one leaks business rules into the database.

### Checkpoint — the database is real and seeded ✅

After the scripts run, verify the foundation actually exists. Query the seed users:

```sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -d Erp -Q "SELECT Username, Role FROM [identity].AppUser;"
```

You should see three rows — `rep` / `SalesRep`, `manager` / `SalesManager`, `admin` / `Admin`. A couple more
quick checks: `SELECT Name, IsBlocked FROM sales.Customer;` shows Gamma Traders blocked, and
`SELECT ItemId, OnHand FROM inventory.Inventory;` shows the restricted kit with only 8 on hand.

If those queries return the seed rows, **Phase 3 is done and verifiable**: the schemas exist, integrity
constraints are in force, the procs are compiled, and there's data to log in as and order against. Next,
[Phase 3b](03b-data-access.md) wires up how C# *connects* to this database; then Phase 4 builds login on top
of exactly these `[identity]` tables.

## 8. Common mistakes & best practices

- ❌ **Putting business policy in a proc** (e.g. an approval `IF` inside `usp_SalesOrder_Insert`). ✅
  Procs persist and enforce *integrity*; *policy* stays in the domain. One rule, one home.
- ❌ **Storing plain-text passwords.** ✅ `PasswordHash` only, always. Phase 4 hashes them.
- ❌ **Forgetting indexes** on filter/join columns → slow search as data grows. ✅ Index `CustomerId`,
  `Status`, and the line's `SalesOrderId`.
- ❌ **Not snapshotting `UnitPrice` on the line** → changing an item's price silently rewrites history.
  ✅ Copy the price onto the line at order time.
- ✅ **Best practice: `CHECK` constraints as backstops.** `Reserved <= OnHand`, `Quantity > 0`,
  `Status BETWEEN 0 AND 4` — these make bad data *impossible*, independent of any app bug. Cheap
  insurance you'll be glad of.
- ✅ **Best practice: `CREATE OR ALTER PROCEDURE`.** Re-running the script updates the proc instead of
  erroring — friendlier for the iterate-and-rerun workflow.

---

## ✅ Check Yourself

> Try each one before you look. [Model answers](ANSWER-KEY.md#phase-3-database) — but attempting first is what makes them stick.

1. Which business rule is the *one* we deliberately enforce in SQL, and why can't it live only in C#?
2. What does the `RowVersion` column enable, and in which later phase?
3. Why do we copy `UnitPrice` onto the order line instead of always reading it from `Item`?
4. Name two `CHECK` constraints and the bad data each one makes impossible.

---

← Previous: [Phase 2 — Your own mediator vs. the free one](02-mediator-own-vs-free.md) · [Tutorial overview](00-START-HERE.md) · Next: [Phase 3b — Data Access: connections & transactions](03b-data-access.md) →
