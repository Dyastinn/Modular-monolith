-- Phase 3/7 — Inventory schema. Owned exclusively by the Inventory module.
-- Note: ItemId is a REFERENCE to sales.Item, but deliberately NOT an FK — a cross-module-schema FK
-- would weld the modules together at the database level.

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'inventory') EXEC('CREATE SCHEMA inventory');
GO

IF OBJECT_ID('inventory.Inventory') IS NULL
CREATE TABLE inventory.Inventory (
    ItemId   BIGINT PRIMARY KEY,        -- resolves to sales.Item(Id) through the other module, not an FK
    OnHand   INT NOT NULL DEFAULT 0,    -- physically in the warehouse
    Reserved INT NOT NULL DEFAULT 0,    -- promised to orders, not yet shipped
    CONSTRAINT CK_Inventory_OnHand   CHECK (OnHand >= 0),
    CONSTRAINT CK_Inventory_Reserved CHECK (Reserved >= 0),
    CONSTRAINT CK_Inventory_NotOver  CHECK (Reserved <= OnHand)  -- "oversold" is impossible at the data level
);
GO
