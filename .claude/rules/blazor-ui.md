---
paths:
  - "**/*.razor"
  - "**/*.css"
  - "course-validation/business/src/Yasda.Web/**"
  - "course-validation/business/tests/Yasda.Web.UiTests/**"
---

# Screen rules (plain Blazor + DESIGN.md)

Read this and [DESIGN.md](../../DESIGN.md) before writing or changing any `.razor` or `.css` file, in the
reference build **or inside a lesson**. DESIGN.md says how a page looks; this file says how it's built.
Decided 2026-11-19 (user): **no component library**. MudBlazor was removed with the ERP.

## The look

- **Plain HTML, styled by one file:** `Yasda.Web/wwwroot/css/app.css`. Its `:root` tokens are DESIGN.md §2, and
  nothing else defines a color, font, gap, radius or shadow. No inline `style=` (the CSP has no
  `'unsafe-inline'`), no CSS framework, no icon font, no dark mode.
- **If HTML has an element for it, use that element:** `<h1>`, `<nav>`, `<table>`, `<hr>`, `<label>`, `<button>`,
  `<a>`. A `<div>` only to group fields or actions (`.field`, `.actions`, `.search`, `.table-box`).
- **Every page reads correctly with CSS switched off** (DESIGN.md §6): order the markup the way it should read.
- Emphasis is `<strong>`. A status is a word, bold when it needs attention, never a colored badge.
- **Packages:** none for the UI. Microsoft's own Blazor packages only. QuickGrid is allowed (user, 2026-11-19)
  but not used: lists are plain tables with link-based sort and paging (below).

## Layout

- `MainLayout` draws DESIGN.md's triangle: `Masthead` (logo, name, tagline), the `Main` nav row, a short rule, the
  `Account` nav row, a full rule, then `<main>` with the `NoticeArea` and the page.
- Navigation is rows of links (`NavMenu`). No app bar, drawer, tabs, hamburger or tooltip.
- Every page starts with `PageHeader`: the trail (a page under a list), the `<h1>` title with its status, a
  description, and the page's actions as links or buttons.
- A detail page's trail goes back to its list as it was left: `ListMemory.Last("users")`; the list calls
  `Memory.Remember(...)` when it loads.

## Forms

- `EditForm` + Blazor's own inputs (`InputText`, `InputSelect`, `InputCheckbox`), each in a `.field` with its
  `<label for>` **above** it. A hint is a `.hint` span linked by `aria-describedby`.
- Passwords use `PasswordField` (a Show/Hide button whose accessible name names the field).
- Submit buttons are `type="submit"` and `disabled="@Api.Busy"` while a change is on its way.
- A form with unsaved changes asks before it's left: `NavigationLock` + `UnsavedChanges.ConfirmLeave` (the
  browser's own confirm box).

## Lists

- A list is a `<table>` inside `.table-box` (it scrolls sideways on a phone; the page never does), with `<th
  scope="col">`. Empty: a sentence ("No users match."), not an empty table.
- **The list's state lives in the address**: search, filters, `sort`, `desc`, `page`, read with
  `[SupplyParameterFromQuery]` and loaded in `OnParametersSetAsync`. A search is a form whose submit navigates to
  the new address with `page` cleared.
- **Sorting is on the server**: each sortable column is a `SortHeader` (a link that sets `?sort=`, then
  `&desc=true`). The API checks the name with `ValidSort` against an allow-list of the columns' JSON names; the
  SQL function orders with one `CASE` per column and direction, never text built into SQL. A log or a document's
  lines have no sort headers.
- Paging is the `Pager` component: Previous · Page n of m · Next, as links.

## Messages and confirmations

- `ApiClient` reports every failure to `Notices`; `NoticeArea` shows it as `role="alert"` with a link to the
  error code's page. Successes are `role="status"`. A message survives the navigation a save makes by itself
  and clears when the person follows a link.
- No modal dialogs. An action that can't be undone (disable, close, cancel) asks **inline** first, naming the
  record and the consequence, with verb buttons ("Disable" / "Not yet"). Editing a record is its own page.

## Browser tests

- Find things by **role and name** and check with Playwright's `Expect`. Never CSS classes, `WaitForTimeout`,
  `NetworkIdle`, or read-once checks.
- Each person gets their own browser context. A failed test keeps a trace.
- If a control can't be found by role, fix its accessibility (a `<label>`, `aria-label`), don't reach for CSS.
- Run the app for browser tests with `RateLimiting__AuthRequestsPerMinute` raised (every test signs in).
