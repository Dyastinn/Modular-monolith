# Phase 4: Complete Feature Implementation – End-to-End Workflows

## Objective

Implement complete features from HTTP request to database and back.

See how all the pieces fit together in a real scenario.

**Time Investment:** 4–5 hours  
**Deliverable:** 4 complete features (CreateOrder, BookOrder, CancelOrder, GetOrders)

---

## What This Phase Covers

Phase 1: ✅ Mediator  
Phase 2: ✅ Domain entities  
Phase 3: ✅ Database  
**Phase 4: ✅ Complete workflows**

This phase ties everything together.

---

## Feature 1: CreateOrder (Complete Implementation)

This feature creates a new order with business validation.

### Step 1: Define the Command

**File:** `Features/Orders/CreateOrder/CreateOrderCommand.cs`

```csharp
Imports OrderSystem.Core.Mediator
Imports OrderSystem.Domain.Shared

namespace OrderSystem.Features.Orders.CreateOrder
    ''' Command: Create a new order
    ''' Represents a user's intent to create an order
    Public Class CreateOrderCommand
        Implements IRequest(Of CreateOrderResponse)
        
        Public Property CustomerId As Integer
        Public Property Items As List(Of CreateOrderItemDto)
        Public Property RequestedAt As DateTime
    End Class
    
    ''' Data transfer object for line items
    Public Class CreateOrderItemDto
        Public Property MaterialId As Integer
        Public Property Quantity As Decimal
        Public Property UnitPrice As Decimal
    End Class
    
    ''' Response: the created order
    Public Class CreateOrderResponse
        Public Property OrderId As Integer
        Public Property CustomerId As Integer
        Public Property Status As String
        Public Property Total As Decimal
    End Class
End Namespace
```

### Step 2: Create the Validator

**File:** `Features/Orders/CreateOrder/CreateOrderValidator.cs`

```csharp
Imports OrderSystem.Core.Mediator
Imports OrderSystem.Infrastructure.Repositories

namespace OrderSystem.Features.Orders.CreateOrder
    ''' Validates CreateOrderCommand at the application layer
    ''' Does NOT validate business rules (that's the domain entity's job)
    ''' Only validates input format and existence checks
    Public Class CreateOrderValidator
        Implements IValidator(Of CreateOrderCommand)
        
        Private _customerRepository As ICustomerRepository
        Private _materialRepository As IMaterialRepository
        
        Public Sub New(
            customerRepository As ICustomerRepository,
            materialRepository As IMaterialRepository)
            _customerRepository = customerRepository
            _materialRepository = materialRepository
        End Sub
        
        Public Async Function Validate(command As CreateOrderCommand) As Task(Of ValidationResult)
            Dim result = New ValidationResult()
            
            ' Input format validation
            If command.CustomerId <= 0 Then
                result.AddError("Customer ID must be positive")
            End If
            
            If command.Items Is Nothing OrElse command.Items.Count = 0 Then
                result.AddError("Order must contain at least one item")
            Else
                For Each item In command.Items
                    If item.Quantity <= 0 Then
                        result.AddError("Item quantity must be positive")
                    End If
                    If item.UnitPrice < 0 Then
                        result.AddError("Item price cannot be negative")
                    End If
                Next
            End If
            
            ' Existence checks (require async, so needs repository)
            Dim customer = Await _customerRepository.GetById(New CustomerId(command.CustomerId))
            If customer Is Nothing Then
                result.AddError("Customer not found")
            End If
            
            ' Validate each material exists
            For Each item In command.Items
                Dim material = Await _materialRepository.GetById(New MaterialId(item.MaterialId))
                If material Is Nothing Then
                    result.AddError($"Material {item.MaterialId} not found")
                End If
            Next
            
            Return result
        End Function
    End Class
End Namespace
```

### Step 3: Create the Handler

**File:** `Features/Orders/CreateOrder/CreateOrderHandler.cs`

```csharp
Imports OrderSystem.Core.Mediator
Imports OrderSystem.Domain.Orders
Imports OrderSystem.Domain.Shared
Imports OrderSystem.Infrastructure.Repositories

namespace OrderSystem.Features.Orders.CreateOrder
    ''' Handles the CreateOrder command
    ''' Orchestrates domain entities, repositories, and business logic
    Public Class CreateOrderHandler
        Implements IRequestHandler(Of CreateOrderCommand, CreateOrderResponse)
        
        Private _orderRepository As IOrderRepository
        Private _customerRepository As ICustomerRepository
        Private _materialRepository As IMaterialRepository
        Private _domainEventPublisher As IDomainEventPublisher
        
        Public Sub New(
            orderRepository As IOrderRepository,
            customerRepository As ICustomerRepository,
            materialRepository As IMaterialRepository,
            domainEventPublisher As IDomainEventPublisher)
            _orderRepository = orderRepository
            _customerRepository = customerRepository
            _materialRepository = materialRepository
            _domainEventPublisher = domainEventPublisher
        End Sub
        
        Public Async Function Handle(command As CreateOrderCommand) As Task(Of CreateOrderResponse) _
            Implements IRequestHandler(Of CreateOrderCommand, CreateOrderResponse).Handle
            
            ' Step 1: Load customer (for validation and context)
            Dim customer = Await _customerRepository.GetById(New CustomerId(command.CustomerId))
            If customer Is Nothing Then
                Throw New InvalidOperationException("Customer not found")
            End If
            
            ' Step 2: Create domain entity
            Dim order = New Order(customer.Id)
            
            ' Step 3: Load materials and add items to order
            ' This is where domain rules are enforced
            For Each itemDto In command.Items
                Dim material = Await _materialRepository.GetById(New MaterialId(itemDto.MaterialId))
                If material Is Nothing Then
                    Throw New InvalidOperationException($"Material not found: {itemDto.MaterialId}")
                End If
                
                Dim quantity = New Quantity(itemDto.Quantity)
                Dim price = New Money(itemDto.UnitPrice, "USD")
                
                ' Domain entity enforces: can only add to Draft orders
                order.AddItem(material.Id, quantity, price)
            Next
            
            ' Step 4: Check business rules
            ' Rule: Order total must not exceed customer's credit limit
            Dim orderTotal = order.CalculateTotal()
            If Not customer.CanPlaceOrder(orderTotal) Then
                Throw New InvalidOperationException(
                    $"Order total ${orderTotal.Amount} exceeds credit limit ${customer.CreditLimit.Amount}")
            End If
            
            ' Step 5: Persist to database
            Dim newOrderId = Await _orderRepository.Create(order)
            
            ' Step 6: Publish domain events
            ' This triggers other processes (send email, update inventory, etc.)
            For Each [event] In order.DomainEvents
                Await _domainEventPublisher.Publish([event])
            Next
            order.ClearDomainEvents()
            
            ' Step 7: Return response
            Return New CreateOrderResponse With {
                .OrderId = newOrderId.Value,
                .CustomerId = customer.Id.Value,
                .Status = order.Status.ToString(),
                .Total = orderTotal.Amount
            }
        End Function
    End Class
End Namespace
```

### Step 4: Create the API Endpoint

**File:** `Controllers/OrdersController.cs`

```csharp
Imports Microsoft.AspNetCore.Mvc
Imports OrderSystem.Core.Mediator
Imports OrderSystem.Features.Orders.CreateOrder

Namespace OrderSystem.Controllers
    <ApiController>
    <Route("api/[controller]")>
    Public Class OrdersController
        Inherits ControllerBase
        
        Private _mediator As Mediator
        
        Public Sub New(mediator As Mediator)
            _mediator = mediator
        End Sub
        
        ''' POST /api/orders
        ''' Create a new order
        <HttpPost>
        Public Async Function CreateOrder(
            <FromBody> request As CreateOrderRequest) As Task(Of IActionResult)
            
            Try
                Dim command = New CreateOrderCommand With {
                    .CustomerId = request.CustomerId,
                    .Items = request.Items.Select(Function(i) New CreateOrderItemDto With {
                        .MaterialId = i.MaterialId,
                        .Quantity = i.Quantity,
                        .UnitPrice = i.UnitPrice
                    }).ToList(),
                    .RequestedAt = DateTime.UtcNow
                }
                
                Dim response = Await _mediator.Send(command)
                
                Return CreatedAtAction(
                    nameof(GetOrder),
                    New With {.orderId = response.OrderId},
                    response)
                
            Catch ex As ValidationException
                Return BadRequest(New With {.error = ex.Message})
            Catch ex As InvalidOperationException
                Return BadRequest(New With {.error = ex.Message})
            Catch ex As Exception
                Return StatusCode(500, New With {.error = "Internal server error"})
            End Try
        End Function
    End Class
    
    Public Class CreateOrderRequest
        Public Property CustomerId As Integer
        Public Property Items As List(Of OrderItemRequest)
    End Class
    
    Public Class OrderItemRequest
        Public Property MaterialId As Integer
        Public Property Quantity As Decimal
        Public Property UnitPrice As Decimal
    End Class
End Namespace
```

### Step 5: Unit Test the Handler

**File:** `Tests/Features/Orders/CreateOrder/CreateOrderHandlerTests.cs`

```csharp
Imports NUnit.Framework
Imports Moq
Imports OrderSystem.Features.Orders.CreateOrder
Imports OrderSystem.Domain.Orders
Imports OrderSystem.Domain.Shared
Imports OrderSystem.Domain.Customers
Imports OrderSystem.Domain.Inventory
Imports OrderSystem.Infrastructure.Repositories

Namespace OrderSystem.Tests.Features.Orders.CreateOrder
    <TestFixture>
    Public Class CreateOrderHandlerTests
        
        Private _handler As CreateOrderHandler
        Private _orderRepositoryMock As Mock(Of IOrderRepository)
        Private _customerRepositoryMock As Mock(Of ICustomerRepository)
        Private _materialRepositoryMock As Mock(Of IMaterialRepository)
        Private _eventPublisherMock As Mock(Of IDomainEventPublisher)
        
        <SetUp>
        Public Sub Setup()
            _orderRepositoryMock = New Mock(Of IOrderRepository)
            _customerRepositoryMock = New Mock(Of ICustomerRepository)
            _materialRepositoryMock = New Mock(Of IMaterialRepository)
            _eventPublisherMock = New Mock(Of IDomainEventPublisher)
            
            _handler = New CreateOrderHandler(
                _orderRepositoryMock.Object,
                _customerRepositoryMock.Object,
                _materialRepositoryMock.Object,
                _eventPublisherMock.Object)
        End Sub
        
        <Test>
        Public Async Function Handle_ValidOrder_CreatesSuccessfully()
            ' Arrange
            Dim customerId = 1
            Dim materialId = 1
            Dim customer = New Customer("John Doe", New CustomerEmail("john@example.com"), 
                                        New Money(10000, "USD"))
            Dim material = New Material("Widget", New Money(100, "USD"), New Quantity(50))
            
            _customerRepositoryMock.Setup(x => x.GetById(It.IsAny(Of CustomerId)))
                .ReturnsAsync(customer)
            
            _materialRepositoryMock.Setup(x => x.GetById(It.IsAny(Of MaterialId)))
                .ReturnsAsync(material)
            
            _orderRepositoryMock.Setup(x => x.Create(It.IsAny(Of Order)))
                .ReturnsAsync(New OrderId(1))
            
            Dim command = New CreateOrderCommand With {
                .CustomerId = customerId,
                .Items = New List(Of CreateOrderItemDto) From {
                    New CreateOrderItemDto With {
                        .MaterialId = materialId,
                        .Quantity = 5,
                        .UnitPrice = 100
                    }
                },
                .RequestedAt = DateTime.UtcNow
            }
            
            ' Act
            Dim response = Await _handler.Handle(command)
            
            ' Assert
            Assert.That(response.OrderId, [Is].EqualTo(1))
            Assert.That(response.Total, [Is].EqualTo(500))
            
            ' Verify repository was called
            _orderRepositoryMock.Verify(x => x.Create(It.IsAny(Of Order)), Times.Once)
            
            ' Verify events were published
            _eventPublisherMock.Verify(x => x.Publish(It.IsAny(Of DomainEvent)), 
                                       Times.AtLeastOnce)
        End Function
        
        <Test>
        Public Async Function Handle_OrderExceedsCreditLimit_Throws()
            ' Arrange
            Dim customerId = 1
            Dim materialId = 1
            Dim customer = New Customer("John Doe", New CustomerEmail("john@example.com"), 
                                        New Money(300, "USD"))  ' Low credit limit
            Dim material = New Material("Widget", New Money(100, "USD"), New Quantity(50))
            
            _customerRepositoryMock.Setup(x => x.GetById(It.IsAny(Of CustomerId)))
                .ReturnsAsync(customer)
            
            _materialRepositoryMock.Setup(x => x.GetById(It.IsAny(Of MaterialId)))
                .ReturnsAsync(material)
            
            Dim command = New CreateOrderCommand With {
                .CustomerId = customerId,
                .Items = New List(Of CreateOrderItemDto) From {
                    New CreateOrderItemDto With {
                        .MaterialId = materialId,
                        .Quantity = 5,      ' Total: 5 * 100 = 500
                        .UnitPrice = 100    ' Exceeds 300 limit
                    }
                }
            }
            
            ' Act & Assert
            Assert.ThrowsAsync(Of InvalidOperationException)(
                Async Function()
                    Await _handler.Handle(command)
                End Function)
        End Function
    End Class
End Namespace
```

---

## Feature 2: BookOrder

Book an order (transition from Draft → Pending).

### Complete Implementation

**File:** `Features/Orders/BookOrder/BookOrderCommand.cs`

```csharp
Imports OrderSystem.Core.Mediator

namespace OrderSystem.Features.Orders.BookOrder
    Public Class BookOrderCommand
        Implements IRequest(Of BookOrderResponse)
        
        Public Property OrderId As Integer
    End Class
    
    Public Class BookOrderResponse
        Public Property OrderId As Integer
        Public Property Status As String
        Public Property BookedAt As DateTime
    End Class
End Namespace
```

**File:** `Features/Orders/BookOrder/BookOrderValidator.cs`

```csharp
Imports OrderSystem.Core.Mediator
Imports OrderSystem.Infrastructure.Repositories

namespace OrderSystem.Features.Orders.BookOrder
    Public Class BookOrderValidator
        Implements IValidator(Of BookOrderCommand)
        
        Private _orderRepository As IOrderRepository
        
        Public Sub New(orderRepository As IOrderRepository)
            _orderRepository = orderRepository
        End Sub
        
        Public Async Function Validate(command As BookOrderCommand) As Task(Of ValidationResult)
            Dim result = New ValidationResult()
            
            If command.OrderId <= 0 Then
                result.AddError("Order ID must be positive")
            End If
            
            ' Check order exists
            Dim order = Await _orderRepository.GetById(New OrderId(command.OrderId))
            If order Is Nothing Then
                result.AddError("Order not found")
            End If
            
            Return result
        End Function
    End Class
End Namespace
```

**File:** `Features/Orders/BookOrder/BookOrderHandler.cs`

```csharp
Imports OrderSystem.Core.Mediator
Imports OrderSystem.Domain.Orders
Imports OrderSystem.Domain.Shared
Imports OrderSystem.Infrastructure.Repositories

namespace OrderSystem.Features.Orders.BookOrder
    Public Class BookOrderHandler
        Implements IRequestHandler(Of BookOrderCommand, BookOrderResponse)
        
        Private _orderRepository As IOrderRepository
        Private _eventPublisher As IDomainEventPublisher
        
        Public Sub New(
            orderRepository As IOrderRepository,
            eventPublisher As IDomainEventPublisher)
            _orderRepository = orderRepository
            _eventPublisher = eventPublisher
        End Sub
        
        Public Async Function Handle(command As BookOrderCommand) As Task(Of BookOrderResponse) _
            Implements IRequestHandler(Of BookOrderCommand, BookOrderResponse).Handle
            
            ' Load order
            Dim order = Await _orderRepository.GetById(New OrderId(command.OrderId))
            If order Is Nothing Then
                Throw New InvalidOperationException("Order not found")
            End If
            
            ' Apply business logic
            ' Domain entity enforces: can only book Draft orders with items
            order.Book()
            
            ' Persist changes
            Await _orderRepository.Update(order)
            
            ' Publish events
            For Each [event] In order.DomainEvents
                Await _eventPublisher.Publish([event])
            Next
            order.ClearDomainEvents()
            
            ' Return response
            Return New BookOrderResponse With {
                .OrderId = order.Id.Value,
                .Status = order.Status.ToString(),
                .BookedAt = DateTime.UtcNow
            }
        End Function
    End Class
End Namespace
```

---

## Feature 3: CancelOrder

Cancel an order (Draft or Pending only).

**File:** `Features/Orders/CancelOrder/CancelOrderCommand.cs`

```csharp
Imports OrderSystem.Core.Mediator

namespace OrderSystem.Features.Orders.CancelOrder
    Public Class CancelOrderCommand
        Implements IRequest(Of CancelOrderResponse)
        
        Public Property OrderId As Integer
        Public Property Reason As String
    End Class
    
    Public Class CancelOrderResponse
        Public Property OrderId As Integer
        Public Property Status As String
        Public Property CancelledAt As DateTime
    End Class
End Namespace
```

**File:** `Features/Orders/CancelOrder/CancelOrderHandler.cs`

```csharp
Imports OrderSystem.Core.Mediator
Imports OrderSystem.Domain.Orders
Imports OrderSystem.Domain.Shared
Imports OrderSystem.Infrastructure.Repositories

namespace OrderSystem.Features.Orders.CancelOrder
    Public Class CancelOrderHandler
        Implements IRequestHandler(Of CancelOrderCommand, CancelOrderResponse)
        
        Private _orderRepository As IOrderRepository
        
        Public Sub New(orderRepository As IOrderRepository)
            _orderRepository = orderRepository
        End Sub
        
        Public Async Function Handle(command As CancelOrderCommand) As Task(Of CancelOrderResponse) _
            Implements IRequestHandler(Of CancelOrderCommand, CancelOrderResponse).Handle
            
            Dim order = Await _orderRepository.GetById(New OrderId(command.OrderId))
            If order Is Nothing Then
                Throw New InvalidOperationException("Order not found")
            End If
            
            ' Domain entity enforces: can't cancel Approved orders
            order.Cancel(command.Reason)
            
            Await _orderRepository.Update(order)
            
            Return New CancelOrderResponse With {
                .OrderId = order.Id.Value,
                .Status = order.Status.ToString(),
                .CancelledAt = DateTime.UtcNow
            }
        End Function
    End Class
End Namespace
```

---

## Feature 4: GetOrders (Query)

Retrieve orders for a customer.

**File:** `Features/Orders/GetOrders/GetOrdersQuery.cs`

```csharp
Imports OrderSystem.Core.Mediator

namespace OrderSystem.Features.Orders.GetOrders
    ''' Query: Get orders for a customer
    ''' Queries are simple data retrieval, no business logic
    Public Class GetOrdersQuery
        Implements IRequest(Of GetOrdersResponse)
        
        Public Property CustomerId As Integer
    End Class
    
    Public Class GetOrdersResponse
        Public Property Orders As List(Of OrderSummary)
    End Class
    
    Public Class OrderSummary
        Public Property OrderId As Integer
        Public Property CustomerName As String
        Public Property Status As String
        Public Property Total As Decimal
        Public Property CreatedAt As DateTime
        Public Property ItemCount As Integer
    End Class
End Namespace
```

**File:** `Features/Orders/GetOrders/GetOrdersHandler.cs`

```csharp
Imports OrderSystem.Core.Mediator
Imports OrderSystem.Infrastructure.Data.Orders

namespace OrderSystem.Features.Orders.GetOrders
    ''' Handles GetOrders query
    ''' This is the READ side of CQRS
    ''' No domain entities loaded, just query the read model
    Public Class GetOrdersHandler
        Implements IRequestHandler(Of GetOrdersQuery, GetOrdersResponse)
        
        Private _queryService As OrderQueryService
        
        Public Sub New(queryService As OrderQueryService)
            _queryService = queryService
        End Sub
        
        Public Async Function Handle(query As GetOrdersQuery) As Task(Of GetOrdersResponse) _
            Implements IRequestHandler(Of GetOrdersQuery, GetOrdersResponse).Handle
            
            ' Query the read model (optimized for queries)
            Dim orderReadModels = Await _queryService.SearchOrdersByCustomer(query.CustomerId)
            
            Dim summaries = orderReadModels.Select(Function(o) New OrderSummary With {
                .OrderId = o.OrderId,
                .CustomerName = o.CustomerName,
                .Status = o.Status,
                .Total = o.TotalAmount,
                .CreatedAt = o.CreatedAt,
                .ItemCount = o.LineItemCount
            }).ToList()
            
            Return New GetOrdersResponse With {
                .Orders = summaries
            }
        End Function
    End Class
End Namespace
```

---

## Wire Everything Up: Dependency Injection

**File:** `Program.cs`

```csharp
Imports Microsoft.AspNetCore.Builder
Imports Microsoft.Extensions.DependencyInjection
Imports OrderSystem.Core.Mediator
Imports OrderSystem.Infrastructure.Repositories
Imports OrderSystem.Infrastructure.Data.Orders
Imports OrderSystem.Features.Orders.CreateOrder
Imports OrderSystem.Features.Orders.BookOrder
Imports OrderSystem.Features.Orders.CancelOrder
Imports OrderSystem.Features.Orders.GetOrders

Module Program
    Sub Main()
        Dim builder = WebApplication.CreateBuilder(args)
        
        ' Database connection string
        Dim connectionString = builder.Configuration.GetConnectionString("OrdersDb")
        
        ' Register repositories
        builder.Services.AddScoped(Function(sp) New OrderRepository(connectionString))
        builder.Services.AddScoped(Function(sp) CType(sp.GetService(Of OrderRepository), IOrderRepository))
        
        builder.Services.AddScoped(Function(sp) New CustomerRepository(connectionString))
        builder.Services.AddScoped(Function(sp) CType(sp.GetService(Of CustomerRepository), ICustomerRepository))
        
        builder.Services.AddScoped(Function(sp) New MaterialRepository(connectionString))
        builder.Services.AddScoped(Function(sp) CType(sp.GetService(Of MaterialRepository), IMaterialRepository))
        
        ' Register query services
        builder.Services.AddScoped(Function(sp) New OrderQueryService(connectionString))
        
        ' Register domain event publisher
        builder.Services.AddScoped(Of IDomainEventPublisher, DomainEventPublisher)
        
        ' Build mediator
        Dim mediator = New Mediator()
        
        ' Register CreateOrder
        mediator.Register(
            Of CreateOrderCommand, CreateOrderResponse)(
            New CreateOrderHandler(
                New OrderRepository(connectionString),
                New CustomerRepository(connectionString),
                New MaterialRepository(connectionString),
                New DomainEventPublisher()))
        mediator.RegisterValidator(
            Of CreateOrderCommand)(
            New CreateOrderValidator(
                New CustomerRepository(connectionString),
                New MaterialRepository(connectionString)))
        
        ' Register BookOrder
        mediator.Register(
            Of BookOrderCommand, BookOrderResponse)(
            New BookOrderHandler(
                New OrderRepository(connectionString),
                New DomainEventPublisher()))
        mediator.RegisterValidator(
            Of BookOrderCommand)(
            New BookOrderValidator(New OrderRepository(connectionString)))
        
        ' Register CancelOrder
        mediator.Register(
            Of CancelOrderCommand, CancelOrderResponse)(
            New CancelOrderHandler(New OrderRepository(connectionString)))
        
        ' Register GetOrders
        mediator.Register(
            Of GetOrdersQuery, GetOrdersResponse)(
            New GetOrdersHandler(New OrderQueryService(connectionString)))
        
        builder.Services.AddSingleton(mediator)
        
        ' Add controllers
        builder.Services.AddControllers()
        
        Dim app = builder.Build()
        app.UseRouting()
        app.MapControllers()
        
        app.Run()
    End Sub
End Module
```

---

## End-to-End Test: Full Workflow

**File:** `Tests/Integration/OrderWorkflowTests.cs`

```csharp
Imports NUnit.Framework
Imports OrderSystem.Core.Mediator
Imports OrderSystem.Features.Orders.CreateOrder
Imports OrderSystem.Features.Orders.BookOrder
Imports OrderSystem.Features.Orders.CancelOrder
Imports OrderSystem.Features.Orders.GetOrders

Namespace OrderSystem.Tests.Integration
    <TestFixture>
    Public Class OrderWorkflowTests
        
        Private _mediator As Mediator
        Private _connectionString As String
        
        <SetUp>
        Public Async Function Setup() As Task
            _connectionString = "Server=(local);Database=OrderSystemTest;Integrated Security=true;"
            _mediator = New Mediator()
            
            ' Setup mediator with real repositories and services
            ' (see Program.cs for full setup)
            
            ' Clear test data
            Using connection = New SqlConnection(_connectionString)
                Await connection.OpenAsync()
                Await connection.ExecuteAsync("DELETE FROM OrderLineItems")
                Await connection.ExecuteAsync("DELETE FROM DomainEvents")
                Await connection.ExecuteAsync("DELETE FROM Orders")
                Await connection.ExecuteAsync("DELETE FROM Customers")
                Await connection.ExecuteAsync("DELETE FROM Materials")
                
                ' Insert test data
                Await connection.ExecuteAsync(
                    "INSERT INTO Customers (Name, Email, CreditLimit, IsActive) " &
                    "VALUES (@Name, @Email, @CreditLimit, @IsActive)",
                    New With {
                        .Name = "Test Customer",
                        .Email = "test@example.com",
                        .CreditLimit = 10000D,
                        .IsActive = 1
                    })
                
                Await connection.ExecuteAsync(
                    "INSERT INTO Materials (Name, UnitPrice, StockQuantity) " &
                    "VALUES (@Name, @UnitPrice, @StockQuantity)",
                    New With {
                        .Name = "Test Widget",
                        .UnitPrice = 100D,
                        .StockQuantity = 50D
                    })
                
                Await connection.CloseAsync()
            End Using
        End Sub
        
        <Test>
        Public Async Function Workflow_CreateBookAndCancelOrder_Succeeds()
            ' Step 1: Create order
            Dim createCommand = New CreateOrderCommand With {
                .CustomerId = 1,
                .Items = New List(Of CreateOrderItemDto) From {
                    New CreateOrderItemDto With {
                        .MaterialId = 1,
                        .Quantity = 5,
                        .UnitPrice = 100
                    }
                }
            }
            
            Dim createResponse = Await _mediator.Send(createCommand)
            Assert.That(createResponse.OrderId, [Is].GreaterThan(0))
            Assert.That(createResponse.Status, [Is].EqualTo("Draft"))
            Assert.That(createResponse.Total, [Is].EqualTo(500))
            
            ' Step 2: Book the order
            Dim bookCommand = New BookOrderCommand With {
                .OrderId = createResponse.OrderId
            }
            
            Dim bookResponse = Await _mediator.Send(bookCommand)
            Assert.That(bookResponse.Status, [Is].EqualTo("Pending"))
            
            ' Step 3: Cancel the order
            Dim cancelCommand = New CancelOrderCommand With {
                .OrderId = createResponse.OrderId,
                .Reason = "Customer requested cancellation"
            }
            
            Dim cancelResponse = Await _mediator.Send(cancelCommand)
            Assert.That(cancelResponse.Status, [Is].EqualTo("Cancelled"))
            
            ' Step 4: Verify via query
            Dim getOrdersQuery = New GetOrdersQuery With {
                .CustomerId = createCommand.CustomerId
            }
            
            Dim queryResponse = Await _mediator.Send(getOrdersQuery)
            Assert.That(queryResponse.Orders.Count, [Is].EqualTo(1))
            Assert.That(queryResponse.Orders(0).Status, [Is].EqualTo("Cancelled"))
        End Function
    End Class
End Namespace
```

---

## Key Learnings from Phase 4

### The Complete Picture

```
HTTP Request
    ↓
Controller (HTTP concerns)
    ↓
Mediator (dispatch + cross-cutting)
    ↓
Validator (input validation)
    ↓
Handler (orchestration)
    ↓
Domain Entity (business rules)
    ↓
Repository (persistence)
    ↓
Database (data storage)
    ↓
HTTP Response
```

### Separation of Concerns

| Layer | Responsibility | Example |
|-------|---|---|
| **Controller** | Parse HTTP, return responses | Parse JSON, return 200/400/500 |
| **Validator** | Input validation, existence checks | "Email format valid?", "Customer exists?" |
| **Handler** | Orchestration | "Load entity, call methods, save" |
| **Domain Entity** | Business rules | "Can only book non-empty orders" |
| **Repository** | Persistence | "Save to database atomically" |
| **Database** | Data integrity | "Foreign keys, constraints, transactions" |

### Why This Matters

**Maintainability:**
- To understand CreateOrder: read `CreateOrderHandler`
- All related code is in one folder
- No hunting through multiple layers

**Testability:**
- Test validators without handlers
- Test handlers without database (mock repositories)
- Test domain logic without any infrastructure
- Test integration with real database

**Flexibility:**
- Switch database from SQL Server to PostgreSQL? Change repository only.
- Add email notification? Add event handler.
- Add authorization? Add mediator middleware.

---

## Next Steps

When you've completed Phase 4:

1. ✅ Complete CreateOrder feature
2. ✅ Complete BookOrder feature
3. ✅ Complete CancelOrder feature
4. ✅ Complete GetOrders query
5. ✅ End-to-end testing
6. ✅ Dependency injection setup

You'll move to **Phase 5: CQRS Formalization & Advanced Features**.

