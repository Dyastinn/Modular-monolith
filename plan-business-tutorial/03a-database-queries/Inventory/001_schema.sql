/* =====================================================================
   Inventory module — schema + table  (idempotent)
   Companion to Phase 3 §3.
   NOTE: ItemId is a cross-module id, deliberately NOT an FK to sales.Item
   -- a foreign key across module schemas would weld the modules together.
   ===================================================================== */

IF SCHEMA_ID('inventory') IS NULL EXEC('CREATE SCHEMA inventory;');
GO

IF OBJECT_ID('inventory.Inventory', 'U') IS NULL
BEGIN
    CREATE TABLE inventory.Inventory (
        ItemId   BIGINT PRIMARY KEY,        -- a REFERENCE to sales.Item, but NOT an FK (cross-module)
        OnHand   INT NOT NULL DEFAULT 0,    -- physically in the warehouse
        Reserved INT NOT NULL DEFAULT 0,    -- promised to orders, not yet shipped
        CONSTRAINT CK_Inventory_OnHand   CHECK (OnHand >= 0),
        CONSTRAINT CK_Inventory_Reserved CHECK (Reserved >= 0),
        CONSTRAINT CK_Inventory_NotOver  CHECK (Reserved <= OnHand)  -- can't reserve more than you have
    );
END
GO
