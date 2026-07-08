/* =====================================================================
   Identity module — schema + tables  (idempotent / re-runnable)
   Companion to Phase 3 §3. See 03a-database-queries/README.md.
   [identity] is bracketed everywhere: IDENTITY is a T-SQL reserved word.
   ===================================================================== */

IF SCHEMA_ID('identity') IS NULL EXEC('CREATE SCHEMA [identity];');
GO

IF OBJECT_ID('[identity].AppUser', 'U') IS NULL
BEGIN
    CREATE TABLE [identity].AppUser (
        Id                 BIGINT IDENTITY PRIMARY KEY,
        Username           NVARCHAR(100) NOT NULL UNIQUE,
        PasswordHash       NVARCHAR(500) NOT NULL,        -- NEVER store plain passwords
        Role               NVARCHAR(50)  NOT NULL,        -- 'SalesRep' | 'SalesManager' | 'Admin'
        IsActive           BIT NOT NULL DEFAULT 1,
        SecurityStamp      UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),  -- changes on every password change
        PasswordExpiresUtc DATETIME2 NOT NULL,            -- DB-driven policy: 90 days from last password change
        CONSTRAINT CK_AppUser_Role CHECK (Role IN ('SalesRep','SalesManager','Admin'))
    );
END
GO

IF OBJECT_ID('[identity].RefreshToken', 'U') IS NULL
BEGIN
    CREATE TABLE [identity].RefreshToken (
        Id                 BIGINT IDENTITY PRIMARY KEY,
        TokenHash          NVARCHAR(200) NOT NULL UNIQUE,   -- SHA-256 of the raw token; never the raw value
        UserId             BIGINT NOT NULL REFERENCES [identity].AppUser(Id),
        FamilyId           UNIQUEIDENTIFIER NOT NULL,       -- one login/device lineage
        SecurityStamp      UNIQUEIDENTIFIER NOT NULL,       -- user's stamp at mint; mismatch later = password changed
        ExpiresUtc         DATETIME2 NOT NULL,              -- sliding inactivity timeout
        AbsoluteExpiresUtc DATETIME2 NOT NULL,              -- hard cap = user's PasswordExpiresUtc at mint
        CreatedUtc         DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        RevokedUtc         DATETIME2 NULL,
        ReplacedByHash     NVARCHAR(200) NULL,              -- rotation audit chain
        CONSTRAINT CK_RefreshToken_Expiry CHECK (AbsoluteExpiresUtc >= ExpiresUtc)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_RefreshToken_UserId'
                 AND object_id = OBJECT_ID('[identity].RefreshToken'))
    CREATE INDEX IX_RefreshToken_UserId ON [identity].RefreshToken(UserId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_RefreshToken_FamilyId'
                 AND object_id = OBJECT_ID('[identity].RefreshToken'))
    CREATE INDEX IX_RefreshToken_FamilyId ON [identity].RefreshToken(FamilyId);
GO
