/* =====================================================================
   Identity module — audit log + retention   (LOGS ONLY, no business logic)
   ---------------------------------------------------------------------
   Two different "record what happened" needs, handled two different ways:

     • AppUser      → an append-only audit TRIGGER (trg_AppUser_Audit).
                      Rows change rarely, so the ChangeLog stays small.

     • RefreshToken → NO trigger. Its rows churn (one INSERT per login and
                      per rotation, one UPDATE per revoke), so a per-row
                      trigger would bloat ChangeLog faster than the table
                      it audits. Instead, a retention job archives spent
                      tokens out of the hot table (usp_RefreshToken_Purge,
                      below) — keeping RefreshToken small while preserving
                      full history in RefreshTokenArchive.

   Neither path holds policy, validation, or writes to business tables —
   just "this row existed / changed". Business intent (which app user, why)
   is captured by domain events, not here. See 03a-database-queries/README.md.
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

/* ---------------------------------------------------------------------
   RefreshToken history — archive-on-retention, not a per-row trigger.

   Why not a trigger? RefreshToken is high-churn, and — more importantly —
   we must NOT delete a token the moment it's revoked: reuse detection
   (Phase 4 §10) works by finding an already-revoked row when a spent
   token is replayed. Delete it on revoke and a replay returns "not found"
   instead of "reuse detected", so the family is never burned and a thief
   who refreshed first keeps a live session. The row has to LINGER while it
   could still be replayed.

   So the rule is timing, not deletion: keep every token until its sliding
   window has closed (ExpiresUtc has passed), then a scheduled job moves the
   now-unusable rows into RefreshTokenArchive and clears them from the hot
   table. Reuse detection stays fully intact; RefreshToken stays small.
   --------------------------------------------------------------------- */

IF OBJECT_ID('[identity].RefreshTokenArchive', 'U') IS NULL
BEGIN
    CREATE TABLE [identity].RefreshTokenArchive (
        ArchiveId          BIGINT IDENTITY PRIMARY KEY,      -- the archive's own key
        Id                 BIGINT NOT NULL,                  -- the token's original RefreshToken.Id
        TokenHash          NVARCHAR(200) NOT NULL,           -- still only the HASH; a leak yields no usable token
        UserId             BIGINT NOT NULL,
        FamilyId           UNIQUEIDENTIFIER NOT NULL,
        SecurityStamp      UNIQUEIDENTIFIER NOT NULL,
        ExpiresUtc         DATETIME2 NOT NULL,
        AbsoluteExpiresUtc DATETIME2 NOT NULL,
        CreatedUtc         DATETIME2 NOT NULL,
        RevokedUtc         DATETIME2 NULL,
        ReplacedByHash     NVARCHAR(200) NULL,
        ArchivedUtc        DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_RefreshTokenArchive_UserId'
                 AND object_id = OBJECT_ID('[identity].RefreshTokenArchive'))
    CREATE INDEX IX_RefreshTokenArchive_UserId ON [identity].RefreshTokenArchive(UserId);
GO

-- Retention sweep: archive + purge every token whose sliding window closed at
-- least @RetentionDays ago. Keyed off ExpiresUtc, so a still-usable (or still-
-- replayable) token is NEVER removed — reuse detection keeps its full window.
-- A single DELETE ... OUTPUT ... INTO does the move atomically (this is also
-- why RefreshToken must have no trigger: OUTPUT INTO forbids one on the target).
-- Call it from a scheduler: a SQL Agent job (preferred where Agent exists) or an
-- in-process hosted BackgroundService (portable / LocalDB). Both are built in
-- Phase 4 §10. Nothing on the request path touches it.
CREATE OR ALTER PROCEDURE [identity].usp_RefreshToken_Purge
    @RetentionDays INT = 7   -- how long dead rows linger in the hot table before archiving
AS
BEGIN
    SET NOCOUNT ON;
    DELETE t
    OUTPUT deleted.Id, deleted.TokenHash, deleted.UserId, deleted.FamilyId,
           deleted.SecurityStamp, deleted.ExpiresUtc, deleted.AbsoluteExpiresUtc,
           deleted.CreatedUtc, deleted.RevokedUtc, deleted.ReplacedByHash
      INTO [identity].RefreshTokenArchive
           (Id, TokenHash, UserId, FamilyId, SecurityStamp, ExpiresUtc,
            AbsoluteExpiresUtc, CreatedUtc, RevokedUtc, ReplacedByHash)
    FROM [identity].RefreshToken AS t
    WHERE t.ExpiresUtc < DATEADD(day, -@RetentionDays, SYSUTCDATETIME());
    SELECT @@ROWCOUNT;   -- rows archived + purged this run
END
GO
