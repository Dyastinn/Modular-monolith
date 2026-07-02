# Phase 3: Database Integration – Dapper & Repositories

## Objective

Connect your domain entities to SQL Server using Dapper.

Understand the difference between domain models and database models.

Learn the repository pattern for data access.

**Time Investment:** 3–4 hours  
**Deliverable:** Dapper repository, stored procedures, integration tests

---

## The Problem We're Solving

Your domain entities are complete, but they live only in memory.

You need to:
- **Persist** entities to the database
- **Load** entities from the database
- **Query** without loading full entities (CQRS read model)
- **Keep them in sync**

### The Naive Approach (Doesn't Work)

```csharp
' Domain entity
Public Class Order
    Public Property Id As OrderId
    Public Property CustomerId As CustomerId
    Public Property Status As OrderStatus
    Public Property LineItems As List(Of OrderLineItem)
End Class

' Naively save to database
Dim mapper = New SqlMapper()  ' Not real, just illustration
mapper.Insert("Orders", order)
mapper.Insert("OrderLineItems", order.LineItems)
mapper.Insert("DomainEvents", order.DomainEvents)

' Problem 1: How do we map domain types to database columns?
' Problem 2: What if something fails halfway through?
' Problem 3: What if the order is huge?
' Problem 4: SQL must handle domain logic (concurrency, validation)
```

---

## Understanding the Three Models

### Model 1: Domain Model
**Location:** `Domain/Orders/Order.cs`  
**Owned by:** Business logic  
**Represents:** Business concepts

```csharp
Order
  - Id (OrderId)
  - CustomerId (CustomerId)
  - Status (OrderStatus enum)
  - LineItems (List(Of OrderLineItem))
  - CalculateTotal()
  - Book()
  - Approve()
```

### Model 2: Database Model
**Location:** SQL Server schema  
**Owned by:** DBA  
**Represents:** How data is stored

```sql
-- Orders table
CREATE TABLE Orders (
    OrderId INT PRIMARY KEY,
    CustomerId INT NOT NULL,
    CreatedAt DATETIME NOT NULL,
    Status INT NOT NULL,  -- 0=Draft, 1=Pending, 2=Approved, 3=Cancelled
    TotalAmount DECIMAL(10,2) NOT NULL  -- Denormalized for queries
)

-- OrderLineItems table
CREATE TABLE OrderLineItems (
    LineItemId INT PRIMARY KEY,
    OrderId INT NOT NULL FOREIGN KEY,
    MaterialId INT NOT NULL,
    Quantity DECIMAL(10,2) NOT NULL,
    UnitPrice DECIMAL(10,2) NOT NULL
)

-- DomainEvents table (audit trail)
CREATE TABLE DomainEvents (
    EventId INT PRIMARY KEY IDENTITY,
    OrderId INT NOT NULL,
    EventType NVARCHAR(100) NOT NULL,
    EventData NVARCHAR(MAX) NOT NULL,
    OccurredAt DATETIME NOT NULL
)
```

### Model 3: API/DTO Model
**Location:** `Controllers/`, `Features/*/Commands.cs`  
**Owned by:** API contract  
**Represents:** What the client sees

```csharp
' API Request
Public Class CreateOrderRequest
    Public Property CustomerId As Integer
    Public Property Items As List(Of OrderItemRequest)
End Class

' API Response
Public Class OrderResponse
    Public Property OrderId As Integer
    Public Property CustomerId As Integer
    Public Property Total As Decimal
    Public Property Status As String
End Class
```

---

## Step-by-Step Implementation

### Step 1: Create the Repository Interface

**File:** `Infrastructure/Repositories/IOrderRepository.cs`

```csharp
Imports OrderSystem.Domain.Orders
Imports OrderSystem.Domain.Shared

namespace OrderSystem.Infrastructure.Repositories
    ''' Repository for Order aggregates
    ''' Handles loading and saving Order entities
    Public Interface IOrderRepository
        
        ''' Load an order by ID
        ''' Returns Nothing if not found
        Function GetById(orderId As OrderId) As Task(Of Order)
        
        ''' Save a new order
        ''' Assigns OrderId and persists all related data
        Function Create(order As Order) As Task(Of OrderId)
        
        ''' Update an existing order
        ''' Persists all changes including new domain events
        Function Update(order As Order) As Task
        
        ''' Delete an order
        Function Delete(orderId As OrderId) As Task
        
        ''' Get all orders for a customer
        Function GetByCustomerId(customerId As CustomerId) As Task(Of List(Of Order))
    End Interface
End Namespace
```

**Why this interface?**
- Separates domain logic from data access
- Enables testing without a real database
- Allows swapping database implementations later

### Step 2: Create Database Models (Mapping Classes)

**File:** `Infrastructure/Data/Orders/OrderDataModel.cs`

```csharp
Imports OrderSystem.Domain.Orders
Imports OrderSystem.Domain.Shared

namespace OrderSystem.Infrastructure.Data.Orders
    ''' Database representation of Order
    ''' This is NOT a domain entity
    ''' It's shaped by database schema, not business logic
    Public Class OrderDataModel
        Public Property OrderId As Integer
        Public Property CustomerId As Integer
        Public Property CreatedAt As DateTime
        Public Property Status As Integer  ' Database stores enum as int
        Public Property LineItems As List(Of OrderLineItemDataModel)
    End Class
    
    ''' Database representation of OrderLineItem
    Public Class OrderLineItemDataModel
        Public Property LineItemId As Integer
        Public Property OrderId As Integer
        Public Property MaterialId As Integer
        Public Property Quantity As Decimal
        Public Property UnitPrice As Decimal
    End Class
    
    ''' Helper: convert between domain and database models
    Public Class OrderDataMapper
        
        ''' Convert domain Order to database model
        Public Shared Function ToDatabaseModel(order As Order) As OrderDataModel
            Return New OrderDataModel With {
                .OrderId = If(order.Id Is Nothing, 0, order.Id.Value),
                .CustomerId = order.CustomerId.Value,
                .CreatedAt = order.CreatedAt,
                .Status = CInt(order.Status),
                .LineItems = order.LineItems.Select(Function(item) New OrderLineItemDataModel With {
                    .MaterialId = item.MaterialId.Value,
                    .Quantity = item.Quantity.Value,
                    .UnitPrice = item.UnitPrice.Amount
                }).ToList()
            }
        End Function
        
        ''' Convert database model to domain Order
        Public Shared Function ToDomainModel(dataModel As OrderDataModel) As Order
            Dim order = New Order(New CustomerId(dataModel.CustomerId)) With {
                .Id = New OrderId(dataModel.OrderId),
                .CreatedAt = dataModel.CreatedAt,
                .Status = CType(dataModel.Status, OrderStatus)
            }
            
            For Each itemData In dataModel.LineItems
                Dim lineItem = New OrderLineItem With {
                    .MaterialId = New MaterialId(itemData.MaterialId),
                    .Quantity = New Quantity(itemData.Quantity),
                    .UnitPrice = New Money(itemData.UnitPrice, "USD")
                }
                order.LineItems.Add(lineItem)
            Next
            
            Return order
        End Function
    End Class
End Namespace
```

**Critical insight:**

The mapper separates concerns:
- Domain entities know about business rules
- Database models know about schema
- Mapper handles the translation

### Step 3: Implement the Repository with Dapper

**File:** `Infrastructure/Repositories/OrderRepository.cs`

```csharp
Imports System.Data
Imports System.Data.SqlClient
Imports Dapper
Imports OrderSystem.Domain.Orders
Imports OrderSystem.Domain.Shared
Imports OrderSystem.Infrastructure.Data.Orders

namespace OrderSystem.Infrastructure.Repositories
    ''' Dapper-based repository for Order aggregates
    Public Class OrderRepository
        Implements IOrderRepository
        
        Private _connectionString As String
        
        Public Sub New(connectionString As String)
            _connectionString = connectionString
        End Sub
        
        ''' Get order by ID
        Public Async Function GetById(orderId As OrderId) As Task(Of Order) _
            Implements IOrderRepository.GetById
            
            Using connection = New SqlConnection(_connectionString)
                Await connection.OpenAsync()
                
                ' Load order header
                Const orderQuery = "SELECT * FROM Orders WHERE OrderId = @OrderId"
                Dim orderData = Await connection.QueryFirstOrDefaultAsync(Of OrderDataModel)(
                    orderQuery,
                    New With {.OrderId = orderId.Value})
                
                If orderData Is Nothing Then
                    Return Nothing
                End If
                
                ' Load line items
                Const lineItemsQuery = _
                    "SELECT * FROM OrderLineItems WHERE OrderId = @OrderId"
                Dim lineItems = (Await connection.QueryAsync(Of OrderLineItemDataModel)(
                    lineItemsQuery,
                    New With {.OrderId = orderId.Value})).ToList()
                
                orderData.LineItems = lineItems
                
                ' Convert to domain model
                Return OrderDataMapper.ToDomainModel(orderData)
            End Using
        End Function
        
        ''' Create a new order
        Public Async Function Create(order As Order) As Task(Of OrderId) _
            Implements IOrderRepository.Create
            
            Using connection = New SqlConnection(_connectionString)
                Await connection.OpenAsync()
                Using transaction = connection.BeginTransaction()
                    Try
                        ' Insert order header
                        Const insertOrderQuery = _
                            "INSERT INTO Orders (CustomerId, CreatedAt, Status) " &
                            "VALUES (@CustomerId, @CreatedAt, @Status); " &
                            "SELECT CAST(SCOPE_IDENTITY() as int)"
                        
                        Dim newOrderId = Await connection.QuerySingleAsync(Of Integer)(
                            insertOrderQuery,
                            New With {
                                .CustomerId = order.CustomerId.Value,
                                .CreatedAt = order.CreatedAt,
                                .Status = CInt(order.Status)
                            },
                            transaction)
                        
                        order.Id = New OrderId(newOrderId)
                        
                        ' Insert line items
                        For Each lineItem In order.LineItems
                            Const insertLineItemQuery = _
                                "INSERT INTO OrderLineItems " &
                                "(OrderId, MaterialId, Quantity, UnitPrice) " &
                                "VALUES (@OrderId, @MaterialId, @Quantity, @UnitPrice)"
                            
                            Await connection.ExecuteAsync(
                                insertLineItemQuery,
                                New With {
                                    .OrderId = newOrderId,
                                    .MaterialId = lineItem.MaterialId.Value,
                                    .Quantity = lineItem.Quantity.Value,
                                    .UnitPrice = lineItem.UnitPrice.Amount
                                },
                                transaction)
                        Next
                        
                        ' Insert domain events
                        Await PublishDomainEvents(order, newOrderId, connection, transaction)
                        
                        Await transaction.CommitAsync()
                        Return New OrderId(newOrderId)
                    Catch
                        Await transaction.RollbackAsync()
                        Throw
                    End Try
                End Using
            End Using
        End Function
        
        ''' Update an existing order
        Public Async Function Update(order As Order) As Task _
            Implements IOrderRepository.Update
            
            Using connection = New SqlConnection(_connectionString)
                Await connection.OpenAsync()
                Using transaction = connection.BeginTransaction()
                    Try
                        ' Update order header
                        Const updateQuery = _
                            "UPDATE Orders SET Status = @Status WHERE OrderId = @OrderId"
                        
                        Await connection.ExecuteAsync(
                            updateQuery,
                            New With {
                                .Status = CInt(order.Status),
                                .OrderId = order.Id.Value
                            },
                            transaction)
                        
                        ' Note: We're NOT updating line items here
                        ' Line items are immutable once added
                        ' To change items, remove and re-add
                        
                        ' Insert new domain events
                        Await PublishDomainEvents(order, order.Id.Value, connection, transaction)
                        
                        Await transaction.CommitAsync()
                    Catch
                        Await transaction.RollbackAsync()
                        Throw
                    End Try
                End Using
            End Using
        End Function
        
        ''' Delete an order
        Public Async Function Delete(orderId As OrderId) As Task _
            Implements IOrderRepository.Delete
            
            Using connection = New SqlConnection(_connectionString)
                Await connection.OpenAsync()
                Using transaction = connection.BeginTransaction()
                    Try
                        ' Delete line items (cascade)
                        Const deleteItemsQuery = "DELETE FROM OrderLineItems WHERE OrderId = @OrderId"
                        Await connection.ExecuteAsync(
                            deleteItemsQuery,
                            New With {.OrderId = orderId.Value},
                            transaction)
                        
                        ' Delete order
                        Const deleteOrderQuery = "DELETE FROM Orders WHERE OrderId = @OrderId"
                        Await connection.ExecuteAsync(
                            deleteOrderQuery,
                            New With {.OrderId = orderId.Value},
                            transaction)
                        
                        ' Delete related events
                        Const deleteEventsQuery = "DELETE FROM DomainEvents WHERE OrderId = @OrderId"
                        Await connection.ExecuteAsync(
                            deleteEventsQuery,
                            New With {.OrderId = orderId.Value},
                            transaction)
                        
                        Await transaction.CommitAsync()
                    Catch
                        Await transaction.RollbackAsync()
                        Throw
                    End Try
                End Using
            End Using
        End Function
        
        ''' Get all orders for a customer
        Public Async Function GetByCustomerId(customerId As CustomerId) As Task(Of List(Of Order)) _
            Implements IOrderRepository.GetByCustomerId
            
            Using connection = New SqlConnection(_connectionString)
                Await connection.OpenAsync()
                
                Const query = "SELECT * FROM Orders WHERE CustomerId = @CustomerId"
                Dim orders = (Await connection.QueryAsync(Of OrderDataModel)(
                    query,
                    New With {.CustomerId = customerId.Value})).ToList()
                
                ' Load line items for each order
                For Each order In orders
                    Const lineItemsQuery = _
                        "SELECT * FROM OrderLineItems WHERE OrderId = @OrderId"
                    Dim lineItems = (Await connection.QueryAsync(Of OrderLineItemDataModel)(
                        lineItemsQuery,
                        New With {.OrderId = order.OrderId})).ToList()
                    
                    order.LineItems = lineItems
                Next
                
                Return orders.Select(Function(o) OrderDataMapper.ToDomainModel(o)).ToList()
            End Using
        End Function
        
        ''' Publish domain events to audit trail
        Private Async Function PublishDomainEvents(
            order As Order,
            orderId As Integer,
            connection As SqlConnection,
            transaction As SqlTransaction) As Task
            
            For Each [event] In order.DomainEvents
                Const insertQuery = _
                    "INSERT INTO DomainEvents (OrderId, EventType, EventData, OccurredAt) " &
                    "VALUES (@OrderId, @EventType, @EventData, @OccurredAt)"
                
                Dim eventData = Newtonsoft.Json.JsonConvert.SerializeObject([event])
                
                Await connection.ExecuteAsync(
                    insertQuery,
                    New With {
                        .OrderId = orderId,
                        .EventType = [event].GetType().Name,
                        .EventData = eventData,
                        .OccurredAt = [event].OccurredAt
                    },
                    transaction)
            Next
        End Function
    End Class
End Namespace
```

**Key concepts:**

1. **Dapper handles mapping:** `QueryAsync(Of OrderDataModel)` automatically maps SQL columns to properties.
2. **Transactions:** All operations (order + items + events) are atomic.
3. **Mapper pattern:** Database model → domain model conversion.
4. **Domain events are persisted:** Every event is logged for audit trail.

### Step 4: Create Stored Procedures for Complex Operations

**File:** `Infrastructure/Data/Scripts/CreateOrderProcedure.sql`

```sql
-- Stored procedure: Create an order with inventory validation
CREATE PROCEDURE sp_CreateOrderWithInventoryCheck
    @CustomerId INT,
    @CreatedAt DATETIME,
    @Status INT,
    @MaterialId INT,
    @Quantity DECIMAL(10,2),
    @UnitPrice DECIMAL(10,2),
    @NewOrderId INT OUTPUT
AS
BEGIN
    BEGIN TRANSACTION
    
    BEGIN TRY
        -- Step 1: Validate customer exists and is active
        IF NOT EXISTS (SELECT 1 FROM Customers WHERE CustomerId = @CustomerId AND IsActive = 1)
        BEGIN
            RAISERROR('Customer not found or inactive', 16, 1)
            RETURN
        END
        
        -- Step 2: Validate inventory exists and has sufficient stock
        IF NOT EXISTS (SELECT 1 FROM Materials WHERE MaterialId = @MaterialId)
        BEGIN
            RAISERROR('Material not found', 16, 1)
            RETURN
        END
        
        DECLARE @AvailableStock DECIMAL(10,2)
        SELECT @AvailableStock = StockQuantity FROM Materials WHERE MaterialId = @MaterialId
        
        IF @AvailableStock < @Quantity
        BEGIN
            RAISERROR('Insufficient inventory', 16, 1)
            RETURN
        END
        
        -- Step 3: Create order
        INSERT INTO Orders (CustomerId, CreatedAt, Status)
        VALUES (@CustomerId, @CreatedAt, @Status)
        
        SET @NewOrderId = SCOPE_IDENTITY()
        
        -- Step 4: Add line item
        INSERT INTO OrderLineItems (OrderId, MaterialId, Quantity, UnitPrice)
        VALUES (@NewOrderId, @MaterialId, @Quantity, @UnitPrice)
        
        -- Step 5: Reserve inventory (reduce stock)
        UPDATE Materials
        SET StockQuantity = StockQuantity - @Quantity
        WHERE MaterialId = @MaterialId
        
        -- Step 6: Log event
        INSERT INTO DomainEvents (OrderId, EventType, EventData, OccurredAt)
        VALUES (@NewOrderId, 'OrderCreatedEvent', '{}', @CreatedAt)
        
        COMMIT TRANSACTION
    END TRY
    BEGIN CATCH
        ROLLBACK TRANSACTION
        THROW
    END CATCH
END
```

**Why stored procedures?**

✓ **Database-level validation:** Customer exists, material exists, stock available.  
✓ **Atomicity:** All-or-nothing operation across multiple tables.  
✓ **Concurrency:** SQL Server's locking handles simultaneous orders.  
✗ **Not for all business logic:** Only database constraints, not domain rules.

### Step 5: Create Read Models for Queries

Remember CQRS? This is the "Query" side.

**File:** `Infrastructure/Data/Orders/OrderReadModel.cs`

```csharp
Imports System.Data.SqlClient
Imports Dapper

namespace OrderSystem.Infrastructure.Data.Orders
    ''' Read-optimized model for order queries
    ''' This is NOT a domain entity
    ''' It's optimized for API responses
    Public Class OrderReadModel
        Public Property OrderId As Integer
        Public Property CustomerId As Integer
        Public Property CustomerName As String
        Public Property CreatedAt As DateTime
        Public Property Status As String
        Public Property TotalAmount As Decimal
        Public Property LineItemCount As Integer
    End Class
    
    ''' Queries for orders (read-only)
    Public Class OrderQueryService
        
        Private _connectionString As String
        
        Public Sub New(connectionString As String)
            _connectionString = connectionString
        End Sub
        
        ''' Search orders by customer
        Public Async Function SearchOrdersByCustomer(customerId As Integer) As Task(Of List(Of OrderReadModel))
            Using connection = New SqlConnection(_connectionString)
                Const query = _
                    "SELECT " &
                    "  o.OrderId, " &
                    "  o.CustomerId, " &
                    "  c.Name AS CustomerName, " &
                    "  o.CreatedAt, " &
                    "  CASE WHEN o.Status = 0 THEN 'Draft' " &
                    "       WHEN o.Status = 1 THEN 'Pending' " &
                    "       WHEN o.Status = 2 THEN 'Approved' " &
                    "       WHEN o.Status = 3 THEN 'Cancelled' " &
                    "       ELSE 'Unknown' END AS Status, " &
                    "  SUM(li.Quantity * li.UnitPrice) AS TotalAmount, " &
                    "  COUNT(li.LineItemId) AS LineItemCount " &
                    "FROM Orders o " &
                    "LEFT JOIN Customers c ON o.CustomerId = c.CustomerId " &
                    "LEFT JOIN OrderLineItems li ON o.OrderId = li.OrderId " &
                    "WHERE o.CustomerId = @CustomerId " &
                    "GROUP BY o.OrderId, o.CustomerId, c.Name, o.CreatedAt, o.Status " &
                    "ORDER BY o.CreatedAt DESC"
                
                Return (Await connection.QueryAsync(Of OrderReadModel)(
                    query,
                    New With {.CustomerId = customerId})).ToList()
            End Using
        End Function
        
        ''' Get high-value orders (over $10,000)
        Public Async Function GetHighValueOrders() As Task(Of List(Of OrderReadModel))
            Using connection = New SqlConnection(_connectionString)
                Const query = _
                    "SELECT " &
                    "  o.OrderId, " &
                    "  o.CustomerId, " &
                    "  c.Name AS CustomerName, " &
                    "  o.CreatedAt, " &
                    "  CASE WHEN o.Status = 0 THEN 'Draft' " &
                    "       WHEN o.Status = 1 THEN 'Pending' " &
                    "       WHEN o.Status = 2 THEN 'Approved' " &
                    "       WHEN o.Status = 3 THEN 'Cancelled' " &
                    "       ELSE 'Unknown' END AS Status, " &
                    "  SUM(li.Quantity * li.UnitPrice) AS TotalAmount, " &
                    "  COUNT(li.LineItemId) AS LineItemCount " &
                    "FROM Orders o " &
                    "LEFT JOIN Customers c ON o.CustomerId = c.CustomerId " &
                    "LEFT JOIN OrderLineItems li ON o.OrderId = li.OrderId " &
                    "GROUP BY o.OrderId, o.CustomerId, c.Name, o.CreatedAt, o.Status " &
                    "HAVING SUM(li.Quantity * li.UnitPrice) > 10000 " &
                    "ORDER BY TotalAmount DESC"
                
                Return (Await connection.QueryAsync(Of OrderReadModel)(query)).ToList()
            End Using
        End Function
    End Class
End Namespace
```

**Critical insight:**

```csharp
' Creating an order requires loading all business logic
' This is why we have Order entity and repository
Dim order = orderRepository.GetById(orderId)
order.Book()
orderRepository.Update(order)

' But QUERYING orders only needs to show data
' No need to load full entities
' Just query the read model directly
Dim orderSummaries = queryService.SearchOrdersByCustomer(customerId)
' Fast, efficient, no business logic loaded
```

### Step 6: Create Database Schema SQL

**File:** `Infrastructure/Data/Scripts/CreateSchema.sql`

```sql
-- Create Customers table
CREATE TABLE Customers (
    CustomerId INT PRIMARY KEY IDENTITY(1,1),
    Name NVARCHAR(255) NOT NULL,
    Email NVARCHAR(255) NOT NULL UNIQUE,
    CreditLimit DECIMAL(10,2) NOT NULL,
    IsActive BIT NOT NULL DEFAULT 1
)

-- Create Materials table (inventory)
CREATE TABLE Materials (
    MaterialId INT PRIMARY KEY IDENTITY(1,1),
    Name NVARCHAR(255) NOT NULL,
    UnitPrice DECIMAL(10,2) NOT NULL,
    StockQuantity DECIMAL(10,2) NOT NULL
)

-- Create Orders table
CREATE TABLE Orders (
    OrderId INT PRIMARY KEY IDENTITY(1,1),
    CustomerId INT NOT NULL FOREIGN KEY REFERENCES Customers(CustomerId),
    CreatedAt DATETIME NOT NULL,
    Status INT NOT NULL,  -- 0=Draft, 1=Pending, 2=Approved, 3=Cancelled
    CONSTRAINT CK_Orders_Status CHECK (Status IN (0,1,2,3))
)

-- Create OrderLineItems table
CREATE TABLE OrderLineItems (
    LineItemId INT PRIMARY KEY IDENTITY(1,1),
    OrderId INT NOT NULL FOREIGN KEY REFERENCES Orders(OrderId),
    MaterialId INT NOT NULL FOREIGN KEY REFERENCES Materials(MaterialId),
    Quantity DECIMAL(10,2) NOT NULL,
    UnitPrice DECIMAL(10,2) NOT NULL,
    CONSTRAINT CK_OrderLineItems_Quantity CHECK (Quantity > 0)
)

-- Create DomainEvents table (audit trail)
CREATE TABLE DomainEvents (
    EventId INT PRIMARY KEY IDENTITY(1,1),
    OrderId INT NOT NULL FOREIGN KEY REFERENCES Orders(OrderId),
    EventType NVARCHAR(100) NOT NULL,
    EventData NVARCHAR(MAX) NOT NULL,
    OccurredAt DATETIME NOT NULL
)

-- Create indexes
CREATE INDEX IX_Orders_CustomerId ON Orders(CustomerId)
CREATE INDEX IX_OrderLineItems_OrderId ON OrderLineItems(OrderId)
CREATE INDEX IX_DomainEvents_OrderId ON DomainEvents(OrderId)
```

---

## Integration Tests (With Real Database)

**File:** `Tests/Integration/Repositories/OrderRepositoryTests.cs`

```csharp
Imports NUnit.Framework
Imports OrderSystem.Domain.Orders
Imports OrderSystem.Domain.Shared
Imports OrderSystem.Domain.Customers
Imports OrderSystem.Infrastructure.Repositories

Namespace OrderSystem.Tests.Integration.Repositories
    <TestFixture>
    Public Class OrderRepositoryTests
        
        Private _repository As OrderRepository
        Private _testConnectionString As String
        
        <SetUp>
        Public Sub Setup()
            ' Use test database connection string
            _testConnectionString = "Server=(local);Database=OrderSystemTest;Integrated Security=true;"
            _repository = New OrderRepository(_testConnectionString)
            
            ' Clear test data
            Using connection = New SqlConnection(_testConnectionString)
                connection.Open()
                connection.Execute("DELETE FROM OrderLineItems")
                connection.Execute("DELETE FROM DomainEvents")
                connection.Execute("DELETE FROM Orders")
                connection.Execute("DELETE FROM Customers")
                connection.Execute("DELETE FROM Materials")
                connection.Close()
            End Using
        End Sub
        
        <Test>
        Public Async Function Create_NewOrder_PersistsSuccessfully()
            ' Arrange
            Dim order = New Order(New CustomerId(1))
            order.AddItem(
                New MaterialId(1),
                New Quantity(5),
                New Money(50, "USD"))
            
            ' Act
            Dim orderId = Await _repository.Create(order)
            
            ' Assert
            Assert.That(orderId, [Is].NotNull)
            Assert.That(orderId.Value, [Is].GreaterThan(0))
        End Function
        
        <Test>
        Public Async Function GetById_ExistingOrder_ReturnsEntity()
            ' Arrange
            Dim order = New Order(New CustomerId(1))
            order.AddItem(New MaterialId(1), New Quantity(10), New Money(25, "USD"))
            Dim orderId = Await _repository.Create(order)
            
            ' Act
            Dim loadedOrder = Await _repository.GetById(orderId)
            
            ' Assert
            Assert.That(loadedOrder, [Is].NotNull)
            Assert.That(loadedOrder.Status, [Is].EqualTo(OrderStatus.Draft))
            Assert.That(loadedOrder.LineItems.Count, [Is].EqualTo(1))
        End Function
        
        <Test>
        Public Async Function Update_BookOrder_PersistsStatusChange()
            ' Arrange
            Dim order = New Order(New CustomerId(1))
            order.AddItem(New MaterialId(1), New Quantity(3), New Money(100, "USD"))
            Dim orderId = Await _repository.Create(order)
            
            ' Act
            Dim loadedOrder = Await _repository.GetById(orderId)
            loadedOrder.Book()
            Await _repository.Update(loadedOrder)
            
            ' Assert
            Dim reloadedOrder = Await _repository.GetById(orderId)
            Assert.That(reloadedOrder.Status, [Is].EqualTo(OrderStatus.Pending))
        End Function
        
        <Test>
        Public Async Function Delete_Order_RemovesFromDatabase()
            ' Arrange
            Dim order = New Order(New CustomerId(1))
            order.AddItem(New MaterialId(1), New Quantity(2), New Money(50, "USD"))
            Dim orderId = Await _repository.Create(order)
            
            ' Act
            Await _repository.Delete(orderId)
            
            ' Assert
            Dim deletedOrder = Await _repository.GetById(orderId)
            Assert.That(deletedOrder, [Is].Null)
        End Function
    End Class
End Namespace
```

**Notice:** These tests use a real database, not mocks.

---

## Key Insights

### The Three Concerns

| Layer | Owns | Concern |
|-------|------|---------|
| **Domain** | Order entity | Business rules, state transitions |
| **Repository** | Dapper queries | How to persist/load | 
| **Database** | SQL schema | Data integrity, relationships |

### When to Use Stored Procedures

✓ **DO use:** Database constraints, inventory locking, complex joins  
✗ **DON'T use:** Business logic, validations, calculations

```csharp
' Business logic (in domain entity)
order.Book()  ' Enforces "must have items", changes status

' Database logic (in stored procedure)
sp_CreateOrderWithInventoryCheck  ' Ensures stock available, atomic updates
```

### Read vs. Write

```csharp
' Writing (transactional, uses full entity)
Dim order = Await repository.GetById(orderId)
order.Approve()
Await repository.Update(order)

' Reading (optimized, uses read model)
Dim orders = Await queryService.SearchOrdersByCustomer(customerId)
```

---

## Next Steps

When you've completed Phase 3:

1. ✅ Repository pattern
2. ✅ Dapper integration
3. ✅ Database schema
4. ✅ Mapping domain ↔ database models
5. ✅ Integration tests
6. ✅ Stored procedures for complex operations
7. ✅ Read models for queries

You'll move to **Phase 4: Complete Feature Implementation**.

This is where everything comes together in a working end-to-end flow.

