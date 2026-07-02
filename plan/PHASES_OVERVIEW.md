# Enterprise Architecture Learning Project – Complete Phases Overview

## Your Learning Journey

This project is structured in phases to gradually build understanding.

**Do NOT skip ahead.** Each phase builds on previous ones.

---

## Phase Breakdown

### Phase 0: Foundation (READ FIRST)
**File:** `PHASE_0_FOUNDATION.md`  
**Time:** ~30 minutes (reading only, no coding)  
**Outcome:** Understand why these patterns exist

**What you learn:**
- Problems with WebForms/code-behind architecture
- Why separation of concerns matters
- What a mediator does (conceptually)

**Before Phase 1:** Ensure you fully understand the problems described. If you think "this is fine, my code-behind approach works," Phase 1 will be frustrating. The patterns only make sense if you understand the pain.

---

### Phase 1: Simple Mediator
**File:** `PHASE_1_SIMPLE_MEDIATOR.md`  
**Time:** 2–3 hours (building + testing)  
**Deliverable:** Working mediator with 2 features

**What you build:**
- `IRequest<TResponse>` interface
- `IRequestHandler<TRequest, TResponse>` interface  
- Mediator class (reflection-based dispatch)
- `IValidator<TRequest>` interface
- CreateOrder command + handler + validator
- BookOrder command + handler + validator
- Unit tests (no database)

**Key concept:** The mediator is just a dispatcher that:
1. Runs validators
2. Finds the handler
3. Invokes the handler
4. Returns the response

That's it. No magic. You write every line.

**After Phase 1:** You'll understand why `mediator.Send(command)` is better than calling handler directly everywhere.

---

### Phase 2: Domain-Driven Design
**File:** `PHASE_2_DOMAIN_DRIVEN_DESIGN.md`  
**Time:** 3–4 hours (building domain layer)  
**Deliverable:** Complete domain layer with business logic

**What you build:**
- Strongly-typed IDs: `OrderId`, `CustomerId`, `MaterialId`
- Value objects: `Money`, `Quantity`, `CustomerEmail`
- Domain events: `OrderCreatedEvent`, `OrderBookedEvent`
- Domain entities: `Order`, `Customer`, `Material`
- Business rules enforced in entities

**Key concept:** Domain models represent how the business works, NOT how the database works.

**Example difference:**
```csharp
' WebForms: database-shaped
Order.TotalAmount (is this calculated or stored?)

' DDD: business-shaped
Order.CalculateTotal()  (explicit, calculated on demand)
```

**After Phase 2:** You'll understand why:
- `Order` has a `Book()` method, not a `Status = "Pending"` assignment
- `Quantity` throws if you try to create negative quantity
- `Money` prevents currency mismatches

---

### Phase 3: Database Integration
**File:** `PHASE_3_DATABASE_INTEGRATION.md`  
**Time:** 3–4 hours (Dapper + repositories)  
**Deliverable:** Working data access layer

**What you build:**
- `IOrderRepository` interface
- `OrderRepository` implementation (Dapper)
- `OrderDataModel` (database representation)
- `OrderDataMapper` (domain ↔ database conversion)
- Stored procedures for complex operations
- `OrderQueryService` for read models
- Integration tests with real database

**Key concept:** Repositories handle persistence. Domain entities don't know about SQL.

**Three models:**
1. **Domain Model** – `Order` entity (business logic)
2. **Database Model** – `OrderDataModel` (schema representation)
3. **DTO Model** – `CreateOrderRequest` (API contract)

Mappers convert between them.

**After Phase 3:** You'll understand why you need both aggregate roots AND read models for performance.

---

### Phase 4: Complete Feature Implementation
**File:** `PHASE_4_COMPLETE_FEATURES.md`  
**Time:** 4–5 hours (full features + testing)  
**Deliverable:** 4 complete end-to-end features

**What you build:**
- CreateOrder (full pipeline)
- BookOrder (state transition)
- CancelOrder (business rules)
- GetOrders (CQRS query)
- Dependency injection setup
- End-to-end tests

**Key concept:** All layers working together in a single flow.

**Example flow:**
```
POST /api/orders
  → OrdersController.CreateOrder
    → mediator.Send(CreateOrderCommand)
      → CreateOrderValidator.Validate
      → CreateOrderHandler.Handle
        → Load customer (check credit)
        → Create Order entity (domain logic)
        → Load materials (prices, stock)
        → Add items (domain rules enforced)
        → Save via repository (atomic transaction)
        → Publish domain events
      → Return response
  → HTTP 201 Created
```

**After Phase 4:** You understand the complete architecture and how pieces fit.

---

### Phase 5+: Advanced Features (To Be Implemented)
These phases will add complexity while maintaining the architecture.

**Phase 5:** CQRS Formalization
- Separate read and write databases
- Event sourcing introduction
- Query optimization

**Phase 6:** Complex Business Rules
- Order approval workflows
- Inventory concurrency
- Price recalculation

**Phase 7:** Testing Strategies
- Unit tests (domain logic)
- Integration tests (repository + database)
- End-to-end tests (complete workflows)
- Performance testing

**Phase 8:** Production Concerns
- Error handling strategies
- Logging and observability
- Caching patterns
- API versioning

---

## How to Use These Documents

### For Each Phase:

1. **Read the entire document first.** Don't jump to code.

2. **Understand the "why" before the "what".**
   - Why does this pattern exist?
   - What problem does it solve?
   - What would happen without it?

3. **Implement step-by-step.** Follow the order in the document.

4. **Test as you go.** Don't build everything then test.

5. **Refer back to previous phases** when you're confused.

### If You Get Stuck:

**First:** Re-read the conceptual explanation at the top of the phase.

**Second:** Look for "Why does this exist?" sections.

**Third:** Review the completed example code.

**Last:** Think about what problem you're solving. The architecture should solve that problem.

---

## Critical Concepts to Internalize

### #1: Separation of Concerns

Each layer has ONE responsibility:

```csharp
Controller      → HTTP concerns (parse, respond)
Validator       → Input validation
Handler         → Orchestration
Domain Entity   → Business rules
Repository      → Persistence
Database        → Data integrity
```

If you find yourself mixing these, something is wrong.

### #2: The Mediator is Just a Dispatcher

It does NOT:
- Know about your business logic
- Know about your database
- Perform miracles

It DOES:
- Find validators for a request
- Find the handler for a request
- Call them in order
- Return the response

If the mediator feels magical, you're misunderstanding it.

### #3: Domain Logic Lives in Entities

NOT in services, NOT in repositories, NOT in handlers.

**Right:**
```csharp
order.Book()  ' Entity enforces the rules
```

**Wrong:**
```csharp
If order.Items.Count > 0 AndAlso order.Status = Draft Then  ' Logic in handler
    order.Status = Pending
End If
```

### #4: Read ≠ Write (CQRS)

Commands (writes):
- Load full aggregate
- Enforce all business rules
- One update at a time
- Slow but consistent

Queries (reads):
- Use denormalized read models
- No business logic
- Multiple records at once
- Fast for reporting

### #5: Testing Levels

**Unit Tests:** Domain logic, no database
```csharp
order.AddItem(materialId, quantity, price)  ' Works without database
```

**Integration Tests:** Repositories, stored procedures, transactions
```csharp
Dim savedOrder = Await repository.GetById(orderId)  ' Uses real database
```

**End-to-End Tests:** Complete workflows
```csharp
CreateOrder → BookOrder → CancelOrder → VerifyDatabase  ' Full path
```

---

## Common Mistakes (Avoid These)

### ❌ Mistake #1: Skipping Phases
"I understand mediators, let me skip to Phase 4."

**Why this fails:** Phase 4 builds on domain modeling (Phase 2) and database integration (Phase 3). You'll be lost.

**Fix:** Do every phase, even if you think you already know it.

### ❌ Mistake #2: Putting Business Logic in Handlers

```csharp
' WRONG
Handler:
    If order.Total > 10000 Then  ' Business logic in handler
        order.Status = Approved
    End If

' RIGHT
Handler:
    order.Approve()  ' Entity enforces the rules
```

**Why this fails:** Logic spread across many handlers. Hard to test, easy to duplicate.

**Fix:** Ask "is this a business rule?" If yes, it goes in the entity.

### ❌ Mistake #3: Using Generic Repositories

```csharp
' WRONG
Interface IRepository(Of T)
    Function GetById(id) As T
    Function Save(entity As T)
End Interface

' RIGHT
Interface IOrderRepository
    Function GetById(orderId As OrderId) As Task(Of Order)
    Function Create(order As Order) As Task(Of OrderId)
End Interface
```

**Why this fails:** Generic repos encourage leaking abstractions. Specific repos clarify intent.

**Fix:** Each aggregate has its own repository. Interfaces are specific to that aggregate.

### ❌ Mistake #4: Storing Calculated Values

```csharp
' WRONG
Order.TotalAmount stored in database
Then later: calculation changes but stored value doesn't

' RIGHT
Calculate TotalAmount on-the-fly from line items
Or store it BUT update it whenever items change
```

**Why this fails:** Two sources of truth. They get out of sync.

**Fix:** Either calculate always, or always recalculate when source data changes.

### ❌ Mistake #5: Not Publishing Domain Events

```csharp
' WRONG
order.Book()
Await repository.Update(order)
' Nothing happens. No audit trail. No other processes triggered.

' RIGHT
order.Book()  ' Raises OrderBookedEvent
Await repository.Update(order)
Await eventPublisher.Publish(order.DomainEvents)
' Events are logged, other processes notified
```

**Why this fails:** System can't react to changes. Hard to audit. Inventory not reserved. Email not sent.

**Fix:** Always publish events. Let event handlers deal with consequences.

---

## Project Structure (Final)

After all phases, your project looks like:

```
OrderSystem/
├── src/
│   ├── Domain/                 # Phase 2
│   │   ├── Shared/
│   │   │   ├── EntityId.cs
│   │   │   ├── Money.cs
│   │   │   └── Quantity.cs
│   │   ├── Orders/
│   │   │   ├── Order.cs
│   │   │   ├── OrderLineItem.cs
│   │   │   ├── OrderStatus.cs
│   │   │   └── Events/
│   │   ├── Customers/
│   │   └── Inventory/
│   ├── Core/                   # Phase 1
│   │   └── Mediator/
│   │       ├── IRequest.cs
│   │       ├── IRequestHandler.cs
│   │       ├── IValidator.cs
│   │       └── Mediator.cs
│   ├── Application/            # Phase 1-4
│   │   ├── Features/
│   │   │   └── Orders/
│   │   │       ├── CreateOrder/
│   │   │       ├── BookOrder/
│   │   │       ├── CancelOrder/
│   │   │       └── GetOrders/
│   ├── Infrastructure/         # Phase 3
│   │   ├── Repositories/
│   │   │   ├── IOrderRepository.cs
│   │   │   ├── ICustomerRepository.cs
│   │   │   └── OrderRepository.cs
│   │   └── Data/
│   │       ├── Orders/
│   │       └── Scripts/
│   ├── API/                    # Phase 4
│   │   └── Controllers/
│   │       └── OrdersController.cs
│   └── Program.cs              # Dependency injection
│
└── tests/
    ├── Unit/
    │   ├── Domain/
    │   └── Features/
    ├── Integration/
    │   ├── Repositories/
    │   └── Workflows/
    └── Database/
        └── Scripts/
```

**Why this structure:**

- **Vertical slicing:** Each feature self-contained
- **Layer separation:** Domain, Application, Infrastructure isolated
- **Testing:** Unit, Integration, E2E all organized
- **Clarity:** Anyone can navigate and find what they need

---

## Success Criteria

By the end of Phase 4, you should:

### Knowledge
- ✅ Understand why you need a mediator
- ✅ Know what belongs in domain entities
- ✅ Know how repositories work
- ✅ See the difference between domain models and database models

### Skills
- ✅ Write domain entities with business rules
- ✅ Implement handlers that orchestrate
- ✅ Use Dapper to persist entities
- ✅ Write validators that are reusable
- ✅ Test without a database

### Perspective
- ✅ See code as organized vertically (by feature), not horizontally (by layer)
- ✅ Appreciate the separation between HTTP, business logic, and data
- ✅ Understand why CQRS (separate read/write) makes sense

---

## After You Complete All Phases

You'll be ready to:

1. **Build real enterprise systems** using these patterns
2. **Migrate legacy code** from WebForms to modern architecture
3. **Teach others** about DDD, CQRS, and vertical slicing
4. **Make better architectural decisions** because you understand tradeoffs
5. **Debug complex issues** because you know where each piece belongs

---

## Recommended Order

### Week 1
- Phase 0: Foundation (1 hour)
- Phase 1: Mediator (8 hours over 2-3 days)

### Week 2
- Phase 2: Domain-Driven Design (10 hours over 2-3 days)

### Week 3
- Phase 3: Database Integration (10 hours over 2-3 days)

### Week 4
- Phase 4: Complete Features (12 hours over 2-3 days)

**Total:** ~4 weeks of learning and implementation

This is worth the time investment. You'll reference these patterns for the rest of your career.

---

## Getting Help

### If Code Won't Compile
1. Check you're using the same VB.NET version as examples
2. Verify all `Imports` statements
3. Ensure NuGet packages are installed (Dapper, ASP.NET Core)

### If Logic Seems Wrong
1. Re-read the conceptual section at the top of the phase
2. Ask: "What problem is this solving?"
3. Compare your code to the example

### If Tests Fail
1. Check test data setup
2. Verify database schema matches SQL scripts
3. Use debugger to trace execution

### If You Feel Lost
1. Go back to the previous phase
2. Re-read the "Key Insights" section
3. Implement a simpler version first

---

## Next Steps

1. **Read Phase 0 completely** – Understand the problems
2. **Implement Phase 1** – Build a working mediator
3. **Ask yourself:** "Does this solve the duplication problem?"
4. **Move to Phase 2** – Add domain modeling
5. **Continue through all phases**

You now have a complete learning path. Follow it deliberately.

The goal isn't speed. The goal is deep understanding.

Start with Phase 0.

