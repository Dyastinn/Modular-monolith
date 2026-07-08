/* =====================================================================
   Sales module — audit log + triggers   (LOGS ONLY, no business logic)
   ---------------------------------------------------------------------
   One append-only row per change. NO policy, NO validation, NO writes to
   business tables. The status/credit/approval rules are NOT here — they
   live in the domain (C#). See 03a-database-queries/README.md.
   ===================================================================== */

IF OBJECT_ID('sales.ChangeLog', 'U') IS NULL
BEGIN
    CREATE TABLE sales.ChangeLog (
        Id         BIGINT IDENTITY PRIMARY KEY,
        SchemaName SYSNAME    NOT NULL,
        TableName  SYSNAME    NOT NULL,
        Operation  CHAR(1)    NOT NULL,                       -- 'I' | 'U' | 'D'
        KeyId      BIGINT     NULL,
        ChangedUtc DATETIME2  NOT NULL DEFAULT SYSUTCDATETIME(),
        ChangedBy  SYSNAME    NOT NULL DEFAULT SUSER_SNAME(), -- DB login, NOT the app user
        CONSTRAINT CK_Sales_ChangeLog_Op CHECK (Operation IN ('I','U','D'))
    );
END
GO

CREATE OR ALTER TRIGGER sales.trg_Customer_Audit
ON sales.Customer
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO sales.ChangeLog (SchemaName, TableName, Operation, KeyId)
    SELECT 'sales', 'Customer',
           CASE WHEN EXISTS (SELECT 1 FROM deleted) THEN 'U' ELSE 'I' END, i.Id
    FROM inserted i;
    INSERT INTO sales.ChangeLog (SchemaName, TableName, Operation, KeyId)
    SELECT 'sales', 'Customer', 'D', d.Id
    FROM deleted d WHERE NOT EXISTS (SELECT 1 FROM inserted);
END
GO

CREATE OR ALTER TRIGGER sales.trg_ItemCategory_Audit
ON sales.ItemCategory
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO sales.ChangeLog (SchemaName, TableName, Operation, KeyId)
    SELECT 'sales', 'ItemCategory',
           CASE WHEN EXISTS (SELECT 1 FROM deleted) THEN 'U' ELSE 'I' END, i.Id
    FROM inserted i;
    INSERT INTO sales.ChangeLog (SchemaName, TableName, Operation, KeyId)
    SELECT 'sales', 'ItemCategory', 'D', d.Id
    FROM deleted d WHERE NOT EXISTS (SELECT 1 FROM inserted);
END
GO

CREATE OR ALTER TRIGGER sales.trg_Item_Audit
ON sales.Item
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO sales.ChangeLog (SchemaName, TableName, Operation, KeyId)
    SELECT 'sales', 'Item',
           CASE WHEN EXISTS (SELECT 1 FROM deleted) THEN 'U' ELSE 'I' END, i.Id
    FROM inserted i;
    INSERT INTO sales.ChangeLog (SchemaName, TableName, Operation, KeyId)
    SELECT 'sales', 'Item', 'D', d.Id
    FROM deleted d WHERE NOT EXISTS (SELECT 1 FROM inserted);
END
GO

CREATE OR ALTER TRIGGER sales.trg_SalesOrder_Audit
ON sales.SalesOrder
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO sales.ChangeLog (SchemaName, TableName, Operation, KeyId)
    SELECT 'sales', 'SalesOrder',
           CASE WHEN EXISTS (SELECT 1 FROM deleted) THEN 'U' ELSE 'I' END, i.Id
    FROM inserted i;
    INSERT INTO sales.ChangeLog (SchemaName, TableName, Operation, KeyId)
    SELECT 'sales', 'SalesOrder', 'D', d.Id
    FROM deleted d WHERE NOT EXISTS (SELECT 1 FROM inserted);
END
GO

CREATE OR ALTER TRIGGER sales.trg_SalesOrderLine_Audit
ON sales.SalesOrderLine
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO sales.ChangeLog (SchemaName, TableName, Operation, KeyId)
    SELECT 'sales', 'SalesOrderLine',
           CASE WHEN EXISTS (SELECT 1 FROM deleted) THEN 'U' ELSE 'I' END, i.Id
    FROM inserted i;
    INSERT INTO sales.ChangeLog (SchemaName, TableName, Operation, KeyId)
    SELECT 'sales', 'SalesOrderLine', 'D', d.Id
    FROM deleted d WHERE NOT EXISTS (SELECT 1 FROM inserted);
END
GO
