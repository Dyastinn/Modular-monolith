/* =====================================================================
   Sales module — schema + tables + order-number sequence  (idempotent)
   Companion to Phase 3 §3. Run AFTER Identity (SalesOrder references
   [identity].AppUser).
   ===================================================================== */

IF SCHEMA_ID('sales') IS NULL EXEC('CREATE SCHEMA sales;');
GO

IF OBJECT_ID('sales.Customer', 'U') IS NULL
BEGIN
    CREATE TABLE sales.Customer (
        Id                 BIGINT IDENTITY PRIMARY KEY,
        Name               NVARCHAR(200) NOT NULL,
        IsBlocked          BIT NOT NULL DEFAULT 0,               -- customer blocking rule
        CreditLimit        DECIMAL(18,2) NOT NULL DEFAULT 0,     -- max they can owe
        OutstandingBalance DECIMAL(18,2) NOT NULL DEFAULT 0,     -- what they currently owe
        CONSTRAINT CK_Customer_CreditLimit CHECK (CreditLimit >= 0),
        CONSTRAINT CK_Customer_Balance     CHECK (OutstandingBalance >= 0)
    );
END
GO

IF OBJECT_ID('sales.ItemCategory', 'U') IS NULL
BEGIN
    CREATE TABLE sales.ItemCategory (
        Id                  BIGINT IDENTITY PRIMARY KEY,
        Name                NVARCHAR(100) NOT NULL UNIQUE,
        MaxQuantityPerOrder INT NOT NULL DEFAULT 1000,           -- category quantity cap per order
        RequiredRole        NVARCHAR(50) NULL,                   -- NULL = anyone; else 'SalesManager' etc.
        CONSTRAINT CK_ItemCategory_MaxQty CHECK (MaxQuantityPerOrder > 0)
    );
END
GO

IF OBJECT_ID('sales.Item', 'U') IS NULL
BEGIN
    CREATE TABLE sales.Item (
        Id         BIGINT IDENTITY PRIMARY KEY,
        Sku        NVARCHAR(50) NOT NULL UNIQUE,
        Name       NVARCHAR(200) NOT NULL,
        CategoryId BIGINT NOT NULL REFERENCES sales.ItemCategory(Id),
        UnitPrice  DECIMAL(18,2) NOT NULL,
        IsActive   BIT NOT NULL DEFAULT 1,
        CONSTRAINT CK_Item_UnitPrice CHECK (UnitPrice >= 0)
    );
END
GO

IF OBJECT_ID('sales.SalesOrder', 'U') IS NULL
BEGIN
    CREATE TABLE sales.SalesOrder (
        Id               BIGINT IDENTITY PRIMARY KEY,
        OrderNumber      NVARCHAR(30) NOT NULL UNIQUE,          -- e.g. SO-2026-000123
        CustomerId       BIGINT NOT NULL REFERENCES sales.Customer(Id),
        Status           INT NOT NULL,                          -- 0 Draft,1 PendingApproval,2 Approved,3 Cancelled,4 Rejected
        TotalAmount      DECIMAL(18,2) NOT NULL DEFAULT 0,      -- persisted snapshot of the total
        CreatedByUserId  BIGINT NOT NULL REFERENCES [identity].AppUser(Id),
        CreatedUtc       DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        ApprovedByUserId BIGINT NULL REFERENCES [identity].AppUser(Id),
        ApprovedUtc      DATETIME2 NULL,
        RowVersion       ROWVERSION,                            -- optimistic concurrency stamp
        CONSTRAINT CK_SalesOrder_Status CHECK (Status BETWEEN 0 AND 4),
        CONSTRAINT CK_SalesOrder_Total  CHECK (TotalAmount >= 0)
    );
END
GO

IF OBJECT_ID('sales.SalesOrderLine', 'U') IS NULL
BEGIN
    CREATE TABLE sales.SalesOrderLine (
        Id           BIGINT IDENTITY PRIMARY KEY,
        SalesOrderId BIGINT NOT NULL REFERENCES sales.SalesOrder(Id) ON DELETE CASCADE,
        ItemId       BIGINT NOT NULL REFERENCES sales.Item(Id),
        Quantity     INT NOT NULL,
        UnitPrice    DECIMAL(18,2) NOT NULL,                    -- price captured at order time
        CONSTRAINT CK_SalesOrderLine_Qty   CHECK (Quantity > 0),
        CONSTRAINT CK_SalesOrderLine_Price CHECK (UnitPrice >= 0)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_SalesOrderLine_OrderId'
                 AND object_id = OBJECT_ID('sales.SalesOrderLine'))
    CREATE INDEX IX_SalesOrderLine_OrderId ON sales.SalesOrderLine(SalesOrderId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_SalesOrder_CustomerId'
                 AND object_id = OBJECT_ID('sales.SalesOrder'))
    CREATE INDEX IX_SalesOrder_CustomerId ON sales.SalesOrder(CustomerId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_SalesOrder_Status'
                 AND object_id = OBJECT_ID('sales.SalesOrder'))
    CREATE INDEX IX_SalesOrder_Status ON sales.SalesOrder(Status);
GO

-- Concurrency-safe order numbers (a SEQUENCE, not MAX()+1)
IF OBJECT_ID('sales.SalesOrderNumberSeq', 'SO') IS NULL
    CREATE SEQUENCE sales.SalesOrderNumberSeq AS BIGINT START WITH 1 INCREMENT BY 1;
GO
