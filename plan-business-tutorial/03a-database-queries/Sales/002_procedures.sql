/* =====================================================================
   Sales module — stored procedures  (CREATE OR ALTER = idempotent)
   Companion to Phase 3 §4. Procs persist + enforce INTEGRITY only;
   all policy (credit, blocking, approval) stays in the domain.
   ===================================================================== */

-- Persist the header, return the new id. No credit/blocking/approval logic here.
CREATE OR ALTER PROCEDURE sales.usp_SalesOrder_Insert
    @OrderNumber     NVARCHAR(30),
    @CustomerId      BIGINT,
    @Status          INT,
    @TotalAmount     DECIMAL(18,2),
    @CreatedByUserId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO sales.SalesOrder (OrderNumber, CustomerId, Status, TotalAmount, CreatedByUserId)
    VALUES (@OrderNumber, @CustomerId, @Status, @TotalAmount, @CreatedByUserId);
    SELECT CAST(SCOPE_IDENTITY() AS BIGINT);   -- return the new Id to the repository
END
GO

-- Version-checked status change (optimistic concurrency).
CREATE OR ALTER PROCEDURE sales.usp_SalesOrder_UpdateStatus
    @Id               BIGINT,
    @Status           INT,
    @ApprovedByUserId BIGINT = NULL,
    @RowVersion       BINARY(8)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE sales.SalesOrder
    SET Status = @Status,
        ApprovedByUserId = COALESCE(@ApprovedByUserId, ApprovedByUserId),
        ApprovedUtc = CASE WHEN @Status = 2 THEN SYSUTCDATETIME() ELSE ApprovedUtc END
    WHERE Id = @Id AND RowVersion = @RowVersion;   -- only if nobody changed it since we loaded it
    SELECT @@ROWCOUNT;   -- 0 => stale, someone else changed it first
END
GO

-- Hand out the next unique, concurrency-safe order number.
CREATE OR ALTER PROCEDURE sales.usp_SalesOrder_NextNumber
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @n BIGINT = NEXT VALUE FOR sales.SalesOrderNumberSeq;   -- atomic; no two callers get the same @n
    SELECT CONCAT('SO-', FORMAT(SYSUTCDATETIME(), 'yyyy'), '-', FORMAT(@n, '000000'));
END
GO

-- Signed delta: + on approval (they now owe), - when an approved order is cancelled.
CREATE OR ALTER PROCEDURE sales.usp_Customer_AdjustBalance
    @CustomerId BIGINT,
    @Delta      DECIMAL(18,2)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE sales.Customer
    SET OutstandingBalance = OutstandingBalance + @Delta
    WHERE Id = @CustomerId;
    -- CK_Customer_Balance (>= 0) is the backstop: reversing can't drive the balance negative.
END
GO
