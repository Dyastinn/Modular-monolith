-- Phase 3 — Inventory seed. Item 3 (Restricted Kit) has thin stock so the concurrency race is testable.
-- Runs AFTER the Sales seed (items 1-3 exist by then).

IF NOT EXISTS (SELECT 1 FROM inventory.Inventory)
INSERT INTO inventory.Inventory (ItemId, OnHand, Reserved) VALUES
 (1, 100, 0),
 (2,  50, 0),
 (3,   8, 0);   -- only 8 of the restricted kit exist
GO
