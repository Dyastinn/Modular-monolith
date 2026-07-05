-- Phase 3/7 — The atomic inventory guard. This is the one genuine "SQL owns it" rule.

CREATE OR ALTER PROCEDURE inventory.usp_Inventory_Reserve
    @ItemId BIGINT,
    @Qty    INT
AS
BEGIN
    SET NOCOUNT ON;
    -- The check and the write are ONE statement -> safe under concurrency.
    UPDATE inventory.Inventory
    SET Reserved = Reserved + @Qty
    WHERE ItemId = @ItemId
      AND (OnHand - Reserved) >= @Qty;   -- only if enough is available
    SELECT @@ROWCOUNT;                    -- 0 = not enough stock, 1 = reserved
END
GO

CREATE OR ALTER PROCEDURE inventory.usp_Inventory_Release
    @ItemId BIGINT,
    @Qty    INT
AS
BEGIN
    SET NOCOUNT ON;
    -- Never let Reserved go below 0, even if a buggy caller over-releases (the CHECK also backstops this).
    UPDATE inventory.Inventory
    SET Reserved = CASE WHEN Reserved - @Qty < 0 THEN 0 ELSE Reserved - @Qty END
    WHERE ItemId = @ItemId;
END
GO
