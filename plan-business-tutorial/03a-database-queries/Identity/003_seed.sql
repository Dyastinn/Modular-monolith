/* =====================================================================
   Identity module — seed data  (guarded: insert once, never duplicate)
   Companion to Phase 3 §5.
   Password hashes are placeholders; Phase 4 shows how to generate real ones.
   ===================================================================== */

IF NOT EXISTS (SELECT 1 FROM [identity].AppUser)
BEGIN
    INSERT INTO [identity].AppUser (Username, PasswordHash, Role) VALUES
     ('rep',     '<hash-of-Password123!>', 'SalesRep'),
     ('manager', '<hash-of-Password123!>', 'SalesManager'),
     ('admin',   '<hash-of-Password123!>', 'Admin');
END
GO
