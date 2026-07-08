/* =====================================================================
   Identity module — audit log + triggers   (LOGS ONLY, no business logic)
   ---------------------------------------------------------------------
   These triggers write an append-only change record. They contain NO
   policy, NO validation, NO writes to business tables — just "row X was
   inserted/updated/deleted, by DB login Y, at time Z". Business intent
   (which app user, why) is captured by domain events, not here.
   See 03a-database-queries/README.md for the full rationale.
   ===================================================================== */

IF OBJECT_ID('[identity].ChangeLog', 'U') IS NULL
BEGIN
    CREATE TABLE [identity].ChangeLog (
        Id         BIGINT IDENTITY PRIMARY KEY,
        SchemaName SYSNAME    NOT NULL,
        TableName  SYSNAME    NOT NULL,
        Operation  CHAR(1)    NOT NULL,                       -- 'I' insert | 'U' update | 'D' delete
        KeyId      BIGINT     NULL,                           -- PK of the affected row
        ChangedUtc DATETIME2  NOT NULL DEFAULT SYSUTCDATETIME(),
        ChangedBy  SYSNAME    NOT NULL DEFAULT SUSER_SNAME(), -- DB login, NOT the app user
        CONSTRAINT CK_Identity_ChangeLog_Op CHECK (Operation IN ('I','U','D'))
    );
END
GO

CREATE OR ALTER TRIGGER [identity].trg_AppUser_Audit
ON [identity].AppUser
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    -- INSERT or UPDATE (row present in inserted)
    INSERT INTO [identity].ChangeLog (SchemaName, TableName, Operation, KeyId)
    SELECT 'identity', 'AppUser',
           CASE WHEN EXISTS (SELECT 1 FROM deleted) THEN 'U' ELSE 'I' END, i.Id
    FROM inserted i;
    -- DELETE (row only in deleted)
    INSERT INTO [identity].ChangeLog (SchemaName, TableName, Operation, KeyId)
    SELECT 'identity', 'AppUser', 'D', d.Id
    FROM deleted d WHERE NOT EXISTS (SELECT 1 FROM inserted);
END
GO

CREATE OR ALTER TRIGGER [identity].trg_RefreshToken_Audit
ON [identity].RefreshToken
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [identity].ChangeLog (SchemaName, TableName, Operation, KeyId)
    SELECT 'identity', 'RefreshToken',
           CASE WHEN EXISTS (SELECT 1 FROM deleted) THEN 'U' ELSE 'I' END, i.Id
    FROM inserted i;
    INSERT INTO [identity].ChangeLog (SchemaName, TableName, Operation, KeyId)
    SELECT 'identity', 'RefreshToken', 'D', d.Id
    FROM deleted d WHERE NOT EXISTS (SELECT 1 FROM inserted);
END
GO
