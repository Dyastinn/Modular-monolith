# Phase 1: Building a Simple Mediator

## Objective
Implement the simplest possible mediator to understand what problem it solves.

You'll implement this from scratch. No external libraries.

**Time Investment:** 2–3 hours  
**Deliverable:** Working mediator with 2 test features

---

## Before You Start: What is a Mediator?

A mediator is a **central dispatch system** that:

1. Takes a request (a command or query)
2. Finds the right handler for that request
3. Runs any cross-cutting logic (validation, logging, error handling)
4. Returns the response

That's it. Everything else is details.

---

## The Problem a Mediator Solves (Before vs. After)

### Without a Mediator (Code Duplication)

```csharp
' Feature 1: CreateOrder
Public Class CreateOrderController
    Inherits ControllerBase
    
    <HttpPost("/orders")>
    Public Function CreateOrder(request As CreateOrderRequest) As IActionResult
        Try
            ' Validation - repeated everywhere
            If String.IsNullOrEmpty(request.CustomerId) Then
                Return BadRequest("Customer ID required")
            End If
            If request.Items.Count = 0 Then
                Return BadRequest("Order must contain items")
            End If
            
            ' Handler
            Dim handler = New CreateOrderHandler()
            Dim orderId = handler.Handle(request)
            
            ' Success response
            Return Ok(New With {.OrderId = orderId})
        Catch ex As Exception
            ' Error handling - repeated everywhere
            Console.WriteLine(ex.Message)
            Return StatusCode(500, "Order creation failed")
        End Try
    End Function
End Class

' Feature 2: BookOrder
Public Class BookOrderController
    Inherits ControllerBase
    
    <HttpPost("/orders/{orderId}/book")>
    Public Function BookOrder(orderId As String, request As BookOrderRequest) As IActionResult
        Try
            ' SAME validation pattern repeated
            If String.IsNullOrEmpty(orderId) Then
                Return BadRequest("Order ID required")
            End If
            If request.ShippingAddress Is Nothing Then
                Return BadRequest("Shipping address required")
            End If
            
            ' SAME handler pattern repeated
            Dim handler = New BookOrderHandler()
            Dim bookingId = handler.Handle(request)
            
            ' SAME error handling repeated
            Return Ok(New With {.BookingId = bookingId})
        Catch ex As Exception
            Console.WriteLine(ex.Message)
            Return StatusCode(500, "Booking failed")
        End Try
    End Function
End Class

' Feature 3, 4, 5, ... same code structure repeated 50 times
```

**The Problem:** The same try-catch-validate-handle-return pattern appears in every controller. Adding a new cross-cutting concern (logging, authorization, metrics) requires changes in 50 places.

### With a Mediator (DRY - Don't Repeat Yourself)

```csharp
' One dispatcher for everything
Public Class OrdersController
    Inherits ControllerBase
    
    Private _mediator As Mediator
    
    <HttpPost("/orders")>
    Public Function CreateOrder(request As CreateOrderRequest) As IActionResult
        Dim response = _mediator.Send(New CreateOrderCommand With {
            .CustomerId = request.CustomerId,
            .Items = request.Items
        })
        Return Ok(response)
    End Function
    
    <HttpPost("/orders/{orderId}/book")>
    Public Function BookOrder(orderId As String, request As BookOrderRequest) As IActionResult
        Dim response = _mediator.Send(New BookOrderCommand With {
            .OrderId = orderId,
            .ShippingAddress = request.ShippingAddress
        })
        Return Ok(response)
    End Function
End Class

' Add logging to ALL features at once:
' Just add middleware to the mediator. Done. No controller changes.
```

---

## Phase 1 Implementation: Step by Step

### Step 1: Define the Request/Response Contracts

These interfaces describe what a mediator request looks like.

**File:** `Core/Mediator/IRequest.cs`

```csharp
namespace OrderSystem.Core.Mediator
{
    /// A request that produces a response of type TResponse
    Public Interface IRequest(Of TResponse)
    End Interface
    
    /// A request with no response (fire and forget)
    Public Interface IRequest
    End Interface
End Namespace
```

**Why this exists:**
- Defines a contract. Any handler must handle something that implements this interface.
- Allows the mediator to discover handlers at runtime.
- Separates the request from the handler (handler doesn't know who called it).

### Step 2: Define the Handler Interface

**File:** `Core/Mediator/IRequestHandler.cs`

```csharp
namespace OrderSystem.Core.Mediator
{
    /// Handles a request and produces a response
    Public Interface IRequestHandler(Of TRequest As IRequest(Of TResponse), TResponse)
        Function Handle(request As TRequest) As TResponse
    End Interface
    
    /// Handles a request with no response
    Public Interface IRequestHandler(Of TRequest As IRequest)
        Sub Handle(request As TRequest)
    End Interface
End Namespace
```

**Why this exists:**
- Defines what a handler looks like.
- The mediator uses this to know which method to call.
- Consistent interface across all handlers.

### Step 3: Build the Mediator

**File:** `Core/Mediator/Mediator.cs`

```csharp
namespace OrderSystem.Core.Mediator
{
    Public Class Mediator
        ' Dictionary that maps request types to their handlers
        Private _handlers As New Dictionary(Of Type, Object)
        
        ' Dictionary that maps request types to their validators
        Private _validators As New Dictionary(Of Type, List(Of Object))
        
        ''' Register a handler
        Public Sub Register(Of TRequest As IRequest(Of TResponse), TResponse)(
            handler As IRequestHandler(Of TRequest, TResponse))
            
            _handlers(GetType(TRequest)) = handler
        End Sub
        
        ''' Register a validator for a request type
        Public Sub RegisterValidator(Of TRequest)(
            validator As IValidator(Of TRequest))
            
            Dim requestType = GetType(TRequest)
            If Not _validators.ContainsKey(requestType) Then
                _validators(requestType) = New List(Of Object)
            End If
            _validators(requestType).Add(validator)
        End Sub
        
        ''' Send a request and get a response
        Public Function Send(Of TResponse)(request As IRequest(Of TResponse)) As TResponse
            Dim requestType = request.GetType()
            
            ' Step 1: Run all validators
            If _validators.ContainsKey(requestType) Then
                For Each validator In _validators(requestType)
                    ' Cast to IValidator(Of request type)
                    Dim validatorType = GetType(IValidator(Of )).MakeGenericType(requestType)
                    Dim validateMethod = validatorType.GetMethod("Validate")
                    Dim result = validateMethod.Invoke(validator, New Object() { request })
                    
                    ' If validation failed, throw
                    Dim resultType = result.GetType()
                    Dim isValidProperty = resultType.GetProperty("IsValid")
                    If Not CBool(isValidProperty.GetValue(result)) Then
                        Dim errorsProperty = resultType.GetProperty("Errors")
                        Dim errors = errorsProperty.GetValue(result)
                        Throw New ValidationException(errors.ToString())
                    End If
                End If
            End If
            
            ' Step 2: Find the handler
            If Not _handlers.ContainsKey(requestType) Then
                Throw New InvalidOperationException($"No handler registered for {requestType.Name}")
            End If
            
            Dim handler = _handlers(requestType)
            
            ' Step 3: Call the handler
            Dim handlerType = handler.GetType()
            Dim handleMethod = handlerType.GetMethod("Handle")
            Return CType(handleMethod.Invoke(handler, New Object() { request }), TResponse)
        End Function
    End Class
End Namespace
```

**What's happening here:**

1. **Registration:** When the application starts, you tell the mediator about handlers.
2. **Validation:** Before running a handler, run all registered validators for that request.
3. **Handler Lookup:** Find the handler by request type.
4. **Invocation:** Call the handler using reflection.

**Why reflection?** Because `IRequestHandler(Of CreateOrderCommand, OrderId)` is a different type than `IRequestHandler(Of BookOrderCommand, BookingId)`. Reflection lets us store and invoke them generically.

### Step 4: Add Validators

**File:** `Core/Mediator/IValidator.cs`

```csharp
namespace OrderSystem.Core.Mediator
{
    Public Interface IValidator(Of TRequest)
        Function Validate(request As TRequest) As ValidationResult
    End Interface
    
    Public Class ValidationResult
        Public Property IsValid As Boolean
        Public Property Errors As List(Of String)
        
        Sub New()
            Errors = New List(Of String)
            IsValid = True
        End Sub
        
        Public Sub AddError(message As String)
            Errors.Add(message)
            IsValid = False
        End Sub
    End Class
    
    Public Class ValidationException
        Inherits Exception
        
        Public Sub New(message As String)
            MyBase.New(message)
        End Sub
    End Class
End Namespace
```

---

## Create Your First Feature: CreateOrder

### Step 5: Define the Command

**File:** `Features/Orders/CreateOrder/CreateOrderCommand.cs`

```csharp
namespace OrderSystem.Features.Orders.CreateOrder
{
    ''' Represents a request to create an order
    Public Class CreateOrderCommand
        Implements IRequest(Of OrderId)
        
        Public Property CustomerId As Integer
        Public Property Items As List(Of OrderItem)
        Public Property OrderDate As DateTime
    End Class
    
    ''' Represents an order item
    Public Class OrderItem
        Public Property MaterialId As Integer
        Public Property Quantity As Decimal
        Public Property UnitPrice As Decimal
    End Class
    
    ''' The response: the ID of the created order
    Public Class OrderId
        Public Property Value As Integer
        
        Sub New(value As Integer)
            Me.Value = value
        End Sub
    End Class
End Namespace
```

**Why this structure:**
- `CreateOrderCommand` is the **request**. It says "I want to create an order with these details."
- `OrderId` is the **response**. It says "Here's the ID of your new order."
- The command implements `IRequest(Of OrderId)`, which tells the mediator "this command returns an OrderId."

### Step 6: Add Validation

**File:** `Features/Orders/CreateOrder/CreateOrderValidator.cs`

```csharp
namespace OrderSystem.Features.Orders.CreateOrder
{
    Public Class CreateOrderValidator
        Implements IValidator(Of CreateOrderCommand)
        
        Public Function Validate(command As CreateOrderCommand) As ValidationResult
            Dim result = New ValidationResult()
            
            If command.CustomerId <= 0 Then
                result.AddError("Customer ID must be greater than 0")
            End If
            
            If command.Items Is Nothing OrElse command.Items.Count = 0 Then
                result.AddError("Order must contain at least one item")
            End If
            
            For Each item In command.Items
                If item.Quantity <= 0 Then
                    result.AddError($"Item quantity must be greater than 0")
                End If
                If item.UnitPrice < 0 Then
                    result.AddError("Item price cannot be negative")
                End If
            Next
            
            Return result
        End Function
    End Class
End Namespace
```

**Why this exists:**
- Validation is NOT mixed with business logic.
- Validation is NOT in the database layer.
- Validation is NOT in the controller.
- Validation lives in one place, declared once, reusable everywhere.

### Step 7: Create the Handler

**File:** `Features/Orders/CreateOrder/CreateOrderHandler.cs`

```csharp
namespace OrderSystem.Features.Orders.CreateOrder
{
    Public Class CreateOrderHandler
        Implements IRequestHandler(Of CreateOrderCommand, OrderId)
        
        Public Function Handle(command As CreateOrderCommand) As OrderId _
            Implements IRequestHandler(Of CreateOrderCommand, OrderId).Handle
            
            ' This is where the ACTUAL BUSINESS LOGIC lives
            ' No validation (that's done by validators)
            ' No HTTP concerns (that's the controller's job)
            ' Just pure order creation logic
            
            Dim order = New Order(
                customerId:= command.CustomerId,
                orderDate:= command.OrderDate
            )
            
            ' Add items to the order
            For Each item In command.Items
                order.AddItem(
                    materialId:= item.MaterialId,
                    quantity:= item.Quantity,
                    unitPrice:= item.UnitPrice
                )
            Next
            
            ' Save to database (we'll do this in Phase 3)
            ' For now, just return a fake ID
            Dim newOrderId = 1
            
            Return New OrderId(newOrderId)
        End Function
    End Class
End Namespace
```

### Step 8: Create a Domain Entity

**File:** `Domain/Orders/Order.cs`

```csharp
namespace OrderSystem.Domain.Orders
{
    ''' Domain entity: represents a business Order
    ''' This is NOT a database model. This is our BUSINESS MODEL.
    Public Class Order
        Public Property Id As Integer
        Public Property CustomerId As Integer
        Public Property OrderDate As DateTime
        Public Property Items As List(Of OrderLineItem)
        Public Property Status As OrderStatus
        
        Sub New(customerId As Integer, orderDate As DateTime)
            Me.CustomerId = customerId
            Me.OrderDate = orderDate
            Me.Items = New List(Of OrderLineItem)
            Me.Status = OrderStatus.Draft
        End Sub
        
        ''' Add an item to this order
        Public Sub AddItem(materialId As Integer, quantity As Decimal, unitPrice As Decimal)
            Dim lineItem = New OrderLineItem With {
                .MaterialId = materialId,
                .Quantity = quantity,
                .UnitPrice = unitPrice
            }
            Items.Add(lineItem)
        End Sub
        
        ''' Calculate total price (business logic)
        Public Function CalculateTotal() As Decimal
            Return Items.Sum(Function(item) item.Quantity * item.UnitPrice)
        End Function
    End Class
    
    ''' Value object representing a line item
    Public Class OrderLineItem
        Public Property MaterialId As Integer
        Public Property Quantity As Decimal
        Public Property UnitPrice As Decimal
    End Class
    
    ''' Domain concept: order status
    Public Enum OrderStatus
        Draft = 0
        Pending = 1
        Approved = 2
        Cancelled = 3
    End Enum
End Namespace
```

**Important distinctions:**
- `Order` is a **domain entity**. It represents the business concept of an order.
- `CreateOrderCommand` is an **application request**. It's the DTO that comes from the API.
- These are **separate**. The command may have different data than the entity needs.

---

## Wire It All Together: Dependency Injection

**File:** `Program.cs`

```csharp
Imports Microsoft.AspNetCore.Builder
Imports Microsoft.Extensions.DependencyInjection
Imports OrderSystem.Core.Mediator
Imports OrderSystem.Features.Orders.CreateOrder

Module Program
    Sub Main()
        Dim builder = WebApplication.CreateBuilder(args)
        
        ' Register services
        builder.Services.AddControllers()
        
        ' Register the mediator
        Dim mediator = New Mediator()
        
        ' Register handlers
        mediator.Register(Of CreateOrderCommand, OrderId)(New CreateOrderHandler())
        
        ' Register validators
        mediator.RegisterValidator(Of CreateOrderCommand)(New CreateOrderValidator())
        
        builder.Services.AddSingleton(mediator)
        
        Dim app = builder.Build()
        app.UseRouting()
        app.MapControllers()
        
        app.Run()
    End Sub
End Module
```

---

## Create a Controller

**File:** `Controllers/OrdersController.cs`

```csharp
Imports Microsoft.AspNetCore.Mvc
Imports OrderSystem.Core.Mediator
Imports OrderSystem.Features.Orders.CreateOrder

Namespace OrderSystem.Controllers
    <ApiController>
    <Route("api/orders")>
    Public Class OrdersController
        Inherits ControllerBase
        
        Private _mediator As Mediator
        
        Public Sub New(mediator As Mediator)
            _mediator = mediator
        End Sub
        
        <HttpPost>
        Public Function CreateOrder(request As CreateOrderRequest) As IActionResult
            Try
                ' Create the command
                Dim command = New CreateOrderCommand With {
                    .CustomerId = request.CustomerId,
                    .Items = request.Items.Select(Function(i) New OrderItem With {
                        .MaterialId = i.MaterialId,
                        .Quantity = i.Quantity,
                        .UnitPrice = i.UnitPrice
                    }).ToList(),
                    .OrderDate = DateTime.Now
                }
                
                ' Send through mediator
                ' Validation happens automatically
                ' Handler execution happens automatically
                Dim orderId = _mediator.Send(command)
                
                Return Ok(New With {.OrderId = orderId.Value})
            Catch ex As ValidationException
                Return BadRequest(New With {.Error = ex.Message})
            Catch ex As Exception
                Return StatusCode(500, New With {.Error = ex.Message})
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

---

## Create Your Second Feature: BookOrder

Do this yourself. Follow the same pattern:

1. **Command** (`BookOrderCommand`)
2. **Validator** (`BookOrderValidator`)
3. **Handler** (`BookOrderHandler`)
4. **Register** in `Program.cs`
5. **Controller method** that sends the command

This reinforces that the pattern is reusable and consistent.

---

## Unit Tests (No Database Required)

**File:** `Tests/Features/Orders/CreateOrder/CreateOrderValidatorTests.cs`

```csharp
Imports NUnit.Framework
Imports OrderSystem.Features.Orders.CreateOrder

Namespace OrderSystem.Tests.Features.Orders.CreateOrder
    <TestFixture>
    Public Class CreateOrderValidatorTests
        
        Private _validator As CreateOrderValidator
        
        <SetUp>
        Public Sub Setup()
            _validator = New CreateOrderValidator()
        End Sub
        
        <Test>
        Public Sub Validate_WithValidCommand_ReturnsSuccess()
            Dim command = New CreateOrderCommand With {
                .CustomerId = 1,
                .Items = New List(Of OrderItem) From {
                    New OrderItem With {
                        .MaterialId = 1,
                        .Quantity = 10,
                        .UnitPrice = 50
                    }
                }
            }
            
            Dim result = _validator.Validate(command)
            
            Assert.That(result.IsValid, [Is].True)
            Assert.That(result.Errors, [Is].Empty)
        End Sub
        
        <Test>
        Public Sub Validate_WithInvalidCustomerId_ReturnsFail()
            Dim command = New CreateOrderCommand With {
                .CustomerId = -1,
                .Items = New List(Of OrderItem)()
            }
            
            Dim result = _validator.Validate(command)
            
            Assert.That(result.IsValid, [Is].False)
            Assert.That(result.Errors.Count, [Is].GreaterThan(0))
        End Sub
        
        <Test>
        Public Sub Validate_WithNoItems_ReturnsFail()
            Dim command = New CreateOrderCommand With {
                .CustomerId = 1,
                .Items = New List(Of OrderItem)()
            }
            
            Dim result = _validator.Validate(command)
            
            Assert.That(result.IsValid, [Is].False)
        End Sub
    End Class
End Namespace
```

**Notice:** No database. No mocks. No elaborate setup. Just pure logic testing.

---

## Key Insights from Phase 1

### What the Mediator Solved

**Before:**
```csharp
CreateOrderController → validate → handle
BookOrderController → validate → handle
CancelOrderController → validate → handle
' ... repeated 50 times
```

**After:**
```csharp
CreateOrderController → mediator.Send(command)
BookOrderController → mediator.Send(command)
CancelOrderController → mediator.Send(command)
' ... consistent, no repetition
```

### What We Now Have

1. **Clear separation:**
   - Commands (requests) in one place
   - Handlers (business logic) in another
   - Validators (rules) in another
   - Controllers (HTTP) in another

2. **Easy to test:**
   - Test handlers without a database
   - Test validators without handlers
   - Test mediator dispatch in isolation

3. **Easy to add cross-cutting concerns:**
   - Want to add logging? Modify the mediator once.
   - Want to add authorization? Modify the mediator once.
   - Want to add performance metrics? Modify the mediator once.

### What We Haven't Done Yet

- Database integration
- Domain logic (entities, value objects)
- CQRS separation (commands vs. queries)
- Complex business rules
- Error handling strategies
- Transaction management

---

## Next Steps

When you've implemented Phase 1:

1. ✅ Mediator core
2. ✅ CreateOrder feature (command, handler, validator)
3. ✅ BookOrder feature (repeat the pattern)
4. ✅ Unit tests

Then move to **Phase 2: Domain Entities & Value Objects**.

---

## Questions to Ask Yourself

- **Why register handlers?** What would happen if you didn't?
- **Why separate commands from handlers?** What if you mixed them?
- **Why have validators?** What if validation lived in the handler?
- **Why use reflection?** Could you do this without it?

The answers to these questions are why the architecture exists.

