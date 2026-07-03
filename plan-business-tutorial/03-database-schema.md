# Phase 3 — The Database: Schema, Stored Procedures, Seed Data

> 📎 Reuses the "integrity in SQL, policy in the domain" split from
> [beginner Phase 4 §6](../plan-beginner-friendly/05-phase-4-create-order.md) and the
> "ordered SQL scripts instead of EF migrations" approach from
> [beginner Phase 8 §6](../plan-beginner-friendly/09-phase-8-testing.md).

---

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

Two principles carried over from the earlier plans, applied to a real schema:

- **Integrity lives in SQL.** Foreign keys, `NOT NULL`, `CHECK` constraints, and unique indexes enforce
  data correctness *no matter what code touches the database*. We don't trust the app to never insert
  garbage — the schema refuses it.
- **Policy lives in the domain (C#).** "Only draft orders can be edited," "over $10k needs approval" —
  those are *not* in the database. Putting them there too would create the "split-brain" duplication
  [beginner Phase 4](../plan-beginner-friendly/05-phase-4-create-order.md) warned about.
- **One exception, on purpose:** the *atomic inventory guard* (reserve only if available) **is** in SQL,
  because only the database can make it safe under concurrency. That's [Phase 7](07-submit-reserve-concurrency.md)'s
  whole lesson, and its proc lives here.

> ❓ **You might be wondering: "Why stored procedures instead of just writing SQL strings in C#?"** Three
> reasons a senior cares about: (1) the SQL lives in *one place* the DBA can review and tune; (2) it
> keeps SQL out of your C# so the two don't tangle; (3) it gives a clean, named boundary — your
> repository calls `usp_SalesOrder_Insert`, not a wall of inline text. It's the same "explicit, visible
> data access" value Dapper gives us, taken one step further.

## 3. The tables (each module's `Infrastructure/Sql/001_schema.sql`)

First create the schemas (`CREATE SCHEMA sales;`, `CREATE SCHEMA inventory;`, `CREATE SCHEMA identity;`),
each in the owning project's script. Then build the tables. Let's go table by table, explaining the columns
that encode business meaning.

### Users (for JWT auth — Phase 4)

```sql
CREATE TABLE identity.AppUser (
    Id            BIGINT IDENTITY PRIMARY KEY,
    Username      NVARCHAR(100) NOT NULL UNIQUE,
    PasswordHash  NVARCHAR(500) NOT NULL,        -- NEVER store plain passwords
    Role          NVARCHAR(50)  NOT NULL,        -- 'SalesRep' | 'SalesManager' | 'Admin'
    IsActive      BIT NOT NULL DEFAULT 1,
    CONSTRAINT CK_AppUser_Role CHECK (Role IN ('SalesRep','SalesManager','Admin'))
);
```

The `CHECK` on `Role` is integrity: the database itself refuses an unknown role. That's a backstop even
if buggy code tries to insert `'Wizard'`.

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

### Sales orders and lines

```sql
CREATE TABLE sales.SalesOrder (
    Id             BIGINT IDENTITY PRIMARY KEY,
    OrderNumber    NVARCHAR(30) NOT NULL UNIQUE,      -- human-friendly, e.g. SO-2026-000123
    CustomerId     BIGINT NOT NULL REFERENCES sales.Customer(Id),
    Status         INT NOT NULL,                       -- enum: 0 Draft,1 PendingApproval,2 Approved,3 Cancelled,4 Rejected
    TotalAmount    DECIMAL(18,2) NOT NULL DEFAULT 0,   -- persisted snapshot of the total
    CreatedByUserId BIGINT NOT NULL REFERENCES identity.AppUser(Id),
    CreatedUtc     DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    ApprovedByUserId BIGINT NULL REFERENCES identity.AppUser(Id),
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

Notes a beginner should catch:
- **`RowVersion ROWVERSION`** — an auto-changing stamp SQL Server bumps on every update. It powers
  optimistic concurrency (["don't overwrite someone else's change"](../plan-beginner-friendly/06-phase-5-inventory-concurrency.md))
  in [Phase 8](08-approval-cancellation.md).
- **`UnitPrice` is copied onto the line.** We snapshot the price *at order time* so that later price
  changes don't silently alter historical orders. Real ERPs always do this.
- **`Status` stored as `INT`** — the C# enum maps to these numbers (0–4). The `CHECK` keeps it in range.
- **Indexes** on the columns we filter/join by (customer, status, order id) so search stays fast.

## 4. The stored procedures (each module's `Infrastructure/Sql/002_procedures.sql`)

Each repository method calls one proc. We'll add procs as phases need them; here's the full set with
what each is *for*. (Full bodies for the tricky ones — insert and the atomic reserve — shown now; the
rest follow the same shape.)

| Proc | Used by | Does |
|---|---|---|
| `usp_AppUser_GetByUsername` | Login (Phase 4) | Fetch a user + hash for password check |
| `usp_Customer_GetById` | Create/Submit | Load blocking + credit numbers |
| `usp_Item_GetByIds` | Create | Load items + category + price for the requested lines |
| `usp_SalesOrder_Insert` / `usp_SalesOrderLine_Insert` | Create | Persist header + lines |
| `usp_SalesOrder_GetById` / `usp_SalesOrderLine_GetByOrder` | Load aggregate | Rehydrate a `SalesOrder` |
| `usp_SalesOrder_UpdateStatus` | Submit/Approve/Cancel | Version-checked status change |
| `usp_Inventory_Reserve` | Submit (Phase 7) | **Atomic** reserve-if-available |
| `usp_Inventory_Release` | Cancel (Phase 8) | Give reserved stock back |
| `usp_Customer_AdjustBalance` | Approve (Phase 8) | Add the order total to the customer's balance |
| `usp_SalesOrders_Search` | Search (Phase 10) | Filtered, paged read for the grid |

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

> 📎 *Why* this single-statement form is safe when a C# check-then-act isn't — the moment-by-moment
> timeline is in [beginner Phase 5](../plan-beginner-friendly/06-phase-5-inventory-concurrency.md). This
> proc is that lesson, in production form. [Phase 7](07-submit-reserve-concurrency.md) calls it inside a
> transaction across all the order's lines.

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

## 5. Seed data (`Infrastructure/Sql/003_seed.sql`)

So you have something to log in as and order against:

```sql
-- Password hashes are placeholders; Phase 4 shows how to generate real ones.
INSERT INTO identity.AppUser (Username, PasswordHash, Role) VALUES
 ('rep',     '<hash-of-Password123!>', 'SalesRep'),
 ('manager', '<hash-of-Password123!>', 'SalesManager'),
 ('admin',   '<hash-of-Password123!>', 'Admin');

INSERT INTO sales.Customer (Name, IsBlocked, CreditLimit, OutstandingBalance) VALUES
 ('Acme Corp',        0, 50000, 10000),   -- healthy: 40k of headroom
 ('Beta Industries',  0,  5000,  4500),   -- almost maxed: only 500 left
 ('Gamma Traders',    1, 20000,     0);   -- BLOCKED

INSERT INTO sales.ItemCategory (Name, MaxQuantityPerOrder, RequiredRole) VALUES
 ('General',    1000, NULL),           -- anyone, high cap
 ('Controlled',    5, 'SalesManager'); -- managers only, cap 5 per order

INSERT INTO sales.Item (Sku, Name, CategoryId, UnitPrice) VALUES
 ('GEN-001', 'Widget',        1, 25.00),
 ('GEN-002', 'Gadget',        1, 40.00),
 ('CTL-001', 'Restricted Kit',2, 500.00);

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

Run each module's scripts in dependency order against your dev database (create the schemas first, then
`sales`, then `inventory`, plus `identity`). A tiny runner, or the free **DbUp** library, discovers each
module's `Infrastructure/Sql/` folder:

```bash
sqlcmd -S "(localdb)\MSSQLLocalDB" -d Erp -i src/Modules/Sales/Infrastructure/Sql/001_schema.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -d Erp -i src/Modules/Sales/Infrastructure/Sql/002_procedures.sql
sqlcmd -S "(localdb)\MSSQLLocalDB" -d Erp -i src/Modules/Sales/Infrastructure/Sql/003_seed.sql
# ...and the equivalent Inventory + identity scripts.
```

The *same* scripts set up the test database in [Phase 12](12-testing.md). One source of truth per module
for schema, procs, seed — used by dev, test, and prod. That's the "migrations without EF" approach from
[beginner Phase 8](../plan-beginner-friendly/09-phase-8-testing.md), and it's genuinely production-grade.

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

1. Which business rule is the *one* we deliberately enforce in SQL, and why can't it live only in C#?
2. What does the `RowVersion` column enable, and in which later phase?
3. Why do we copy `UnitPrice` onto the order line instead of always reading it from `Item`?
4. Name two `CHECK` constraints and the bad data each one makes impossible.

> 👉 Next: [Phase 4 — JWT authentication & authorization](04-jwt-auth.md).
