---
paths:
  - "**/*.razor"
  - "course-validation/business/src/Erp.Web/**"
  - "course-validation/business/tests/Erp.Web.UiTests/**"
  - "plan-business-tutorial/MUDBLAZOR.md"
---

# Screen rules (Blazor + MudBlazor)

Read this before writing or changing any `.razor` file, in the reference build **or inside a lesson**. These
rules were moved here from CLAUDE.md §5, unchanged in meaning. The reader-facing version, with the reasons and
the Playwright recipes per component, is
[plan-business-tutorial/MUDBLAZOR.md](../../plan-business-tutorial/MUDBLAZOR.md).

## The look

- **The look lives in the theme** (`Erp.Web/Layout/AppTheme.cs`: light and dark palettes, typography, flat
  elevations 1–5), not in CSS. Pages ask for roles (`Color.Primary`), never color codes. `app.css` holds only
  what the theme can't say, each rule with its reason.
- Dark mode follows the system unless the person picks one (kept in `localStorage`). No gradients.
- Build screens from MudBlazor components and its utility classes, **never raw HTML** (no `<div>`, `<table>`,
  `<b>`; `MudElement` only for a landmark like `<nav>`).
- **Never subclass MudBlazor components** (no `AppTextField : MudTextField`). Use them as they are, with the
  house settings written on each tag. Shared behavior goes in components that *hold* MudBlazor's, or in
  extension methods.

## Fields

- Every field (`MudTextField`, `MudNumericField`, `MudSelect`, `MudAutocomplete`, `MudDatePicker`) has
  `Variant="Variant.Outlined" Margin="Margin.Normal"`. Date fields also have `Editable="true"`.
- Password fields are our `PasswordField` (show/hide).
- Rows of fields use `Breakpoint="Breakpoint.Xs"` so they stack on a phone.

## Tables

- **Every table is `MudDataGrid`** with `Striped="true" Hover="true" Dense="true"`, and `Outlined="true"`
  unless it sits in a framed tab or card. Never `MudTable` or `MudSimpleTable`.
- **Lists sort.** A paged list is `SortMode="SortMode.Single" ShowColumnOptions="false"` (the column menu's
  hide and filter don't fit a server list, and its hidden button would push headers off their numbers).
- A paged list sorts **on the server**: the query's `Sort`/`Desc`, checked by `ValidSort` against an allow-list
  of the columns' JSON names. The SQL function orders with one `CASE` per column and direction, never text
  built into SQL.
- Each sortable column restores its arrow with `InitialDirection="@View.Initial(nameof(Row.Prop))"`. A column
  the API can't sort says `Sortable="false"`.
- A small unpaged report grid sorts in the browser. A document's lines (order, invoice, PO) and a log stay
  `SortMode.None`, in their own order.
- `PropertyColumn` (with a `CellTemplate` when it needs one) for anything sortable, `TemplateColumn` otherwise.
- Amounts are `@(MoneyColumn.Of<Row>("Title", x => x.Amount, …))`, which writes the `PropertyColumn` in place
  (a column wrapped in a component would render last).
- The column `Title` labels values on phones. Cells don't wrap; free text opts in with `CellClass="wrap"`.
- Each grid that can be empty has `NoRecordsContent`.
- Totals under lines use `Totals` + `TotalLine`.
- Row actions go in a "⋮" `MudMenu` with `AriaLabel`.

## Pages

- Every page's top row is `PageHeader` (title, trail, status, actions, description).
- Every page under a list (a detail or edit page) has a breadcrumb back to it (`PageHeader Trail=`, drawn by
  `Crumbs`, a `nav` landmark around `MudBreadcrumbs`), via `ListMemory.Last`.
- A detail page's status is `StatusChip Live="true"`.
- A list keeps its search, filters, sort, page and page size in the address (`ListAddress`). A filter change
  returns to page 1 (`GridPaging.FirstPage`, an extension method).

## Actions

- Buttons that send a change are `Disabled="Api.Busy"`.
- An action that can't be undone (ship, close short, cancel) asks first, naming the record and the
  consequence, with verb buttons ("Ship" / "Not yet").
- A form with unsaved changes asks before it's left (`NavigationLock` + `UnsavedChanges`).

## Browser tests

- Find things by **role and name** and check with Playwright's `Expect`. Never MudBlazor CSS classes,
  `WaitForTimeout`, `NetworkIdle`, or read-once checks.
- Each person gets their own browser context. A failed test keeps a trace.
- If a control can't be found by role, fix its accessibility (`AriaLabel`, `role`); don't reach for CSS.
- Recipes per component are in MUDBLAZOR.md.
