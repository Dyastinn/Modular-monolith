/* =====================================================================
   Inventory module — stored procedures  (CREATE OR ALTER = idempotent)
   Companion to Phase 3 §4. The atomic reserve is the ONE genuine
   "SQL owns it" rule in the whole course (safe under concurrency).
   ===================================================================== */

-- Atomic reserve-if-available: the check and the write are ONE statement.
CREATE OR ALTER PROCEDURE inventory.usp_Inventory_Reserve
    @ItemId BIGINT,
    @Qty    INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE inventory.Inventory
    SET Reserved = Reserved + @Qty
    WHERE ItemId = @ItemId
      AND (OnHand - Reserved) >= @Qty;   -- only if enough is available
    SELECT @@ROWCOUNT;                    -- 0 = not enough stock, 1 = reserved
END
GO

-- The matched pair for reserve: give reserved stock back (never below 0).
CREATE OR ALTER PROCEDURE inventory.usp_Inventory_Release
    @ItemId BIGINT,
    @Qty    INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE inventory.Inventory
    SET Reserved = CASE WHEN Reserved - @Qty < 0 THEN 0 ELSE Reserved - @Qty END
    WHERE ItemId = @ItemId;
END
GO
