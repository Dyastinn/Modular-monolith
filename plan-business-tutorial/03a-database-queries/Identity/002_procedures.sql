/* =====================================================================
   Identity module — stored procedures  (CREATE OR ALTER = idempotent)
   Companion to Phase 3 §4. Persistence only — the policy (is it expired?
   is this a replay?) is decided in the Phase 4 handler, not here.
   ===================================================================== */

CREATE OR ALTER PROCEDURE [identity].usp_AppUser_GetByUsername
    @Username NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Username, PasswordHash, Role, IsActive, SecurityStamp, PasswordExpiresUtc
    FROM [identity].AppUser
    WHERE Username = @Username;
END
GO

CREATE OR ALTER PROCEDURE [identity].usp_AppUser_GetById
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Username, PasswordHash, Role, IsActive, SecurityStamp, PasswordExpiresUtc
    FROM [identity].AppUser
    WHERE Id = @Id;
END
GO

-- Changing a password: rotate the stamp (logs out every session) and push the 90-day expiry — atomically.
CREATE OR ALTER PROCEDURE [identity].usp_AppUser_ChangePassword
    @UserId BIGINT, @NewPasswordHash NVARCHAR(500)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [identity].AppUser
    SET PasswordHash       = @NewPasswordHash,
        SecurityStamp      = NEWID(),                              -- every old refresh token is now stale
        PasswordExpiresUtc  = DATEADD(day, 90, SYSUTCDATETIME())   -- DB policy: 90 days from this change
    WHERE Id = @UserId;
END
GO

CREATE OR ALTER PROCEDURE [identity].usp_RefreshToken_Insert
    @TokenHash NVARCHAR(200), @UserId BIGINT, @FamilyId UNIQUEIDENTIFIER,
    @SecurityStamp UNIQUEIDENTIFIER, @ExpiresUtc DATETIME2, @AbsoluteExpiresUtc DATETIME2
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [identity].RefreshToken (TokenHash, UserId, FamilyId, SecurityStamp, ExpiresUtc, AbsoluteExpiresUtc)
    VALUES (@TokenHash, @UserId, @FamilyId, @SecurityStamp, @ExpiresUtc, @AbsoluteExpiresUtc);
END
GO

CREATE OR ALTER PROCEDURE [identity].usp_RefreshToken_GetByHash
    @TokenHash NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, TokenHash, UserId, FamilyId, SecurityStamp, ExpiresUtc, AbsoluteExpiresUtc, RevokedUtc
    FROM [identity].RefreshToken WHERE TokenHash = @TokenHash;
END
GO

CREATE OR ALTER PROCEDURE [identity].usp_RefreshToken_Revoke
    @TokenHash NVARCHAR(200), @ReplacedByHash NVARCHAR(200) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [identity].RefreshToken
    SET RevokedUtc = SYSUTCDATETIME(), ReplacedByHash = @ReplacedByHash
    WHERE TokenHash = @TokenHash AND RevokedUtc IS NULL;   -- revoking twice is a safe no-op
END
GO

CREATE OR ALTER PROCEDURE [identity].usp_RefreshToken_RevokeFamily
    @FamilyId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [identity].RefreshToken SET RevokedUtc = SYSUTCDATETIME()
    WHERE FamilyId = @FamilyId AND RevokedUtc IS NULL;     -- one statement kills the whole lineage
END
GO

CREATE OR ALTER PROCEDURE [identity].usp_RefreshToken_RevokeAllForUser
    @UserId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [identity].RefreshToken SET RevokedUtc = SYSUTCDATETIME()
    WHERE UserId = @UserId AND RevokedUtc IS NULL;
END
GO
