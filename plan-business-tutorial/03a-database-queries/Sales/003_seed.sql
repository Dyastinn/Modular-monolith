/* =====================================================================
   Sales module — seed data  (guarded: insert once, never duplicate)
   Companion to Phase 3 §5. Designed to exercise the business rules:
   Beta is near its credit limit, Gamma is blocked, Controlled is
   manager-only + capped at 5.
   ===================================================================== */

IF NOT EXISTS (SELECT 1 FROM sales.Customer)
BEGIN
    INSERT INTO sales.Customer (Name, IsBlocked, CreditLimit, OutstandingBalance) VALUES
     ('Acme Corp',        0, 50000, 10000),   -- healthy: PHP 40,000 of headroom
     ('Beta Industries',  0,  5000,  4500),   -- almost maxed: only PHP 500 left
     ('Gamma Traders',    1, 20000,     0);   -- BLOCKED
END
GO

IF NOT EXISTS (SELECT 1 FROM sales.ItemCategory)
BEGIN
    INSERT INTO sales.ItemCategory (Name, MaxQuantityPerOrder, RequiredRole) VALUES
     ('General',    1000, NULL),           -- anyone, high cap
     ('Controlled',    5, 'SalesManager'); -- managers only, cap 5 per order
END
GO

IF NOT EXISTS (SELECT 1 FROM sales.Item)
BEGIN
    INSERT INTO sales.Item (Sku, Name, CategoryId, UnitPrice) VALUES
     ('GEN-001', 'Widget',         1,  25.00),
     ('GEN-002', 'Gadget',         1,  40.00),
     ('CTL-001', 'Restricted Kit', 2, 500.00);
END
GO
