/* =====================================================================
   Identity module — seed data  (guarded: insert once, never duplicate)
   Companion to Phase 3 §5.
   Password hashes are placeholders; Phase 4 shows how to generate real ones.
   ===================================================================== */

-- PasswordExpiresUtc seeded 90 days out (the DB policy); SecurityStamp defaults to a fresh NEWID().
IF NOT EXISTS (SELECT 1 FROM [identity].AppUser)
BEGIN
    INSERT INTO [identity].AppUser (Username, PasswordHash, Role, PasswordExpiresUtc) VALUES
     ('rep',     '<hash-of-Password123!>', 'SalesRep',     DATEADD(day, 90, SYSUTCDATETIME())),
     ('manager', '<hash-of-Password123!>', 'SalesManager', DATEADD(day, 90, SYSUTCDATETIME())),
     ('admin',   '<hash-of-Password123!>', 'Admin',        DATEADD(day, 90, SYSUTCDATETIME()));
END
GO
