# Phase 0: Foundation – Understanding the Problem

## Objective
Before we build anything, understand **why** the new architecture exists by examining what WebForms/code-behind development looks like and where it breaks down.

**Time Investment:** Read and understand (~30 mins)  
**Coding:** None. Conceptual only.

---

## The WebForms Problem: Everything Everywhere

### Your Current Reality (Legacy ASP.NET WebForms)

In WebForms, business logic typically lives in code-behind files:

```csharp
// OrdersPage.aspx.vb
Public Partial Class OrdersPage
    Inherits Page

    Protected Sub CreateOrderButton_Click(sender As Object, e As EventArgs)
        ' Validation
        If String.IsNullOrEmpty(CustomerIdTextBox.Text) Then
            ValidationLabel.Text = "Customer ID required"
            Return
        End If

        ' Database call
        Dim conn As New SqlConnection(ConfigurationManager.ConnectionStrings("Default"))
        conn.Open()
        Dim cmd As New SqlCommand("sp_CreateOrder", conn)
        cmd.CommandType = CommandType.StoredProcedure
        cmd.Parameters.AddWithValue("@CustomerId", Integer.Parse(CustomerIdTextBox.Text))
        cmd.Parameters.AddWithValue("@OrderDate", DateTime.Now)
        ' ... more parameters ...
        Dim result = cmd.ExecuteScalar()
        
        ' Check inventory
        Dim checkInventoryCmd As New SqlCommand("sp_CheckInventory", conn)
        ' ... parameters ...
        Dim inventoryOk = CBool(checkInventoryCmd.ExecuteScalar())
        
        ' Calculate pricing
        Dim priceCmd As New SqlCommand("sp_CalculatePrice", conn)
        ' ... parameters ...
        Dim totalPrice = CDec(priceCmd.ExecuteScalar())
        
        ' More business logic mixed in
        If totalPrice > customer.CreditLimit Then
            ValidationLabel.Text = "Order exceeds credit limit"
            Return
        End If
        
        ' More database calls
        Dim reserveCmd As New SqlCommand("sp_ReserveInventory", conn)
        ' ...
        
        conn.Close()
        
        ' UI feedback
        SuccessLabel.Text = "Order created successfully"
        RefreshOrderList()
    End Sub

    Private Sub RefreshOrderList()
        ' Grid binding, sorting, paging logic all mixed together
        Dim dataTable As New DataTable()
        Dim adapter As New SqlDataAdapter("SELECT * FROM Orders WHERE CustomerId = @Id", conn)
        adapter.Fill(dataTable)
        OrdersGridView.DataSource = dataTable
        OrdersGridView.DataBind()
    End Sub

    Protected Sub CancelOrderButton_Click(sender As Object, e As EventArgs)
        ' Similar mess for a different feature
    End Sub
End Class
```

### The Problems This Creates

#### 1. **Where Does Logic Actually Live?**
- Is it in the stored procedure?
- Is it in the code-behind?
- Is it in a utility class somewhere?
- Probably spread across all three.

**Result:** Bugs are hard to find. To understand how orders are created, you must trace through multiple files and databases.

#### 2. **Testing is Painful**
You cannot test order creation logic without:
- A running database
- A full page lifecycle
- UI event simulation
- A compiled application

```csharp
// How would you test order creation?
// You can't. You have to click a button in a running browser.
```

#### 3. **Features Are Mixed Together**
The same code-behind file handles:
- Customer validation
- Order creation
- Inventory checks
- Pricing
- UI binding
- Grid sorting
- Error display

Changing one feature risks breaking another.

#### 4. **Reuse Requires Copy-Paste**
Want to create an order from:
- Web page
- API endpoint
- Scheduled job
- Integration service

You copy-paste the entire event handler into different places.

#### 5. **Concurrency Becomes a Mystery**
Two orders trying to reserve the same inventory at the same time:
- Which one wins?
- How is it handled?
- Where do you fix it?

The answer is scattered across multiple files.

#### 6. **Change is Expensive**
A new business rule: "Orders over $10,000 require approval from a manager."

Now you must:
- Find all places where orders are created
- Add approval logic to each one
- Test each one
- Hope you didn't miss one
- Hope you didn't break existing logic

---

## Why These Problems Exist

The code-behind approach mixes three concerns:

| Concern | Current Home | Problem |
|---------|--------------|---------|
| **Business Rules** | Code-behind + Stored Procedures | Where is it really? Hard to find. Hard to test. |
| **Orchestration** | Code-behind | Intertwined with UI logic. Can't reuse. |
| **UI Logic** | Code-behind | Mixed with business logic. Can't test independently. |
| **Database Access** | SqlCommand calls scattered everywhere | Impossible to trace data flow. |

---

## The Architecture We're Building Solves This

### Separation of Concerns

Each piece of code will have **one reason to change**:

- **Domain Layer:** Business rules. Changes when business requirements change.
- **Application Layer:** Orchestration. Changes when workflows change.
- **API Layer:** HTTP contracts. Changes when API consumers need different data.
- **Infrastructure Layer:** Database details. Changes when SQL Server is replaced or optimized.

### Feature-Based Organization (Vertical Slicing)

Instead of grouping by layer (Controllers, Services, Repositories), group by feature:

```
Orders/
  CreateOrder/           ← Everything for this feature
    CreateOrderCommand.cs
    CreateOrderHandler.cs
    CreateOrderValidator.cs
    CreateOrderTests.cs
  BookOrder/             ← Everything for this feature
    BookOrderCommand.cs
    BookOrderHandler.cs
  CancelOrder/           ← Everything for this feature
    ...
```

Want to understand "CreateOrder"? Look in that folder. Everything you need is there.

### Testability

Business logic can be tested without a database:

```csharp
[Test]
Public Sub OrderPricingCalculation_WithDiscount_CalculatesCorrectTotal()
    Dim order = New Order(customerId: 1, items: /* ... */)
    Dim pricingRule = New CustomerDiscountRule(customerId: 1, discountPercent: 10)
    
    Dim total = order.CalculateTotal(pricingRule)
    
    Assert.AreEqual(90, total)  ' No database needed. No browser needed.
End Sub
```

### Reusability

The same business logic can be triggered from:
- Web API
- Scheduled job
- Integration message
- Admin tool

No copy-paste. One implementation.

---

## What We're NOT Doing

### We're Not Using These Patterns

| Pattern | Why Not | What We Use Instead |
|---------|---------|-------------------|
| **Entity Framework** | Abstracts too much. We need to understand exactly what SQL runs. | **Dapper** – thin wrapper around ADO.NET. |
| **Generic Repository** | Encourages mixing layers. Hard to understand data flow. | **Query & Command handlers** – explicit, traceable. |
| **Generic Service Layer** | Spreads logic across many classes. Features get hard to locate. | **Handlers in feature folders** – everything for one feature in one place. |
| **MediatR** | License concerns. Too much magic. Hard to follow execution. | **Simple, homemade mediator** – you'll understand every line. |

---

## Learning Path

### Phase 0 (This Document)
Understand the problem.

### Phase 1
Build the absolute simplest mediator possible.  
Understand what a mediator does before we add complexity.

### Phase 2
Create domain entities and value objects.  
Understand how to model business concepts without leaning on the database.

### Phase 3
Build one complete feature end-to-end.  
See how all pieces fit together in a small, understandable example.

### Phase 4
Add validation, error handling, and business rules.  
Understand where validation belongs.

### Phase 5
Add CQRS separation (Commands vs. Queries).  
Understand why separating read and write is beneficial.

### Phase 6
Add integration tests.  
See how to test without brittle mock objects.

### Phase 7
Add the complex features.  
Order approval workflows, inventory concurrency, etc.

### Phase 8
Add end-to-end scenarios.  
Verify everything works together.

---

## Key Insight: The Mediator's Purpose

By the end of Phase 1, you'll understand that the mediator solves this problem:

**Without a mediator:**
```csharp
' Feature 1
Public Function CreateOrder(command As CreateOrderCommand) As OrderId
    Dim validator = New CreateOrderValidator()
    validator.Validate(command)
    
    Dim handler = New CreateOrderHandler()
    Return handler.Handle(command)
End Function

' Feature 2
Public Function BookOrder(command As BookOrderCommand) As BookingId
    Dim validator = New BookOrderValidator()
    validator.Validate(command)
    
    Dim handler = New BookOrderHandler()
    Return handler.Handle(command)
End Function

' Feature 3 ... Feature 50 ... same pattern repeated everywhere
```

**With a mediator:**
```csharp
' One place. All features use the same path.
Public Function Send(Of TResponse)(request As IRequest(Of TResponse)) As TResponse
    Dim validator = GetValidator(GetType(TResponse))
    validator.Validate(request)
    
    Dim handler = GetHandler(GetType(request))
    Return handler.Handle(request)
End Function

' Now just:
mediator.Send(New CreateOrderCommand())
mediator.Send(New BookOrderCommand())
' ... no repetition, consistent error handling, easy to add cross-cutting concerns
```

---

## Next Steps

When you're ready to proceed:

1. Read this document fully.
2. Ensure you understand **why** we're building a new architecture.
3. Move to Phase 1.

The goal is not to memorize patterns. The goal is to **experience the pain** these patterns solve.

