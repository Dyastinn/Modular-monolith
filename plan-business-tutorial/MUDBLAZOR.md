# MudBlazor: How This Course Uses It, and How to Test It

Every screen in this course is built from **MudBlazor** components. This page gathers what the lessons teach
about it in one place:

- what MudBlazor is;
- the rules we follow;
- the pieces we built on top of it;
- the mistakes we made and fixed;
- how to test MudBlazor screens with Playwright.

Read it once after [lesson 01](01-solution-setup.md), and come back whenever a lesson adds a component. Each
section names the lesson where a piece first appears, and nothing here is needed before then.

## What MudBlazor is

MudBlazor is a library of ready-made Blazor components: buttons, fields, tables, menus, dialogs, layouts. They
follow Google's **Material Design**, a well-known set of rules for how screens look and behave. You write
`<MudButton>` instead of styling a `<button>` yourself, and it comes with:

- the look;
- keyboard handling;
- dark mode;
- the accessibility attributes screen readers need.

| | |
|---|---|
| License | MIT: free, including for the buyer of this source ([lesson 01](01-solution-setup.md) explains why not Telerik, Syncfusion or DevExpress) |
| Version | 9.12.0, pinned in `Directory.Packages.props` |
| Docs | mudblazor.com: every component with live examples and its full list of parameters |
| Offline docs | The package ships XML documentation, so your editor's IntelliSense shows each parameter's description and default |
| Changes between versions | The release notes and migration guides on MudBlazor's GitHub. Read them before moving to a new major version |

> **Tip.** mudblazor.com is itself a Blazor app. When you need an exact default value, hover over the parameter
> in your editor: it reads the same XML documentation the site does.

## How it's wired in (lesson 01)

| Piece | Where | What it does |
|---|---|---|
| `MudBlazor` package | `Erp.Web` | The components |
| `MudBlazor.min.css`, `MudBlazor.min.js` | `App.razor` | MudBlazor's styles and the bit of JavaScript some components need (popovers, focus) |
| `AddMudServices(...)` | `Erp.Web/Program.cs` | Registers dialogs, snackbars and the rest; also sets where snackbars appear (bottom right, [lesson 07](07-create-order.md)) |
| `@using MudBlazor` | `_Imports.razor` | So every page can say `<MudButton>` |
| `MudThemeProvider` | `MainLayout.razor` | Applies `AppTheme`, and dark mode |
| `MudPopoverProvider` | `MainLayout.razor` | The layer where anything that pops up is drawn: menus, the lists of selects, tooltips |
| `MudSnackbarProvider` | `MainLayout.razor` ([lesson 07](07-create-order.md)) | Where short messages ("Draft saved.") appear |
| `MudDialogProvider` | `MainLayout.razor` ([lesson 08](08-find-orders.md)) | Where dialogs appear |

Each provider appears exactly once. A missing provider doesn't fail loudly: the dialog or message simply never
shows. If one doesn't appear, check the providers first.

## The house rules

| Rule | In practice | Why |
|---|---|---|
| **MudBlazor components, not HTML** | `MudStack`, `MudText`, `MudPaper`, `MudDataGrid`, never `<div>`, `<table>`, `<b>` | One look and one way of working everywhere. Components bring dark mode, spacing and accessibility for free; raw HTML needs each one added by hand |
| **MudBlazor's components as they are, never subclassed** | `MudTextField`, `MudDataGrid`, with their settings on the tag | What you read in MudBlazor's documentation is exactly what's on the page. No layer of ours sits between the two |
| **The same settings on the same kind of component** | Every field: `Variant="Variant.Outlined" Margin="Margin.Normal"`. Every grid: `Striped Hover Dense`, `SortMode.None`, and `Outlined` unless it sits in a frame | One look across the app. A field that forgets them looks different, which is how review catches it |
| **Utility classes before CSS** | `Class="pa-4 mb-2 d-flex gap-4"` | MudBlazor's spacing and layout classes cover nearly everything. `app.css` holds a few rules, each with a comment saying why MudBlazor can't do it |
| **The theme owns colour** | `Color="Color.Primary"`, `mud-error-text` | No colour codes in pages, so dark mode works everywhere |
| **Name what has no visible name** | `AriaLabel="Actions"` on an icon-only `MudMenu`, `aria-label="Remove line"` on an icon button | A screen reader announces the name, and a test finds the button by it |
| **No gradients, little shadow** | `Shadows` levels 1–5 are `none` in `AppTheme` | A flat, calm look; dialogs and menus keep their shadow so they float above the page |

**When HTML is right anyway.** `MudElement` draws one specific HTML element when no component means the same
thing. `Crumbs` uses it to wrap the breadcrumb in a `<nav>` landmark. The only other HTML is `App.razor`, the
host page that loads the app before any component exists.

## What we built with it

Our own components *use* MudBlazor components; none of them extends one.

| Piece | File | Lesson | What it does |
|---|---|---|---|
| `AppTheme` | `Layout/AppTheme.cs` | 01 | Colours (light and dark), fonts, corners, the flat shadows |
| `Brand` | `Shared/Brand.razor` | 01 | The "E · ERP" mark, on the bar and on the sign-in card |
| `PasswordField` | `Shared/PasswordField.razor` | 06 | A `MudTextField` with an eye button to show what was typed |
| `PageHeader` | `Shared/PageHeader.razor` | 08 | The top of every page: breadcrumb, title, status, actions (wrapping on a phone), description |
| `GridPaging.FirstPage()` | `Shared/GridPaging.cs` | 08 | An extension method: back to page 1 of a `MudDataGrid` after a filter changes |
| `MoneyColumn<T>` | `Shared/MoneyColumn.razor` | 08 | A `TemplateColumn` for pesos: right-aligned; an optional footer `Total`; `Warn` for red |
| `Totals`, `TotalLine` | `Shared/` | 08 | The label-and-amount lines under an order, invoice or purchase order |
| `StatusChip` | `Shared/StatusChip.razor` | 08 | One colour per status; `Live` makes it a `status` region |
| `Crumbs` | `Shared/Crumbs.razor` | 08 | The breadcrumb, inside a `nav` landmark |
| `Tile` | `Shared/Tile.razor` | 08 | A home-page card that is one big link |

### Why no wrappers around MudBlazor's own components

An earlier version of this course had `AppTextField : MudTextField` and friends: MudBlazor's classes with our
defaults set in their constructors. We took them out. The reasons:

- **Two layers to read.** To know what a field does, you read our class, then MudBlazor's documentation. With the
  settings on the tag, there's one place.
- **Our component inside our component.** The password field had to be built *around* our text field, which was
  built *on* MudBlazor's: a wrapper of a wrapper, for a box with a border.
- **MudBlazor's own rule.** Its contributor guidelines say a component must never overwrite its own parameters.
  A subclass that sets defaults is close to that line, and two of ours crossed it (the password field flipped its
  own `InputType`; the money column set its own footer after the fact).
- **What MudBlazor recommends.** Version 9 removed the global input defaults (`MudGlobal.InputDefaults`). Its
  migration guide lists explicit component parameters first, then theme tokens or wrapper components.

What we do instead, by kind:

| You want | Use | Example |
|---|---|---|
| The same look on every component of a kind | The settings on each tag | `<MudTextField ... Variant="Variant.Outlined" Margin="Margin.Normal" />` |
| Behaviour MudBlazor doesn't have, used on several pages | A component of our own that **holds** MudBlazor's | `PasswordField`, `PageHeader`, `MoneyColumn` |
| A helper method for a MudBlazor component | An **extension method** | `_table.FirstPage()` |
| A look for the whole app | The theme | `AppTheme` |

Before you make a component, apply the rule of three: the third time the same markup is copied, it becomes a
component. Before that, copying is cheaper than a guess at what will repeat.

## The components, by job

| Job | Component | First in |
|---|---|---|
| The frame: top bar, side menu, page area | `MudLayout`, `MudAppBar`, `MudDrawer`, `MudMainContent`, `MudNavMenu`, `MudNavLink` | 01 |
| Text and headings | `MudText Typo="Typo.h4"` (a real `<h4>`), `Typo.body2`, `Typo.caption` | 01 |
| Rows and columns of things | `MudStack Row="true"` (with `Breakpoint` to stack on phones), `MudGrid` + `MudItem` | 01, 08 |
| A card | `MudPaper Outlined="true"` | 06 |
| Buttons and links | `MudButton`, `MudIconButton`, `MudLink` | 01 |
| A message that stays | `MudAlert` | 01 |
| A message that comes and goes | `ISnackbar` (through `ApiClient`) | 07 |
| A table | `MudDataGrid` with `PropertyColumn`, `TemplateColumn`, `MoneyColumn`, `MudDataGridPager` | 07, 08 |
| A page's title row | `PageHeader` (ours) | 08 |
| A status or tag | `MudChip` (through `StatusChip`) | 08 |
| A question before acting | `IDialogService.ShowMessageBoxAsync` | 08 |
| A small form in a window | `MudDialog`, opened with `IDialogService.ShowAsync<T>` | 10 |
| A menu of actions | `MudMenu` + `MudMenuItem` | 06 |
| Loading | `MudProgressLinear Indeterminate="true"` | 06 |
| Tabs | `MudTabs` + `MudTabPanel` | 17 |
| On/off | `MudSwitch` | 16 |

### Grids in one minute

```razor
<MudDataGrid T="SalesOrderSummary" ServerData="Load" RowClick="Open"
             Outlined="true" Striped="true" Hover="true" Dense="true" SortMode="SortMode.None">
    <Columns>
        <PropertyColumn Property="x => x.OrderNumber" Title="Order" />
        <TemplateColumn Title="Status"><CellTemplate><StatusChip Status="@context.Item.Status" /></CellTemplate></TemplateColumn>
        <MoneyColumn Title="Total" Amount="x => x.Total" />
    </Columns>
    <NoRecordsContent>No orders match.</NoRecordsContent>
    <PagerContent><MudDataGridPager T="SalesOrderSummary" /></PagerContent>
</MudDataGrid>
```

| Need | Use |
|---|---|
| Data already in memory | `Items="_lines"` |
| Data paged by the API | `ServerData="Load"`, a method taking `GridState<T>` and a `CancellationToken` and returning `GridData<T>` |
| A property as it is | `PropertyColumn` |
| Anything else in a cell | `TemplateColumn`; the row is `context.Item` |
| An amount | `MoneyColumn` |
| A total under a column | `MoneyColumn Total="..."` (the grid's footer row) |
| Free text that may be long | `CellClass="wrap"` (cells don't wrap otherwise) |
| A click on a row | `RowClick`, with `RowStyle="cursor:pointer"` |
| Phones | Nothing: below `Breakpoint.Xs` each row becomes a card, labelled with each column's `Title` |

## Mistakes we made, so you don't

| Mistake | What happened | The fix |
|---|---|---|
| A button inside `MudMenu`'s `ActivatorContent` | The menu never opened, and a button sat inside a button | Use `MudMenu`'s own `Label`, `StartIcon`, `EndIcon` ([lesson 06](06-sign-in.md)) |
| `aria-label="Actions"` on `MudMenu` | It landed on the wrapper; the ⋮ button had no name | `AriaLabel="Actions"`, the component's own parameter ([lesson 16](16-users-email.md)) |
| `MudSimpleTable` for small tables | Raw `<tr>`/`<td>` HTML in the middle of a MudBlazor screen | `MudDataGrid` with `Items`, and `Totals` under it |
| Subclassing MudBlazor components for defaults (`AppTextField : MudTextField`) | Two layers to read; a wrapper of a wrapper for the password field; components rewriting their own parameters | The settings on each tag, and our own components that hold MudBlazor's |
| `Margin.Dense` on every field | Labels and text crowded together | `Margin="Margin.Normal"` on every field |
| `MudDatePicker` as it comes | Dates could only be picked from a calendar, never typed | `Editable="true"` on every date field |
| The dialog provider added after the first dialog | The question never appeared | Each provider in `MainLayout.razor` before the first page needs it |
| Text-aligning a grid heading | The heading stayed on the left | A grid heading is a flex box: `justify-content` (`app.css`) |

## Upgrading MudBlazor

1. Read the release notes and, for a new major version, the migration guide, before changing the version.
2. Change the one line in `Directory.Packages.props`.
3. Build. MudBlazor ships its own analyzer, on by default: rule `MUD0002` warns about an attribute a component
   doesn't have. It catches a misspelled parameter (`Colr="Color.Primary"`), and after an upgrade it points at
   parameters that were renamed or removed, with a hint saying what replaced them. (Lowercase attributes such as
   `role` or `aria-label` pass on purpose: they're HTML attributes for the element underneath.) What it can't catch
   is a setting that's simply *missing*, like a field without `Variant="Variant.Outlined"`; that one shows on
   screen.
4. Run the browser tests. They find what the build can't: a dialog that no longer opens, a button that lost its
   name. This is a big reason they find things by role and not by MudBlazor's CSS classes. The classes may change
   in any version; the roles are part of what MudBlazor promises screen-reader users.
5. Look at the screenshots (`ERP_UI_SCREENSHOTS`) for anything that moved.

## Testing MudBlazor screens with Playwright

[Lesson 06](06-sign-in.md) sets up Playwright and its five rules. In short:

1. Find things by **role and name**, the way a person (and a screen reader) does.
2. Check with **`Expect`**, which retries, never by reading a value once.
3. Never wait for a **clock**.
4. One person, one **browser context**.
5. Keep a **trace** of every failure.

This section is the part specific to MudBlazor: what each component looks like to Playwright, checked against
this app.

### What each component is, to Playwright

| Component | Find it with | Drive it | Check it |
|---|---|---|---|
| `MudButton`, `MudIconButton` | `GetByRole(AriaRole.Button, new() { Name = "Save draft" })`; an icon button by its `aria-label` | `ClickAsync()` | `ToBeEnabledAsync()`, `ToBeDisabledAsync()` (e.g. while `Api.Busy`) |
| `MudTextField`, `MudNumericField` | `GetByLabel("Email")` | `FillAsync("...")` | `ToHaveValueAsync("...")` |
| Password field | `GetByLabel("Password", new() { Exact = true })` | `FillAsync` | `Exact`, because "Show password" also contains the word |
| `MudSelect` | `GetByRole(AriaRole.Combobox, new() { Name = "Status" })` | `ClickAsync()`, then `GetByRole(AriaRole.Option, new() { Name = "Draft" }).ClickAsync()` | `ToContainTextAsync("Draft")`: once a value is chosen, the box is no longer a text input, so check its text, not its value |
| `MudAutocomplete` | `GetByRole(AriaRole.Combobox, new() { Name = "Customer" })` | `FillAsync("Eps")`, then click the `Option` | `ToHaveValueAsync("Epsilon Retail")` |
| `MudDatePicker` (`Editable`) | `GetByLabel("As of")` | `FillAsync("12/31/2026")`, then `PressAsync("Tab")`: the date counts when you leave the field | `ToHaveValueAsync("12/31/2026")` |
| `MudSwitch` | `GetByRole(AriaRole.Switch, new() { Name = "Overdue only" })` | `CheckAsync()`, `UncheckAsync()` | `ToBeCheckedAsync()` |
| `MudDataGrid` | `GetByRole(AriaRole.Row).Filter(new() { HasText = "SO-2026-000012" })`; headings are `Columnheader` | `ClickAsync()` on the row (with `RowClick`) | `ToContainTextAsync(...)`; the footer row is `GetByRole(AriaRole.Row).Last` |
| `MudDataGridPager` | `GetByRole(AriaRole.Button, new() { Name = "Next page" })` (also "First page", "Previous page", "Last page") | `ClickAsync()` | The address: `ToHaveURLAsync(new Regex("page=2"))` |
| `MudMenu` | The button by its `Label` or `AriaLabel` | Click it, then `GetByRole(AriaRole.Menuitem, new() { Name = "Disable" })` | |
| `MudDialog`, message boxes | `GetByRole(AriaRole.Dialog, new() { Name = "Leave without saving?" })` (named by its title) | Click a button *inside* it | `ToBeHiddenAsync()` once answered |
| Snackbar | `GetByRole(AriaRole.Alert)` | | `ToContainTextAsync("(IDENTITY-0003)")` |
| `MudAlert` | Only with `role="alert"` (our error alerts have it) | | `ToHaveTextAsync(...)` |
| `MudTabs` | `GetByRole(AriaRole.Tab, new() { Name = "Categories" })` | `ClickAsync()` | `ToHaveAttributeAsync("aria-selected", "true")` |
| `MudProgressLinear` | `GetByRole(AriaRole.Progressbar)` | | `ToHaveCountAsync(0)`: loading has finished |
| `MudText Typo="Typo.h4"` | `GetByRole(AriaRole.Heading, new() { Level = 4 })` | | `ToHaveTextAsync(...)` |
| `MudNavMenu` | `GetByRole(AriaRole.Navigation)` | Its links: `.GetByRole(AriaRole.Link, ...)` | |
| `StatusChip Live="true"` | `GetByRole(AriaRole.Status)` | | `.Filter(new() { HasText = "Approved" })` + `ToBeVisibleAsync()` |
| `TotalLine` | `GetByRole(AriaRole.Group, new() { Name = "Balance due" })` | | `ToContainTextAsync("₱0.00")` |
| `Crumbs` | `GetByRole(AriaRole.Navigation, new() { Name = "Breadcrumb" })` | Its links | |

Every row of this table was run against the app in this course.

Two rows depend on attributes we added on purpose: `role="status"` on `StatusChip` and `role="group"` on
`TotalLine`. When a test can't find something by its role, that's usually an accessibility gap, not a reason to
reach for a CSS class. Fix the screen, and both the test and screen-reader users benefit.

### Scope before you click

A screen often has two things with the same name:

- the menu's "Sales orders" and the breadcrumb's;
- the page's **Ship** button and the confirmation dialog's **Ship** button.

Playwright is **strict**: a locator that matches two elements refuses to click. Don't reach for `.First`. Say
which one you mean by starting from the part of the page it's in:

```csharp
await page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = "Ship" }).ClickAsync();
await page.GetByRole(AriaRole.Navigation, new() { Name = "Breadcrumb" }).GetByRole(AriaRole.Link, new() { Name = "Sales orders" }).ClickAsync();
await page.GetByRole(AriaRole.Row).Filter(new() { HasText = "admin@erp.local" }).GetByRole(AriaRole.Button, new() { Name = "Actions" }).ClickAsync();
```

### Waiting the right way

| Instead of | Write | Because |
|---|---|---|
| `await page.WaitForTimeoutAsync(1000)` | `await Expect(locator).ToBeVisibleAsync()` | It waits exactly as long as needed, and fails with a message saying what was missing |
| `WaitForLoadStateAsync(LoadState.NetworkIdle)` | Wait for the heading, then `Expect(progressbars).ToHaveCountAsync(0)` | Playwright's docs discourage network-idle; what matters is what's on the screen |
| `(await x.IsVisibleAsync()).Should().BeTrue()` | `await Expect(x).ToBeVisibleAsync()` | `IsVisibleAsync` answers once, right now, maybe before Blazor has drawn it |
| `(await x.InputValueAsync()).Should().Be("a")` | `await Expect(x).ToHaveValueAsync("a")` | Same: it retries until true |
| `x.CountAsync() == 0` | `await Expect(x).ToHaveCountAsync(0)` | |
| `page.Url.Should().EndWith(...)` | `await Expect(page).ToHaveURLAsync(new Regex(...))` | The address may still be changing |

There is one place a test must retry by hand: the page itself won't change until it's reloaded, for example an
invoice made in the background. Even then it waits on what's on the screen, never on a clock (lesson 20):

```csharp
await page.GotoAsync(url);
await Expect(row.Or(page.GetByText("No invoices match."))).ToBeVisibleAsync();   // the list has finished loading
if (await row.IsVisibleAsync()) return;                                          // now reading once is safe
```

### Blazor WebAssembly, and why there's no "wait until ready"

This app runs in the browser and doesn't **prerender**, so until it has started there's nothing on the screen to
click, and every locator waits. With server prerendering (a common Blazor setting), the page appears first and
becomes clickable a moment later. A click in that gap is lost. In such an app, a test first waits for something
only the live page shows, such as data loaded from the API.

Also remember that **each `GotoAsync` restarts the app**: it loads WebAssembly again, and it signs in again from
the cookie. That's a second or two each time. Navigate by clicking where a person would; use `GotoAsync` to
start a test or to reach a page directly.

### When a test fails

1. **Read the message.** `Expect` says what it was waiting for: `Locator expected to be visible ...
   GetByRole(AriaRole.Row).Filter(new() { HasText = "SO-2026-000038" })`.
2. **Open the trace.** Every failed test leaves one under `bin/Debug/net10.0/playwright-traces/`, one file per
   person in the test:
   ```bash
   pwsh tests/Erp.Web.UiTests/bin/Debug/net10.0/playwright.ps1 show-trace <file>.zip
   ```
   You can step through every action and see the page as it was. The network tab shows what the API answered,
   and the console tab shows what the app logged.
3. **Watch it run.** `ERP_UI_HEADED` shows the browser window, slowed down, and `--filter-method` runs just one test:
   ```bash
   ERP_UI_URL=http://localhost:5264 ERP_UI_HEADED=1 dotnet test --project tests/Erp.Web.UiTests --filter-method "*Leaving_an_unsaved*"
   ```
4. **Let Playwright suggest a locator.** `playwright.ps1 codegen http://localhost:5264` opens a browser that
   writes C# as you click. Its locators are a good first guess, and it prefers roles the same way we do. Copy
   the locator, not the whole script.

### A checklist for a new screen's test

- [ ] Every control the test touches is found by role and name, or by label.
- [ ] Every check is an `Expect`.
- [ ] No `WaitForTimeoutAsync`, no `NetworkIdle`, no MudBlazor CSS classes.
- [ ] Two controls with the same name? The locator starts from the dialog, row or landmark around the right one.
- [ ] The test creates the data it needs, or needs none. It doesn't depend on another test having run first.
- [ ] The screen's address is in `Every_page_opens_without_an_error`.

## Sources

- MudBlazor documentation, mudblazor.com, and the package's XML documentation (the reference for every parameter
  and default used here).
- Playwright for .NET: "Locators" (the recommended order, and why CSS selectors aren't recommended), "Assertions"
  (auto-retrying, 5-second default), "Trace viewer", "Test runners" (one browser context per test), at
  playwright.dev/dotnet.
- MudBlazor v9.0.0 migration guide (GitHub issue #12666) and PR #12141, "MudGlobal: Remove deprecated theming
  properties": why the global input defaults were removed, and what to use instead.
- W3C WAI-ARIA Authoring Practices: the Breadcrumb pattern (a `nav` landmark named "Breadcrumb") and the `status`
  and `alert` roles.
- WCAG 2.2, 4.1.3 Status Messages: a status that changes is announced without moving focus.
