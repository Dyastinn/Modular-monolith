# Phase 2: Domain-Driven Design – Entities & Value Objects

## Objective
Separate **how the business works** from **how the database works**.

Build domain models that represent real business concepts, not database schema.

**Time Investment:** 3–4 hours  
**Deliverable:** Complete domain layer for Orders module with business logic

---

## The Problem We're Solving

In your WebForms background, what does an "Order" look like?

```csharp
' In SQL Server
SELECT OrderId, CustomerId, OrderDate, TotalAmount, Status FROM Orders

' In code
Public Class Order
    Public Property OrderId As Integer
    Public Property CustomerId As Integer
    Public Property OrderDate As DateTime
    Public Property TotalAmount As Decimal
    Public Property Status As String
End Class
```

This is a **database model**. It's shaped by table structure, not business logic.

**Problems:**
1. Calculations are mixed with data (TotalAmount: is it calculated or stored? When is it updated?)
2. Business rules are nowhere (what does "Status = 'Pending'" mean? What transitions are allowed?)
3. Logic is scattered (price calculation might be in SQL, in code, or both)
4. Testing is database-dependent

---

## The DDD Solution: Entities and Value Objects

### Entities

An **entity** is a domain concept that:
- Has a unique identity (an Order, not "a thing")
- Changes over time (order status transitions, items added, price changes)
- Maintains business invariants (an order can't have negative quantities)

### Value Objects

A **value object** is a domain concept that:
- Has no identity (two $50 notes are identical; you don't care which one)
- Is immutable (once created, doesn't change)
- Measures or describes something (Money, Quantity, Email, Address)

---

## Building the Domain: Step by Step

### Step 1: Create Identifiers (Strongly Typed IDs)

**Why?** Instead of passing raw `Integer` everywhere, use types that express intent.

**File:** `Domain/Shared/EntityId.cs`

```csharp
namespace OrderSystem.Domain.Shared
{
    ''' Generic strongly-typed ID
    ''' Prevents mixing OrderId with CustomerId at compile time
    Public Class EntityId(Of T)
        Public Property Value As T
        
        Sub New(value As T)
            Me.Value = value
        End Sub
        
        Public Overrides Function Equals(obj As Object) As Boolean
            If obj Is Nothing OrElse Not TypeOf obj Is EntityId(Of T) Then
                Return False
            End If
            Dim other = CType(obj, EntityId(Of T))
            Return Equals(Value, other.Value)
        End Function
        
        Public Overrides Function GetHashCode() As Integer
            Return Value.GetHashCode()
        End Function
        
        Public Overrides Function ToString() As String
            Return Value.ToString()
        End Function
    End Class
    
    ' Specific ID types (improve readability)
    Public Class OrderId
        Inherits EntityId(Of Integer)
        Sub New(value As Integer)
            MyBase.New(value)
        End Sub
    End Class
    
    Public Class CustomerId
        Inherits EntityId(Of Integer)
        Sub New(value As Integer)
            MyBase.New(value)
        End Sub
    End Class
    
    Public Class MaterialId
        Inherits EntityId(Of Integer)
        Sub New(value As Integer)
            MyBase.New(value)
        End Sub
    End Class
End Namespace
```

**Why this matters:**
```csharp
' Without strongly-typed IDs (dangerous):
CreateOrder(orderId: 123, customerId: 456)  ' Easy to swap these
CreateOrder(customerId: 456, orderId: 123)  ' Different meaning, same data

' With strongly-typed IDs (safe):
CreateOrder(orderId: New OrderId(123), customerId: New CustomerId(456))
CreateOrder(customerId: New CustomerId(456), orderId: New OrderId(123))  ' Compiler error!
```

### Step 2: Create Value Objects

Value objects are immutable. They represent measurements or descriptions.

**File:** `Domain/Shared/Money.cs`

```csharp
namespace OrderSystem.Domain.Shared
    ''' Value object: represents an amount of money
    ''' Immutable. Two instances with the same value are equal.
    Public Class Money
        Public Property Amount As Decimal
        Public Property Currency As String
        
        Sub New(amount As Decimal, currency As String)
            If amount < 0 Then
                Throw New ArgumentException("Money amount cannot be negative")
            End If
            Me.Amount = amount
            Me.Currency = currency
        End Sub
        
        ''' Add two amounts
        Public Function Add(other As Money) As Money
            If Me.Currency <> other.Currency Then
                Throw New InvalidOperationException("Cannot add different currencies")
            End If
            Return New Money(Me.Amount + other.Amount, Me.Currency)
        End Function
        
        ''' Multiply amount
        Public Function Multiply(factor As Decimal) As Money
            If factor < 0 Then
                Throw New ArgumentException("Factor cannot be negative")
            End If
            Return New Money(Me.Amount * factor, Me.Currency)
        End Function
        
        ''' Equality: two Money objects are equal if amount and currency match
        Public Overrides Function Equals(obj As Object) As Boolean
            If obj Is Nothing OrElse Not TypeOf obj Is Money Then
                Return False
            End If
            Dim other = CType(obj, Money)
            Return Me.Amount = other.Amount AndAlso Me.Currency = other.Currency
        End Function
        
        Public Overrides Function GetHashCode() As Integer
            Return (Amount.GetHashCode() * 397) Xor Currency.GetHashCode()
        End Function
        
        Public Overrides Function ToString() As String
            Return $"{Currency} {Amount}"
        End Function
    End Class
End Namespace
```

**File:** `Domain/Shared/Quantity.cs`

```csharp
namespace OrderSystem.Domain.Shared
    ''' Value object: represents a quantity
    ''' Immutable. Enforces business rule: quantities can't be negative
    Public Class Quantity
        Public Property Value As Decimal
        
        Sub New(value As Decimal)
            If value <= 0 Then
                Throw New ArgumentException("Quantity must be greater than zero")
            End If
            Me.Value = value
        End Sub
        
        Public Function Add(other As Quantity) As Quantity
            Return New Quantity(Me.Value + other.Value)
        End Function
        
        Public Function Subtract(other As Quantity) As Quantity
            Dim result = Me.Value - other.Value
            If result <= 0 Then
                Throw New ArgumentException("Cannot subtract to zero or negative")
            End If
            Return New Quantity(result)
        End Function
        
        Public Overrides Function Equals(obj As Object) As Boolean
            If obj Is Nothing OrElse Not TypeOf obj Is Quantity Then
                Return False
            End If
            Return Me.Value = CType(obj, Quantity).Value
        End Function
        
        Public Overrides Function GetHashCode() As Integer
            Return Value.GetHashCode()
        End Function
    End Class
End Namespace
```

**File:** `Domain/Shared/CustomerEmail.cs`

```csharp
namespace OrderSystem.Domain.Shared
    ''' Value object: represents a valid email address
    ''' Immutable. Enforces valid email format.
    Public Class CustomerEmail
        Public Property Value As String
        
        Sub New(value As String)
            If String.IsNullOrWhiteSpace(value) Then
                Throw New ArgumentException("Email cannot be empty")
            End If
            If Not value.Contains("@") Then
                Throw New ArgumentException("Invalid email format")
            End If
            Me.Value = value
        End Sub
        
        Public Overrides Function Equals(obj As Object) As Boolean
            If obj Is Nothing OrElse Not TypeOf obj Is CustomerEmail Then
                Return False
            End If
            Return String.Equals(Me.Value, CType(obj, CustomerEmail).Value, 
                                 StringComparison.OrdinalIgnoreCase)
        End Function
        
        Public Overrides Function GetHashCode() As Integer
            Return Value.ToLower().GetHashCode()
        End Function
        
        Public Overrides Function ToString() As String
            Return Value
        End Function
    End Class
End Namespace
```

**Why value objects are powerful:**

```csharp
' Without value objects (fragile):
Dim unitPrice As Decimal = -50  ' Whoops! Negative price allowed
Dim quantity As Decimal = 0     ' Whoops! Zero quantity allowed
CreateLineItem(materialId, unitPrice, quantity)  ' Garbage in, garbage out

' With value objects (safe):
Dim unitPrice = New Money(-50, "USD")    ' Exception! Prevented at construction
Dim quantity = New Quantity(0)           ' Exception! Prevented at construction
CreateLineItem(materialId, unitPrice, quantity)  ' Can't create invalid state
```

### Step 3: Create Domain Events

**File:** `Domain/Orders/Events/DomainEvent.cs`

```csharp
namespace OrderSystem.Domain.Orders.Events
    ''' Base class for domain events
    ''' Events represent something significant that happened in the business
    Public MustInherit Class DomainEvent
        Public Property OccurredAt As DateTime = DateTime.UtcNow
        Public Property OrderId As OrderId
    End Class
    
    ''' Fired when an order is created
    Public Class OrderCreatedEvent
        Inherits DomainEvent
        
        Public Property CustomerId As CustomerId
        Public Property Items As List(Of OrderLineItem)
        Public Property TotalAmount As Money
    End Class
    
    ''' Fired when an order is booked
    Public Class OrderBookedEvent
        Inherits DomainEvent
        
        Public Property BookedAt As DateTime
    End Class
    
    ''' Fired when inventory is insufficient
    Public Class InsufficientInventoryEvent
        Inherits DomainEvent
        
        Public Property MaterialId As MaterialId
        Public Property RequestedQuantity As Quantity
        Public Property AvailableQuantity As Quantity
    End Class
End Namespace
```

**Why events?**
- Track what happened (audit trail)
- Trigger other processes (when order is created, send email, update inventory)
- Keep different domains synchronized (Order module doesn't need to know about Inventory; it just publishes events)

### Step 4: Create the Order Aggregate Root

An **aggregate** is a cluster of domain objects that act as a unit.

**File:** `Domain/Orders/Order.cs`

```csharp
Imports OrderSystem.Domain.Shared
Imports OrderSystem.Domain.Orders.Events

namespace OrderSystem.Domain.Orders
    ''' Domain entity: Order
    ''' This represents a real business Order
    ''' NOT shaped by database schema, shaped by business rules
    Public Class Order
        
        ' Identity
        Public Property Id As OrderId
        
        ' Attributes
        Public Property CustomerId As CustomerId
        Public Property CreatedAt As DateTime
        Public Property Status As OrderStatus
        
        ' Collection (line items)
        Public Property LineItems As List(Of OrderLineItem)
        
        ' Domain events (things that happened)
        Private _domainEvents As New List(Of DomainEvent)
        Public ReadOnly Property DomainEvents As IReadOnlyList(Of DomainEvent)
            Get
                Return _domainEvents.AsReadOnly()
            End Get
        End Property
        
        ' Constructor for creating a new order
        Public Sub New(customerId As CustomerId)
            Me.Id = Nothing  ' Will be assigned by database
            Me.CustomerId = customerId
            Me.CreatedAt = DateTime.UtcNow
            Me.Status = OrderStatus.Draft
            Me.LineItems = New List(Of OrderLineItem)
        End Sub
        
        ' Private constructor for loading from database
        Private Sub New()
            Me.LineItems = New List(Of OrderLineItem)
        End Sub
        
        ''' Add an item to this order
        ''' Business rule: Can only add items to Draft orders
        Public Sub AddItem(materialId As MaterialId, quantity As Quantity, unitPrice As Money)
            If Me.Status <> OrderStatus.Draft Then
                Throw New InvalidOperationException(
                    $"Cannot add items to an order in {Status} status")
            End If
            
            Dim lineItem = New OrderLineItem With {
                .MaterialId = materialId,
                .Quantity = quantity,
                .UnitPrice = unitPrice
            }
            
            LineItems.Add(lineItem)
        End Sub
        
        ''' Remove an item from this order
        ''' Business rule: Can only remove items from Draft orders
        Public Sub RemoveItem(materialId As MaterialId)
            If Me.Status <> OrderStatus.Draft Then
                Throw New InvalidOperationException(
                    $"Cannot remove items from an order in {Status} status")
            End If
            
            Dim item = LineItems.FirstOrDefault(Function(i) i.MaterialId.Equals(materialId))
            If item Is Nothing Then
                Throw New InvalidOperationException("Item not found in order")
            End If
            
            LineItems.Remove(item)
        End Sub
        
        ''' Calculate total price for this order
        ''' Business logic: sum of (quantity * unit price) for all items
        Public Function CalculateTotal() As Money
            If LineItems.Count = 0 Then
                Return New Money(0, "USD")
            End If
            
            Dim total = New Money(0, "USD")
            For Each item In LineItems
                Dim itemTotal = item.UnitPrice.Multiply(item.Quantity.Value)
                total = total.Add(itemTotal)
            Next
            
            Return total
        End Function
        
        ''' Book this order (transition to Pending status)
        ''' Business rules:
        '''   - Must have at least one item
        '''   - Can only book Draft orders
        '''   - Publishing event: OrderBooked
        Public Sub Book()
            If LineItems.Count = 0 Then
                Throw New InvalidOperationException("Cannot book an empty order")
            End If
            
            If Me.Status <> OrderStatus.Draft Then
                Throw New InvalidOperationException(
                    $"Cannot book an order in {Status} status")
            End If
            
            Me.Status = OrderStatus.Pending
            _domainEvents.Add(New OrderBookedEvent With {
                .OrderId = Me.Id,
                .BookedAt = DateTime.UtcNow
            })
        End Sub
        
        ''' Approve this order (transition to Approved status)
        ''' Business rule: Can only approve Pending orders
        Public Sub Approve()
            If Me.Status <> OrderStatus.Pending Then
                Throw New InvalidOperationException(
                    $"Cannot approve an order in {Status} status")
            End If
            
            Me.Status = OrderStatus.Approved
        End Sub
        
        ''' Cancel this order
        ''' Business rule: Can cancel from Draft or Pending, not from Approved
        Public Sub Cancel(reason As String)
            If Me.Status = OrderStatus.Approved Then
                Throw New InvalidOperationException(
                    "Cannot cancel an approved order")
            End If
            
            Me.Status = OrderStatus.Cancelled
            ' Could add cancellation details here
        End Sub
        
        ''' Clear domain events after they've been published
        Public Sub ClearDomainEvents()
            _domainEvents.Clear()
        End Sub
    End Class
    
    ''' Value object: represents one line item in an order
    Public Class OrderLineItem
        Public Property MaterialId As MaterialId
        Public Property Quantity As Quantity
        Public Property UnitPrice As Money
        
        ' Note: This is immutable once created
        ' If you need to change it, remove and re-add
    End Class
    
    ''' Business concept: Order Status
    ''' Only valid states exist
    Public Enum OrderStatus
        Draft = 0       ' Order created but not yet submitted
        Pending = 1     ' Order submitted, awaiting approval
        Approved = 2    ' Order approved, ready to process
        Cancelled = 3   ' Order cancelled
    End Enum
End Namespace
```

**Key insights:**

1. **Business rules enforced in code:**
   - `AddItem` throws if status is not Draft
   - `Book` throws if order is empty
   - `CalculateTotal` computes price correctly

2. **No database concerns:**
   - No SQL
   - No ORM
   - No mapping

3. **State transitions are explicit:**
   - Draft → Pending (via `Book`)
   - Pending → Approved (via `Approve`)
   - Can't transition directly Draft → Approved

4. **Domain events track what happened:**
   - When order is booked, an event is raised
   - This event can trigger other processes

### Step 5: Create a Customer Entity

**File:** `Domain/Customers/Customer.cs`

```csharp
Imports OrderSystem.Domain.Shared

namespace OrderSystem.Domain.Customers
    ''' Domain entity: Customer
    Public Class Customer
        Public Property Id As CustomerId
        Public Property Name As String
        Public Property Email As CustomerEmail
        Public Property CreditLimit As Money
        Public Property IsActive As Boolean
        
        Sub New(name As String, email As CustomerEmail, creditLimit As Money)
            Me.Id = Nothing  ' Will be assigned by database
            Me.Name = name
            Me.Email = email
            Me.CreditLimit = creditLimit
            Me.IsActive = True
        End Sub
        
        ''' Business rule: Check if customer can place an order of given amount
        Public Function CanPlaceOrder(orderAmount As Money) As Boolean
            Return IsActive AndAlso orderAmount.Amount <= CreditLimit.Amount
        End Function
    End Class
End Namespace
```

### Step 6: Create a Material (Inventory) Entity

**File:** `Domain/Inventory/Material.cs`

```csharp
Imports OrderSystem.Domain.Shared

namespace OrderSystem.Domain.Inventory
    ''' Domain entity: Material (Item in inventory)
    Public Class Material
        Public Property Id As MaterialId
        Public Property Name As String
        Public Property UnitPrice As Money
        Public Property StockQuantity As Quantity
        
        Sub New(name As String, unitPrice As Money, initialStock As Quantity)
            Me.Id = Nothing  ' Will be assigned by database
            Me.Name = name
            Me.UnitPrice = unitPrice
            Me.StockQuantity = initialStock
        End Sub
        
        ''' Business rule: Check if sufficient stock exists
        Public Function HasSufficientStock(requestedQuantity As Quantity) As Boolean
            Return requestedQuantity.Value <= StockQuantity.Value
        End Function
        
        ''' Business rule: Reserve stock (reduce available quantity)
        Public Sub ReserveStock(quantity As Quantity)
            If Not HasSufficientStock(quantity) Then
                Throw New InvalidOperationException(
                    $"Insufficient stock. Available: {StockQuantity.Value}, Requested: {quantity.Value}")
            End If
            Me.StockQuantity = Me.StockQuantity.Subtract(quantity)
        End Sub
    End Class
End Namespace
```

---

## What's Different from Your WebForms Approach

### WebForms (Database-Centric)

```csharp
' Order is a mirror of the database table
Public Class Order
    Public Property OrderId As Integer
    Public Property CustomerId As Integer
    Public Property OrderDate As DateTime
    Public Property TotalAmount As Decimal  ' Do we calculate this? When?
    Public Property Status As String         ' What values are valid?
    
    ' No validation
    ' No business logic
    ' No events
End Class
```

### DDD (Business-Centric)

```csharp
' Order is a complete business entity
Public Class Order
    Public Property Id As OrderId            ' Strongly-typed ID
    Public Property CustomerId As CustomerId ' Strongly-typed ID
    Public Property CreatedAt As DateTime
    Public Property Status As OrderStatus    ' Enum (valid states only)
    Public Property LineItems As List(Of OrderLineItem)
    
    ' Calculation (not stored)
    Public Function CalculateTotal() As Money
        ' Business logic: how to compute price
    End Function
    
    ' Operations (state transitions)
    Public Sub Book()          ' Business rule: when can you book?
    Public Sub Approve()       ' Business rule: when can you approve?
    Public Sub Cancel()        ' Business rule: when can you cancel?
    
    ' Events (what happened)
    Public ReadOnly Property DomainEvents As IReadOnlyList(Of DomainEvent)
End Class
```

---

## Unit Tests for Domain Logic

**File:** `Tests/Domain/Orders/OrderTests.cs`

```csharp
Imports NUnit.Framework
Imports OrderSystem.Domain.Orders
Imports OrderSystem.Domain.Shared
Imports OrderSystem.Domain.Customers
Imports OrderSystem.Domain.Inventory

Namespace OrderSystem.Tests.Domain.Orders
    <TestFixture>
    Public Class OrderTests
        
        Private _testCustomerId As CustomerId
        Private _testMaterialId As MaterialId
        
        <SetUp>
        Public Sub Setup()
            _testCustomerId = New CustomerId(1)
            _testMaterialId = New MaterialId(1)
        End Sub
        
        <Test>
        Public Sub CreateOrder_InitializesDraft()
            Dim order = New Order(_testCustomerId)
            
            Assert.That(order.Status, [Is].EqualTo(OrderStatus.Draft))
            Assert.That(order.LineItems.Count, [Is].EqualTo(0))
            Assert.That(order.CreatedAt, [Is].LessThanOrEqualTo(DateTime.UtcNow))
        End Sub
        
        <Test>
        Public Sub AddItem_ToValidOrder_Succeeds()
            Dim order = New Order(_testCustomerId)
            Dim quantity = New Quantity(10)
            Dim unitPrice = New Money(50, "USD")
            
            order.AddItem(_testMaterialId, quantity, unitPrice)
            
            Assert.That(order.LineItems.Count, [Is].EqualTo(1))
        End Sub
        
        <Test>
        Public Sub AddItem_ToNonDraftOrder_Throws()
            Dim order = New Order(_testCustomerId)
            order.AddItem(_testMaterialId, New Quantity(5), New Money(50, "USD"))
            order.Book()  ' Status is now Pending
            
            Assert.Throws(Of InvalidOperationException)(
                Sub()
                    order.AddItem(_testMaterialId, New Quantity(10), New Money(50, "USD"))
                End Sub)
        End Sub
        
        <Test>
        Public Sub CalculateTotal_WithMultipleItems_ReturnsSum()
            Dim order = New Order(_testCustomerId)
            order.AddItem(_testMaterialId, New Quantity(2), New Money(50, "USD"))
            order.AddItem(New MaterialId(2), New Quantity(3), New Money(100, "USD"))
            
            Dim total = order.CalculateTotal()
            
            Assert.That(total.Amount, [Is].EqualTo(400))  ' (2*50) + (3*100)
        End Sub
        
        <Test>
        Public Sub Book_EmptyOrder_Throws()
            Dim order = New Order(_testCustomerId)
            
            Assert.Throws(Of InvalidOperationException)(Sub() order.Book())
        End Sub
        
        <Test>
        Public Sub Book_ValidOrder_TransitionsToKr()
            Dim order = New Order(_testCustomerId)
            order.AddItem(_testMaterialId, New Quantity(5), New Money(50, "USD"))
            
            order.Book()
            
            Assert.That(order.Status, [Is].EqualTo(OrderStatus.Pending))
            Assert.That(order.DomainEvents.Count, [Is].EqualTo(1))
            Assert.That(order.DomainEvents(0), TypeOf OrderBookedEvent)
        End Sub
        
        <Test>
        Public Sub Cancel_ApprovedOrder_Throws()
            Dim order = New Order(_testCustomerId)
            order.AddItem(_testMaterialId, New Quantity(5), New Money(50, "USD"))
            order.Book()
            order.Approve()
            
            Assert.Throws(Of InvalidOperationException)(Sub() order.Cancel("User cancelled"))
        End Sub
        
        <Test>
        Public Sub Cancel_PendingOrder_Succeeds()
            Dim order = New Order(_testCustomerId)
            order.AddItem(_testMaterialId, New Quantity(5), New Money(50, "USD"))
            order.Book()
            
            order.Cancel("User cancelled")
            
            Assert.That(order.Status, [Is].EqualTo(OrderStatus.Cancelled))
        End Sub
    End Class
End Namespace
```

**Notice:** No database calls. No mocks. Pure business logic testing.

---

## Key Concepts Summary

### Entity
- Has identity
- Changes over time
- Enforces business rules

### Value Object
- No identity (equality by value)
- Immutable
- Represents a measurement or concept

### Aggregate Root (Order)
- Is an entity
- Owns related entities (LineItems)
- Enforces invariants for its cluster

### Domain Event
- Represents something significant that happened
- Immutable
- Records intent and context

---

## Comparison: Before and After

### Before Phase 2 (Procedure-Focused)

```csharp
Handler:
  Create an order
  Add items
  Return OrderId

Tests:
  Mock database
  Mock repository
  Complicated setup
  Hard to understand what's being tested
```

### After Phase 2 (Domain-Focused)

```csharp
Handler:
  Create an Order entity (enforces Draft status)
  Call AddItem (enforces business rules)
  Save to database

Tests:
  Create Order entity (no database)
  Call AddItem
  Assert state changed correctly
  Simple, clear, obvious
```

---

## Next Steps

When you've completed Phase 2:

1. ✅ Value objects (Money, Quantity, Email)
2. ✅ Strongly-typed IDs (OrderId, CustomerId, MaterialId)
3. ✅ Domain entities (Order, Customer, Material)
4. ✅ Domain events (OrderCreated, OrderBooked)
5. ✅ Business rules enforced in entities
6. ✅ Unit tests without database

You'll move to **Phase 3: Database Integration with Dapper**.

This is where your entities meet the actual database.

