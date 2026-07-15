# `business-phase15` — the Phase 15 reference build (per-module contracts + `internal`)

This is a **separate, optional** reference build. It is a copy of [`../business`](../business) with the
**Phase 15** refactor applied — the one taught in
[`plan-business-tutorial/15-scaling-the-wall.md`](../../plan-business-tutorial/15-scaling-the-wall.md).

## Why it's a separate folder, not a change to `../business`

The committed base build ([`../business`](../business)) is the runnable proof for **Phases 0–14**, and those
phases teach the wall as *"the contract lives in the neutral `Shared` kernel, and neither module references
the other"* (Phase 1, Phase 7, Phase 13, the GLOSSARY, and `CLAUDE.md §4`). Phase 15 deliberately **changes
that architecture** — so applying it to `../business` would leave those base lessons describing a build that
no longer exists. Keeping Phase 15's end-state here lets the base build stay in sync with the base course
while still giving you real, compiled, test-passing code for the at-scale variant.

## What differs from `../business`

The cross-module contract `IStockReservation` moves **out of the shared kernel and into the Inventory
module**, and the compiler — not an absent project reference — becomes the wall:

| | `../business` (base) | this build (Phase 15) |
|---|---|---|
| `IStockReservation` lives in | `Shared/Erp.Shared.Common/Contracts/` | `Modules/Inventory/Contracts/` (namespace `Erp.Modules.Inventory.Contracts`) |
| `StockReservationService` | `public` | **`internal`** |
| Sales → Inventory reference | none (wall = absent reference) | Sales references the Inventory **assembly**; sees only the `public` contract |
| Inventory → `Erp.Shared.Common` | referenced | dropped (Inventory owns its contract now) |
| Test access to internals | not needed | `InternalsVisibleTo("Erp.Integration.Tests")` in the Inventory csproj |

Everything else — SQL, handlers' logic, DI lifetimes, runtime behavior — is **identical**. The change is
purely about *visibility and ownership of the boundary*.

## Verified

```
dotnet build      → 0 errors
dotnet test       → 21 passing (14 unit + 7 integration, LocalDB)
```

The "wall" itself is proven by a compile-time negative: from inside Sales,
`new Erp.Modules.Inventory.Infrastructure.StockReservationService()` fails with
`error CS0122: 'StockReservationService' is inaccessible due to its protection level`.

## Keeping it in sync

If you change `../business` in a way that touches the Inventory contract, the two Sales handlers that call
it, or the module csprojs, mirror the change here (with the Phase 15 differences above preserved).
