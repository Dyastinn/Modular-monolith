# How to Use This Learning Project

## What You Have

Five markdown documents, each representing one phase of learning:

1. **PHASES_OVERVIEW.md** – Start here (this file explains the whole plan)
2. **QUICK_REFERENCE.md** – Keep open while reading
3. **PHASE_0_FOUNDATION.md** – Why these patterns exist
4. **PHASE_1_SIMPLE_MEDIATOR.md** – Build a working mediator
5. **PHASE_2_DOMAIN_DRIVEN_DESIGN.md** – Model business concepts
6. **PHASE_3_DATABASE_INTEGRATION.md** – Connect to SQL Server with Dapper
7. **PHASE_4_COMPLETE_FEATURES.md** – Wire everything together

---

## Your Workflow

### Step 1: Read Phase 0 (This Week)
- Open `PHASE_0_FOUNDATION.md`
- Read the entire document
- Don't skip sections
- Keep `QUICK_REFERENCE.md` nearby
- Goal: Understand the problems being solved

**Time:** 1–2 hours

**Success:** You can explain why code-behind architecture breaks down.

### Step 2: Implement Phase 1 (Next 3 Days)
- Open `PHASE_1_SIMPLE_MEDIATOR.md`
- Read the conceptual sections
- Follow the step-by-step implementation
- Build the mediator from scratch
- Write tests
- Create both example features (CreateOrder, BookOrder)

**Time:** 6–8 hours spread over 3 days

**Success:** You have a working mediator with 2 features tested.

**Ask yourself:** "Does this solve the duplication problem?"

### Step 3: Implement Phase 2 (Next 3 Days)
- Open `PHASE_2_DOMAIN_DRIVEN_DESIGN.md`
- Implement value objects (Money, Quantity, Email)
- Implement strongly-typed IDs
- Implement domain entities (Order, Customer, Material)
- Write unit tests (no database)

**Time:** 8–10 hours spread over 3 days

**Success:** All business logic lives in domain entities. Tests run without database.

**Ask yourself:** "Is this how the business actually works?"

### Step 4: Implement Phase 3 (Next 3 Days)
- Open `PHASE_3_DATABASE_INTEGRATION.md`
- Create database schema
- Implement repositories with Dapper
- Create mappers (domain ↔ database)
- Write integration tests against real database

**Time:** 8–10 hours spread over 3 days

**Success:** Data persists and loads correctly. Integration tests pass.

**Ask yourself:** "Does the repository handle all persistence concerns?"

### Step 5: Implement Phase 4 (Next 3 Days)
- Open `PHASE_4_COMPLETE_FEATURES.md`
- Implement 4 complete features (Create, Book, Cancel, Get)
- Set up dependency injection
- Write end-to-end tests
- Test complete workflows

**Time:** 10–12 hours spread over 3 days

**Success:** Complete workflows work. You can POST /api/orders and it works end-to-end.

**Ask yourself:** "How do all these pieces fit together?"

---

## How to Work with Claude/Sonnet

### Use Case 1: "Explain This Concept"

**You:** "Can you explain the difference between a domain entity and a database model?"

**Claude:** Explains with examples from the phases.

**Good because:** You're connecting phases and deepening understanding.

### Use Case 2: "Debug My Code"

**You:** "My CreateOrderHandler is throwing, but I don't know why. Can you help debug?"

**Claude:** Asks questions, helps you trace execution.

**Good because:** You learn debugging, not just copy-paste.

### Use Case 3: "Show Me How to Implement Feature X"

**You:** "The phase shows UpdateCustomer as an example. Can you show me how to implement it?"

**Claude:** Shows how it follows the same pattern as CreateOrder.

**Good because:** You reinforce the pattern by applying it.

### Use Case 4: "Review My Code"

**You:** "I've implemented Phase 1. Can you review and point out any issues?"

**Claude:** Reviews against the patterns described.

**Good because:** You get feedback on your implementation.

### Use Case 5: "I'm Stuck"

**You:** "I don't understand why value objects are immutable. Can you explain?"

**Claude:** Re-explains, gives new examples.

**Good because:** Different explanation might click for you.

---

## What NOT to Ask Claude

### ❌ "Can you just write all the code for me?"
**Why this doesn't work:** You learn nothing. The whole point is understanding.

**Instead:** Ask for explanations, ask for reviews, ask for help debugging.

### ❌ "Can you skip the theory and just implement?"
**Why this doesn't work:** Without understanding the "why," you'll rebuild the same mistakes.

**Instead:** Read Phase 0 thoroughly. Understand before coding.

### ❌ "Can you use different tools?" (Entity Framework, MediatR, etc.)
**Why this doesn't work:** The learning comes from building from scratch.

**Instead:** Follow the documents exactly. If you want EF later, you'll understand why you're choosing it.

### ❌ "Can you make this faster?"
**Why this doesn't work:** The 4 weeks is about understanding, not speed.

**Instead:** Take your time. Re-read sections. Let things sink in.

---

## A Typical Conversation

### Session 1: Understanding Phase 1

**You:** "I don't get why we need a mediator. Can't I just call handlers directly?"

**Claude:** Explains the duplication problem. Shows code before/after mediator.

**You:** "OK, I see the duplication now. But why reflection? Isn't that slow?"

**Claude:** Explains that reflection is only at registration time, not per-request. Shows the tradeoff.

**You:** "Alright, I'm going to implement Phase 1 now."

### Session 2: Implementing Phase 1

**You:** "My mediator compiles but the handler isn't being called. What's wrong?"

**Claude:** Asks clarifying questions. Traces through the flow. Finds the issue (handler not registered).

**You:** "Oh! I forgot to register in Program.cs. It works now!"

**Claude:** Points out this is a common mistake.

### Session 3: Testing Phase 1

**You:** "My tests are passing but I feel like I'm missing something. Is there a test I should write?"

**Claude:** Suggests testing error cases (validator fails, handler throws).

**You:** "Good point, I hadn't tested that."

### Session 4: Understanding Phase 2

**You:** "Why can't I just use decimal for Money?"

**Claude:** Shows the problem (negative prices allowed, concurrency issues).

**You:** "OK, making a Money value object."

---

## Benchmarks (You Should Hit These)

### After Phase 1 (Day 5)
- [ ] Mediator compiles and runs
- [ ] CreateOrder feature works
- [ ] BookOrder feature works
- [ ] Unit tests pass
- [ ] Can explain why mediator is useful

### After Phase 2 (Day 8)
- [ ] Domain entities defined
- [ ] Value objects defined
- [ ] Business rules in entities (not handlers)
- [ ] Unit tests run without database
- [ ] Can explain entity vs. value object

### After Phase 3 (Day 11)
- [ ] Database schema created
- [ ] Dapper repository working
- [ ] Mappers convert domain ↔ database
- [ ] Integration tests pass
- [ ] Can trace data from API to database

### After Phase 4 (Day 14)
- [ ] 4 complete features implemented
- [ ] End-to-end tests passing
- [ ] Can POST /api/orders and it works
- [ ] Can trace complete request flow
- [ ] Understanding feels solid

---

## Common Pitfalls (And How to Avoid)

### Pitfall #1: Falling Behind
You get stuck on Phase 2 and don't move forward.

**Prevention:**
- Set a deadline per phase (3 days)
- If stuck after 2 hours, ask for help
- Don't try to be perfect
- "Good enough" is fine; you'll refactor later

### Pitfall #2: Skipping to "Interesting" Stuff
You skip from Phase 1 to Phase 4 because you want to see it work.

**Prevention:**
- Remember: Phase 4 has no value without Phases 2 & 3
- Trust the progression
- The "boring" stuff is where understanding happens

### Pitfall #3: Copy-Paste Without Reading
You copy code examples without understanding.

**Prevention:**
- Read the explanation first
- Then type the code (don't copy-paste)
- Then verify it compiles
- Then ask "why does this work?"

### Pitfall #4: "I Already Understand This"
You think you know mediators after 10 minutes.

**Prevention:**
- Implement it anyway
- Write tests
- Try to break it
- Only then decide if you understand

### Pitfall #5: Using Wrong Language
You try to implement in C# instead of VB.NET as documented.

**Prevention:**
- Examples are in VB.NET for a reason (your background)
- If you switch languages, do it intentionally (Phase 2+)
- Don't fight the flow; follow the documents

---

## When to Ask for Help

### Good times to ask Claude:
- "I don't understand this concept"
- "My code doesn't compile, can you help debug?"
- "Can you review my implementation against the spec?"
- "Can you show me how this pattern applies to a different scenario?"
- "I'm stuck on this problem, where should I look?"

### Not-so-good times:
- "Can you just write it for me?"
- "I haven't read the phase yet, what should I do?"
- "Why does this matter?" (re-read Phase 0)
- After 5 minutes of debugging (give it 30 minutes first)

---

## Files You'll Create

After implementing all phases, you'll have created:

### Phase 1
- `Core/Mediator/Mediator.cs`
- `Core/Mediator/IRequest.cs`
- `Core/Mediator/IRequestHandler.cs`
- `Core/Mediator/IValidator.cs`
- `Features/Orders/CreateOrder/CreateOrderCommand.cs`
- `Features/Orders/CreateOrder/CreateOrderHandler.cs`
- `Features/Orders/CreateOrder/CreateOrderValidator.cs`
- `Features/Orders/BookOrder/*` (similar structure)
- Tests for above

### Phase 2
- `Domain/Shared/EntityId.cs`
- `Domain/Shared/Money.cs`
- `Domain/Shared/Quantity.cs`
- `Domain/Orders/Order.cs`
- `Domain/Orders/OrderStatus.cs`
- `Domain/Orders/Events/*.cs`
- `Domain/Customers/Customer.cs`
- `Domain/Inventory/Material.cs`
- Tests for domain logic

### Phase 3
- `Infrastructure/Repositories/IOrderRepository.cs`
- `Infrastructure/Repositories/OrderRepository.cs`
- `Infrastructure/Data/Orders/OrderDataModel.cs`
- `Infrastructure/Data/Orders/OrderDataMapper.cs`
- `Infrastructure/Data/Orders/OrderQueryService.cs`
- Database schema SQL scripts
- Integration tests

### Phase 4
- `Controllers/OrdersController.cs`
- Complete handlers for: CreateOrder, BookOrder, CancelOrder, GetOrders
- `Program.cs` with full dependency injection
- End-to-end tests

---

## After Phase 4 (What's Next?)

You'll be ready for:

### Phase 5: CQRS Formalization
- Separate read database
- Event sourcing introduction
- Projections

### Phase 6: Complex Business Rules
- Approval workflows
- Inventory concurrency
- Price recalculation

### Phase 7: Testing Strategies
- Test pyramids
- Fixture data patterns
- Performance testing

### Phase 8: Production Concerns
- Error handling
- Logging
- Caching
- Versioning

But **don't** jump ahead. Finish Phase 4 first.

---

## Final Advice

### This is an investment in your career
4 weeks now saves you months of confusion later. You'll use these patterns for the next 10+ years.

### Go slow
Speed kills learning. Re-read sections. Let things sink in. Ask questions.

### Focus on understanding, not code
The code is just a vehicle for understanding the architecture. If you understand why, the code is obvious.

### Build it yourself
Copy-paste teaches nothing. Type every line. Make mistakes. Debug. Learn.

### Refer back constantly
After you finish, you'll come back to these documents again and again. "Where does this go?" → Look at decision tree.

### Share your learning
After you finish, explain these concepts to someone else. Teaching is the best way to deepen understanding.

---

## Your First Step

1. Open `PHASES_OVERVIEW.md` (if you haven't already)
2. Read the intro section
3. Then open `PHASE_0_FOUNDATION.md`
4. Read it completely
5. Don't write any code yet
6. When you're done, ask Claude: "Can you explain the three-model problem from Phase 0?"

If you understand that explanation, you're ready for Phase 1.

---

## Good luck.

The architecture you're about to learn is real, proven, and used in enterprise systems worldwide.

You're not learning theory. You're learning how real software gets built.

Take your time. Understand deeply. Ask questions.

By day 14, you'll look at code differently.

Start with Phase 0. Read the whole thing.

