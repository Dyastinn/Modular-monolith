/* =====================================================================
   Inventory module — seed data  (guarded: insert once, never duplicate)
   Companion to Phase 3 §5. Item ids match sales.Item seed order.
   The restricted kit (ItemId 3) has thin stock on purpose.
   ===================================================================== */

IF NOT EXISTS (SELECT 1 FROM inventory.Inventory)
BEGIN
    INSERT INTO inventory.Inventory (ItemId, OnHand, Reserved) VALUES
     (1, 100, 0),
     (2,  50, 0),
     (3,   8, 0);   -- only 8 of the restricted kit exist
END
GO
