# Quick Reference – Architecture Patterns & Decisions

Keep this open while reading the phases.

---

## When to Use Each Pattern

### Mediator
**Use when:** Multiple features follow the same dispatch pattern
**Don't use:** If you only have one handler
**Benefit:** Consistent error handling, validation, cross-cutting concerns

### Domain Entity
**Use when:** Representing a business concept with rules
**Don't use:** For database records or DTOs
**Benefit:** Business rules enforced at construction time

### Value Object
**Use when:** Representing a measurement (Money, Quantity) or identity (Email)
**Don't use:** For entities that have identity
**Benefit:** Type safety, immutability, clear intent

### Repository
**Use when:** Loading or saving an aggregate
**Don't use:** For simple queries (use read models instead)
**Benefit:** Single place to manage persistence

### Stored Procedure
**Use when:** Database constraints, inventory locking, complex atomic operations
**Don't use:** For business logic or calculations
**Benefit:** Database-level atomicity, performance

### Read Model
**Use when:** Querying for display (list orders, search)
**Don't use:** When writing/updating data
**Benefit:** Query optimization without loading full entities

---

## Decision Tree: Where Does This Code Go?

```
Is it a business rule?
├─ YES: Domain entity method
│  └─ order.Book()
│  └─ customer.CanPlaceOrder(amount)
└─ NO:
   │
   Is it orchestration between domain objects?
   ├─ YES: Handler
   │  └─ Load customer, load materials, create order
   └─ NO:
      │
      Is it data access?
      ├─ YES: Repository
      │  └─ Save to database, load by ID
      └─ NO:
         │
         Is it validation of input?
         ├─ YES: Validator
         │  └─ Check customer exists, email format valid
         └─ NO:
            │
            Is it HTTP parsing/response formatting?
            ├─ YES: Controller
            │  └─ Parse JSON, return 200/400/500
            └─ NO:
               │
               Is it a read-only query?
               ├─ YES: Read model / Query service
               │  └─ Search orders, get summary
               └─ NO: ???
```

---

## Code Location Cheat Sheet

| Code | Location | Why |
|------|----------|-----|
| `order.Book()` | `Domain/Orders/Order.cs` | Business rule about orders |
| `CreateOrderValidator` | `Features/Orders/CreateOrder/` | Input validation for feature |
| `CreateOrderHandler` | `Features/Orders/CreateOrder/` | Orchestration for feature |
| `IOrderRepository` | `Infrastructure/Repositories/` | Data access contract |
| `OrderRepository` | `Infrastructure/Repositories/` | Data access implementation |
| `OrderDataModel` | `Infrastructure/Data/` | Database representation |
| `OrderReadModel` | `Infrastructure/Data/` | Query optimization |
| `OrdersController` | `API/Controllers/` | HTTP endpoint |

---

## Mediator Flow (Simplified)

```
request = new CreateOrderCommand()
         ↓
   mediator.Send(request)
         ↓
   Get validators for CreateOrderCommand
         ↓
   Run all validators
   ├─ Fail? Throw ValidationException
   └─ Pass? Continue
         ↓
   Get handler for CreateOrderCommand
         ↓
   Call handler.Handle(request)
         ↓
   Return response
```

---

## Domain Entity Flow (Simplified)

```
order = new Order(customerId)
         ↓
   order.AddItem(materialId, quantity, price)
         ↓
   Domain enforces:
   ├─ Can only add to Draft orders
   ├─ Quantity must be positive (Quantity value object)
   └─ Price must be non-negative (Money value object)
         ↓
   order.Book()
         ↓
   Domain enforces:
   ├─ Must have at least one item
   ├─ Must be in Draft status
   └─ Raise OrderBookedEvent
         ↓
   Result: Consistent, valid Order object
```

---

## Handler Flow (Simplified)

```
CreateOrderHandler.Handle(command)
         ↓
   Load customer from repository
   └─ Check exists, active, credit limit
         ↓
   Create Order entity
   └─ Starts in Draft status
         ↓
   Load materials from repository
   └─ Check exists, price correct
         ↓
   For each item:
   └─ order.AddItem()  [domain rules enforced]
         ↓
   Validate business rules:
   └─ Order total <= customer credit limit
         ↓
   Save order via repository
   └─ All-or-nothing transaction
         ↓
   Publish domain events
   └─ Audit trail, trigger other processes
         ↓
   Return response
```

---

## Testing Strategy Summary

### Unit Tests (Domain Logic)
```csharp
Test
  new Order()
  order.AddItem()
  order.Book()
  ✅ No database
  ✅ No mocks
  ✅ Lightning fast
```

### Integration Tests (Repository)
```csharp
Test
  repository.Create(order)
  repository.GetById(orderId)
  ✅ Real database
  ✅ No mocks
  ✅ Verify persistence
```

### End-to-End Tests (Workflows)
```csharp
Test
  mediator.Send(CreateOrderCommand)
  mediator.Send(BookOrderCommand)
  mediator.Send(CancelOrderCommand)
  Verify database
  ✅ Real database
  ✅ Complete flow
  ✅ Integration verified
```

---

## Common Refactorings

### "My handler is getting too complex"
**Solution:** Move business logic to domain entity
```csharp
' Before (handler has logic)
If order.Items.Count = 0 Then
    Throw New InvalidOperationException()
End If

' After (entity has logic)
order.Book()  ' Entity throws if no items
```

### "I have the same validation in multiple handlers"
**Solution:** Create a shared validator or domain rule
```csharp
' Before (duplicated)
If request.Email.Contains("@") Then ' In handler 1
If request.Email.Contains("@") Then ' In handler 2

' After (centralized)
new CustomerEmail(request.Email)  ' Throws in constructor
```

### "I'm loading the same data in multiple handlers"
**Solution:** Create a domain service or query service
```csharp
' Before (duplicated queries)
Dim customer = repository.GetById(customerId)  ' Handler 1
Dim customer = repository.GetById(customerId)  ' Handler 2

' After (reusable)
' No change - this is actually correct!
' Each handler loads what it needs
```

### "My queries are slow"
**Solution:** Create a read model (denormalized view)
```csharp
' Before (loading full entities)
Dim orders = repository.GetByCustomerId(customerId)  ' Joins, calculations

' After (read model)
Dim summaries = queryService.GetOrderSummaries(customerId)  ' Simple join
```

---

## Error Handling Strategy

### Where Validation Errors Come From

```
1. Validator
   ├─ Input format invalid
   ├─ Referenced entity doesn't exist
   └─ Throw ValidationException
   
2. Domain Entity
   ├─ Business rule violated
   ├─ Invalid state transition
   └─ Throw InvalidOperationException
   
3. Database
   ├─ Constraint violated
   ├─ Concurrency conflict
   └─ Throw SqlException (handle in repository)
```

### In Handler

```csharp
Try
    order.Book()  ' Might throw if rule violated
    repository.Update(order)  ' Might throw if DB constraint
Catch ex As InvalidOperationException
    Throw  ' Let controller handle
Catch ex As SqlException
    ' Handle concurrency, retry logic
End Try
```

### In Controller

```csharp
Try
    response = mediator.Send(command)
    Return Ok(response)
Catch ex As ValidationException
    Return BadRequest()  ' 400
Catch ex As InvalidOperationException
    Return BadRequest()  ' 400
Catch ex As Exception
    Return StatusCode(500)  ' 500
End Try
```

---

## Performance Checklist

Before shipping to production:

- [ ] **Queries optimized:** Read models avoid loading full entities
- [ ] **Indexes created:** Database indexes on FK, status, dates
- [ ] **Stored procedures for complex ops:** Inventory updates, complex locks
- [ ] **Caching strategy:** Domain events published, subscribers cache busted
- [ ] **Pagination:** Large result sets paginated
- [ ] **Transactions short:** Don't hold locks across HTTP requests
- [ ] **Async/await:** Use throughout (no blocking calls)
- [ ] **Connection pooling:** SQL Server connection pool configured
- [ ] **Batch operations:** Don't insert one-at-a-time in loops
- [ ] **Monitoring:** Slow query log, error rates tracked

---

## When to Break the Rules

### You can put logic in the handler if:
- It's orchestration only (no business logic)
- It coordinates between entities
- Example: "Load customer, load order, transfer ownership"

### You can skip the validator if:
- Only the handler uses this command
- No external input (internal only)
- Example: Internal async job command

### You can merge read and write if:
- Feature is tiny
- Single use case
- But expect to split later

### You can use generic repository if:
- Truly just CRUD
- No aggregate-specific rules
- But prefer specific repositories

---

## Key Terminology

| Term | Means | Example |
|------|-------|---------|
| **Aggregate** | A cluster of domain objects | Order + LineItems together |
| **Aggregate Root** | The entity that owns the aggregate | Order (owns LineItems) |
| **Repository** | Loads/saves aggregates | OrderRepository |
| **Domain Event** | Something significant happened | OrderCreatedEvent |
| **Value Object** | Immutable, no identity | Money, Quantity |
| **Entity** | Identity over time | Order (same order, different state) |
| **Handler** | Executes a command | CreateOrderHandler |
| **Validator** | Checks input | CreateOrderValidator |
| **Mediator** | Routes requests to handlers | mediator.Send(command) |
| **Read Model** | Optimized for queries | OrderSummaryReadModel |
| **CQRS** | Separate read and write | Commands write, Queries read |
| **DDD** | Domain-Driven Design | Model business concepts as entities |
| **Vertical Slice** | Feature-organized folder | Features/Orders/CreateOrder/ |

---

## Quick Problem Diagnosis

### "All my handlers are doing the same thing"
→ Extract to mediator middleware or base class
→ Or ensure they're actually orchestrating different things

### "I don't know where to put this code"
→ Use the decision tree above
→ Ask "what would change if this code moved?"

### "My entities have too many methods"
→ Split into multiple entities
→ Or create domain service

### "My tests require a database"
→ Move logic to domain entity
→ Mock the repository

### "My handlers are calling other handlers"
→ Extract common logic to domain service
→ Or combine into single handler

### "I have too many repositories"
→ That's good!
→ Each aggregate should have one

### "My read model is out of sync"
→ Rebuild it from domain events
→ Or ensure repository publishes events

---

## Migration Checklist

Moving from WebForms to this architecture:

- [ ] Extract business logic from code-behind → Domain entity
- [ ] Extract orchestration → Handler
- [ ] Extract validation → Validator
- [ ] Extract data access → Repository
- [ ] Extract queries → Read model
- [ ] Build mediator
- [ ] Wire up dependency injection
- [ ] Write unit tests (no database)
- [ ] Write integration tests (with database)
- [ ] Deploy incrementally (feature-by-feature)

---

## Resources to Re-Read

When you're confused:

1. **"What problem does this solve?"** → Phase 0
2. **"How does the mediator work?"** → Phase 1
3. **"Where does this rule belong?"** → Phase 2
4. **"How do I persist this?"** → Phase 3
5. **"How does it all fit together?"** → Phase 4

---

## Your Mental Model (After All Phases)

```
Users
  ↓
  HTTP Request
    ↓
    Controller (parse HTTP)
      ↓
      Mediator (dispatch)
        ↓
        Validator (input check)
          ↓
          Handler (orchestration)
            ↓
            Domain Entity (business rules)
              ↓
              Repository (persistence)
                ↓
                Database (storage + constraints)
    ↑
    HTTP Response
↑
Users
```

Each layer has one job. Each layer is testable in isolation. Each layer can be changed without breaking others.

This is the architecture. This is why it exists.

