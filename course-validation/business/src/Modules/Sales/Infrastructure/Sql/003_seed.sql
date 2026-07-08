-- Phase 3 — Seed data, designed to exercise every business rule.
-- All three users share the password 'Password123!'. The hash below is a real salted PBKDF2-HMAC-SHA256
-- value in the {iterations}.{salt}.{key} format PasswordHasher produces (Phase 4 §3), so login works
-- out of the box. (The tutorial leaves this as a <hash-of-Password123!> placeholder — filled in here.)

-- PasswordExpiresUtc seeded 90 days out (the DB policy); SecurityStamp defaults to a fresh NEWID().
IF NOT EXISTS (SELECT 1 FROM [identity].AppUser WHERE Username = 'rep')
INSERT INTO [identity].AppUser (Username, PasswordHash, Role, PasswordExpiresUtc) VALUES
 ('rep',     '600000.2KCoQTqX/lrPlY4QVlVPXg==.5yaeUmLG5LqYE9QBTspiKqER/NSvNhE7mzYQno9q3tE=', 'SalesRep',     DATEADD(day, 90, SYSUTCDATETIME())),
 ('manager', '600000.2KCoQTqX/lrPlY4QVlVPXg==.5yaeUmLG5LqYE9QBTspiKqER/NSvNhE7mzYQno9q3tE=', 'SalesManager', DATEADD(day, 90, SYSUTCDATETIME())),
 ('admin',   '600000.2KCoQTqX/lrPlY4QVlVPXg==.5yaeUmLG5LqYE9QBTspiKqER/NSvNhE7mzYQno9q3tE=', 'Admin',        DATEADD(day, 90, SYSUTCDATETIME()));
GO

IF NOT EXISTS (SELECT 1 FROM sales.Customer)
INSERT INTO sales.Customer (Name, IsBlocked, CreditLimit, OutstandingBalance) VALUES
 ('Acme Corp',        0, 50000, 10000),   -- healthy: 40k of headroom
 ('Beta Industries',  0,  5000,  4500),   -- almost maxed: only 500 left
 ('Gamma Traders',    1, 20000,     0);   -- BLOCKED
GO

IF NOT EXISTS (SELECT 1 FROM sales.ItemCategory)
INSERT INTO sales.ItemCategory (Name, MaxQuantityPerOrder, RequiredRole) VALUES
 ('General',    1000, NULL),           -- anyone, high cap
 ('Controlled',    5, 'SalesManager'); -- managers only, cap 5 per order
GO

IF NOT EXISTS (SELECT 1 FROM sales.Item)
INSERT INTO sales.Item (Sku, Name, CategoryId, UnitPrice) VALUES
 ('GEN-001', 'Widget',         1,  25.00),
 ('GEN-002', 'Gadget',         1,  40.00),
 ('CTL-001', 'Restricted Kit', 2, 500.00);
GO
