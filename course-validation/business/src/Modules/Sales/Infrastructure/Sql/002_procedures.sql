-- Phase 3 — Sales + [identity] stored procedures. Each repository method calls one proc.
-- Procs persist and enforce INTEGRITY; all POLICY stays in the domain.

CREATE OR ALTER PROCEDURE [identity].usp_AppUser_GetByUsername
    @Username NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Username, PasswordHash, Role, IsActive
    FROM [identity].AppUser
    WHERE Username = @Username;
END
GO

CREATE OR ALTER PROCEDURE [identity].usp_AppUser_GetById
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Username, PasswordHash, Role, IsActive
    FROM [identity].AppUser
    WHERE Id = @Id;
END
GO

-- Refresh-token procs (Phase 4 §9). Persistence only — the policy (expired? replayed?) is in the handler.
CREATE OR ALTER PROCEDURE [identity].usp_RefreshToken_Insert
    @TokenHash NVARCHAR(200), @UserId BIGINT, @FamilyId UNIQUEIDENTIFIER,
    @ExpiresUtc DATETIME2, @AbsoluteExpiresUtc DATETIME2
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [identity].RefreshToken (TokenHash, UserId, FamilyId, ExpiresUtc, AbsoluteExpiresUtc)
    VALUES (@TokenHash, @UserId, @FamilyId, @ExpiresUtc, @AbsoluteExpiresUtc);
END
GO

CREATE OR ALTER PROCEDURE [identity].usp_RefreshToken_GetByHash
    @TokenHash NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, TokenHash, UserId, FamilyId, ExpiresUtc, AbsoluteExpiresUtc, RevokedUtc
    FROM [identity].RefreshToken WHERE TokenHash = @TokenHash;
END
GO

CREATE OR ALTER PROCEDURE [identity].usp_RefreshToken_Revoke
    @TokenHash NVARCHAR(200), @ReplacedByHash NVARCHAR(200) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [identity].RefreshToken
    SET RevokedUtc = SYSUTCDATETIME(), ReplacedByHash = @ReplacedByHash
    WHERE TokenHash = @TokenHash AND RevokedUtc IS NULL;
END
GO

CREATE OR ALTER PROCEDURE [identity].usp_RefreshToken_RevokeFamily
    @FamilyId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [identity].RefreshToken SET RevokedUtc = SYSUTCDATETIME()
    WHERE FamilyId = @FamilyId AND RevokedUtc IS NULL;   -- one statement kills the whole lineage
END
GO

CREATE OR ALTER PROCEDURE [identity].usp_RefreshToken_RevokeAllForUser
    @UserId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE [identity].RefreshToken SET RevokedUtc = SYSUTCDATETIME()
    WHERE UserId = @UserId AND RevokedUtc IS NULL;
END
GO

CREATE OR ALTER PROCEDURE sales.usp_Customer_GetById
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Name, IsBlocked, CreditLimit, OutstandingBalance
    FROM sales.Customer
    WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE sales.usp_Customer_AdjustBalance
    @CustomerId BIGINT,
    @Delta      DECIMAL(18,2)   -- + on approval, − when an approved order is cancelled
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE sales.Customer
    SET OutstandingBalance = OutstandingBalance + @Delta
    WHERE Id = @CustomerId;
END
GO

CREATE OR ALTER PROCEDURE sales.usp_Item_GetByIds
    @ItemIds NVARCHAR(MAX)   -- CSV of item ids
AS
BEGIN
    SET NOCOUNT ON;
    SELECT i.Id AS ItemId, i.IsActive, i.UnitPrice,
           c.Id AS CategoryId, c.Name AS CategoryName, c.MaxQuantityPerOrder, c.RequiredRole
    FROM sales.Item i
    JOIN sales.ItemCategory c ON c.Id = i.CategoryId
    WHERE i.Id IN (SELECT CAST(value AS BIGINT) FROM STRING_SPLIT(@ItemIds, ','));
END
GO

CREATE OR ALTER PROCEDURE sales.usp_SalesOrder_NextNumber
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @n BIGINT = NEXT VALUE FOR sales.SalesOrderNumberSeq;   -- atomic; no two callers get the same @n
    SELECT CONCAT('SO-', FORMAT(SYSUTCDATETIME(), 'yyyy'), '-', FORMAT(@n, '000000'));
END
GO

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

CREATE OR ALTER PROCEDURE sales.usp_SalesOrderLine_Insert
    @SalesOrderId BIGINT,
    @ItemId       BIGINT,
    @Quantity     INT,
    @UnitPrice    DECIMAL(18,2)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO sales.SalesOrderLine (SalesOrderId, ItemId, Quantity, UnitPrice)
    VALUES (@SalesOrderId, @ItemId, @Quantity, @UnitPrice);
END
GO

CREATE OR ALTER PROCEDURE sales.usp_SalesOrder_GetById
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, OrderNumber, CustomerId, Status, CreatedByUserId, ApprovedByUserId, RowVersion
    FROM sales.SalesOrder
    WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE sales.usp_SalesOrderLine_GetByOrder
    @SalesOrderId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ItemId, Quantity, UnitPrice
    FROM sales.SalesOrderLine
    WHERE SalesOrderId = @SalesOrderId;
END
GO

CREATE OR ALTER PROCEDURE sales.usp_SalesOrder_GetDetail
    @OrderId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    -- Result set 1: header + customer name
    SELECT o.Id, o.OrderNumber, o.CustomerId, c.Name AS CustomerName,
           o.Status, o.TotalAmount AS Total, o.CreatedUtc
    FROM sales.SalesOrder o
    JOIN sales.Customer c ON c.Id = o.CustomerId
    WHERE o.Id = @OrderId;

    -- Result set 2: lines + item name
    SELECT l.ItemId, i.Name AS ItemName, l.Quantity, l.UnitPrice
    FROM sales.SalesOrderLine l
    JOIN sales.Item i ON i.Id = l.ItemId
    WHERE l.SalesOrderId = @OrderId;
END
GO

CREATE OR ALTER PROCEDURE sales.usp_SalesOrderLine_DeleteByOrder
    @SalesOrderId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM sales.SalesOrderLine WHERE SalesOrderId = @SalesOrderId;
END
GO

CREATE OR ALTER PROCEDURE sales.usp_SalesOrder_UpdateTotal
    @Id          BIGINT,
    @TotalAmount DECIMAL(18,2)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE sales.SalesOrder SET TotalAmount = @TotalAmount WHERE Id = @Id;
END
GO

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

CREATE OR ALTER PROCEDURE sales.usp_SalesOrders_Search
    @CustomerId BIGINT = NULL,
    @Status     INT    = NULL,
    @Page       INT,
    @PageSize   INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT o.Id, o.OrderNumber, c.Name AS CustomerName, o.Status, o.TotalAmount AS Total, o.CreatedUtc
    FROM sales.SalesOrder o
    JOIN sales.Customer c ON c.Id = o.CustomerId
    WHERE (@CustomerId IS NULL OR o.CustomerId = @CustomerId)
      AND (@Status     IS NULL OR o.Status     = @Status)
    ORDER BY o.CreatedUtc DESC
    OFFSET (@Page - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;

    SELECT COUNT(*)
    FROM sales.SalesOrder o
    WHERE (@CustomerId IS NULL OR o.CustomerId = @CustomerId)
      AND (@Status     IS NULL OR o.Status     = @Status);
END
GO
