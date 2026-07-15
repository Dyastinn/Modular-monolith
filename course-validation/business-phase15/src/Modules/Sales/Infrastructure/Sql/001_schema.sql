-- Phase 3 — Sales + [identity] schema. Integrity lives in SQL; policy lives in the domain.
-- [identity] is a T-SQL reserved keyword, so it is bracketed EVERYWHERE (schema, tables, FKs, procs).

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'sales')       EXEC('CREATE SCHEMA sales');
GO
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'identity')    EXEC('CREATE SCHEMA [identity]');
GO

-- Users (for JWT auth — Phase 4)
IF OBJECT_ID('[identity].AppUser') IS NULL
CREATE TABLE [identity].AppUser (
    Id                 BIGINT IDENTITY PRIMARY KEY,
    Username           NVARCHAR(100) NOT NULL UNIQUE,
    PasswordHash       NVARCHAR(500) NOT NULL,        -- NEVER store plain passwords
    Role               NVARCHAR(50)  NOT NULL,        -- 'SalesRep' | 'SalesManager' | 'Admin'
    IsActive           BIT NOT NULL DEFAULT 1,
    SecurityStamp      UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),  -- changes on every password change
    PasswordExpiresUtc DATETIME2 NOT NULL,            -- DB-driven policy: 90 days from the last password change
    CONSTRAINT CK_AppUser_Role CHECK (Role IN ('SalesRep','SalesManager','Admin'))
);
GO

-- Refresh tokens (rotating login sessions — Phase 4 §9). We store the HASH, never the raw token.
IF OBJECT_ID('[identity].RefreshToken') IS NULL
CREATE TABLE [identity].RefreshToken (
    Id                 BIGINT IDENTITY PRIMARY KEY,
    TokenHash          NVARCHAR(200) NOT NULL UNIQUE,   -- SHA-256 of the raw token
    UserId             BIGINT NOT NULL REFERENCES [identity].AppUser(Id),
    FamilyId           UNIQUEIDENTIFIER NOT NULL,       -- one login lineage; rotation keeps this constant
    SecurityStamp      UNIQUEIDENTIFIER NOT NULL,       -- user's stamp at mint; mismatch later = password changed
    ExpiresUtc         DATETIME2 NOT NULL,              -- sliding: reset every rotation
    AbsoluteExpiresUtc DATETIME2 NOT NULL,              -- hard cap = user's PasswordExpiresUtc at mint
    CreatedUtc         DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    RevokedUtc         DATETIME2 NULL,                  -- set when rotated, logged out, or revoked on breach
    ReplacedByHash     NVARCHAR(200) NULL,              -- the token that superseded this one (rotation audit chain)
    CONSTRAINT CK_RefreshToken_Expiry CHECK (AbsoluteExpiresUtc >= ExpiresUtc)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RefreshToken_UserId')
    CREATE INDEX IX_RefreshToken_UserId ON [identity].RefreshToken(UserId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RefreshToken_FamilyId')
    CREATE INDEX IX_RefreshToken_FamilyId ON [identity].RefreshToken(FamilyId);
GO

-- Customers (blocking + credit limit)
IF OBJECT_ID('sales.Customer') IS NULL
CREATE TABLE sales.Customer (
    Id                 BIGINT IDENTITY PRIMARY KEY,
    Name               NVARCHAR(200) NOT NULL,
    IsBlocked          BIT NOT NULL DEFAULT 0,
    CreditLimit        DECIMAL(18,2) NOT NULL DEFAULT 0,
    OutstandingBalance DECIMAL(18,2) NOT NULL DEFAULT 0,
    CONSTRAINT CK_Customer_CreditLimit CHECK (CreditLimit >= 0),
    CONSTRAINT CK_Customer_Balance     CHECK (OutstandingBalance >= 0)
);
GO

-- Item categories (role restrictions + quantity limits)
IF OBJECT_ID('sales.ItemCategory') IS NULL
CREATE TABLE sales.ItemCategory (
    Id                  BIGINT IDENTITY PRIMARY KEY,
    Name                NVARCHAR(100) NOT NULL UNIQUE,
    MaxQuantityPerOrder INT NOT NULL DEFAULT 1000,
    RequiredRole        NVARCHAR(50) NULL,          -- NULL = anyone; else 'SalesManager' etc.
    CONSTRAINT CK_ItemCategory_MaxQty CHECK (MaxQuantityPerOrder > 0)
);
GO

-- Items
IF OBJECT_ID('sales.Item') IS NULL
CREATE TABLE sales.Item (
    Id         BIGINT IDENTITY PRIMARY KEY,
    Sku        NVARCHAR(50) NOT NULL UNIQUE,
    Name       NVARCHAR(200) NOT NULL,
    CategoryId BIGINT NOT NULL REFERENCES sales.ItemCategory(Id),
    UnitPrice  DECIMAL(18,2) NOT NULL,
    IsActive   BIT NOT NULL DEFAULT 1,
    CONSTRAINT CK_Item_UnitPrice CHECK (UnitPrice >= 0)
);
GO

-- Sales orders and lines
IF OBJECT_ID('sales.SalesOrder') IS NULL
CREATE TABLE sales.SalesOrder (
    Id               BIGINT IDENTITY PRIMARY KEY,
    OrderNumber      NVARCHAR(30) NOT NULL UNIQUE,
    CustomerId       BIGINT NOT NULL REFERENCES sales.Customer(Id),
    Status           INT NOT NULL,   -- 0 Draft,1 PendingApproval,2 Approved,3 Cancelled,4 Rejected
    TotalAmount      DECIMAL(18,2) NOT NULL DEFAULT 0,
    CreatedByUserId  BIGINT NOT NULL REFERENCES [identity].AppUser(Id),
    CreatedUtc       DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    ApprovedByUserId BIGINT NULL REFERENCES [identity].AppUser(Id),
    ApprovedUtc      DATETIME2 NULL,
    RowVersion       ROWVERSION,
    CONSTRAINT CK_SalesOrder_Status CHECK (Status BETWEEN 0 AND 4),
    CONSTRAINT CK_SalesOrder_Total  CHECK (TotalAmount >= 0)
);
GO

IF OBJECT_ID('sales.SalesOrderLine') IS NULL
CREATE TABLE sales.SalesOrderLine (
    Id           BIGINT IDENTITY PRIMARY KEY,
    SalesOrderId BIGINT NOT NULL REFERENCES sales.SalesOrder(Id) ON DELETE CASCADE,
    ItemId       BIGINT NOT NULL REFERENCES sales.Item(Id),
    Quantity     INT NOT NULL,
    UnitPrice    DECIMAL(18,2) NOT NULL,
    CONSTRAINT CK_SalesOrderLine_Qty   CHECK (Quantity > 0),
    CONSTRAINT CK_SalesOrderLine_Price CHECK (UnitPrice >= 0)
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SalesOrderLine_OrderId')
    CREATE INDEX IX_SalesOrderLine_OrderId ON sales.SalesOrderLine(SalesOrderId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SalesOrder_CustomerId')
    CREATE INDEX IX_SalesOrder_CustomerId ON sales.SalesOrder(CustomerId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SalesOrder_Status')
    CREATE INDEX IX_SalesOrder_Status ON sales.SalesOrder(Status);
GO

-- Concurrency-safe order numbers (a SEQUENCE, not MAX()+1)
IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'SalesOrderNumberSeq')
    CREATE SEQUENCE sales.SalesOrderNumberSeq AS BIGINT START WITH 1 INCREMENT BY 1;
GO
