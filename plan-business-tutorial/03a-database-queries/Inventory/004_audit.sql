/* =====================================================================
   Inventory module — audit log + trigger   (LOGS ONLY, no business logic)
   ---------------------------------------------------------------------
   Records raw row changes for the DBA. The atomic reserve/release RULE
   stays in the stored procs + CHECK constraints; this trigger only logs
   that a change happened. NOTE: Inventory's key is ItemId, not Id.
   See 03a-database-queries/README.md.
   ===================================================================== */

IF OBJECT_ID('inventory.ChangeLog', 'U') IS NULL
BEGIN
    CREATE TABLE inventory.ChangeLog (
        Id         BIGINT IDENTITY PRIMARY KEY,
        SchemaName SYSNAME    NOT NULL,
        TableName  SYSNAME    NOT NULL,
        Operation  CHAR(1)    NOT NULL,                       -- 'I' | 'U' | 'D'
        KeyId      BIGINT     NULL,                           -- the affected ItemId
        ChangedUtc DATETIME2  NOT NULL DEFAULT SYSUTCDATETIME(),
        ChangedBy  SYSNAME    NOT NULL DEFAULT SUSER_SNAME(), -- DB login, NOT the app user
        CONSTRAINT CK_Inventory_ChangeLog_Op CHECK (Operation IN ('I','U','D'))
    );
END
GO

CREATE OR ALTER TRIGGER inventory.trg_Inventory_Audit
ON inventory.Inventory
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO inventory.ChangeLog (SchemaName, TableName, Operation, KeyId)
    SELECT 'inventory', 'Inventory',
           CASE WHEN EXISTS (SELECT 1 FROM deleted) THEN 'U' ELSE 'I' END, i.ItemId
    FROM inserted i;
    INSERT INTO inventory.ChangeLog (SchemaName, TableName, Operation, KeyId)
    SELECT 'inventory', 'Inventory', 'D', d.ItemId
    FROM deleted d WHERE NOT EXISTS (SELECT 1 FROM inserted);
END
GO
