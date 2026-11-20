# DESIGN.md

> Plain on purpose. Every element earns its place, or it's removed.

This is the design spec for every page in the project. One look everywhere: warm paper, black serif text, blue underlined links, black rules, one column. The design comes from what's left out.

For agents and developers: every rule here is a requirement. Where this file is silent, follow WCAG 2.2 AA, then pick the plainer option. Don't add a page type, color, or component that isn't here without updating this file first.

---

## 1. Principles

**When rules conflict:** the user can finish their task > plain standard HTML > minimal. Minimalism never justifies a worse experience. If a rule blocks usability, break it and leave a one-line code comment saying why.

1. **Content is the interface.** No decoration that doesn't carry information.
2. **The user always knows where they are, what they can do, and what happened.** Current page is marked, actions look like actions, every action gets visible feedback.
3. **Most urgent first.** Every page puts the thing the user most likely needs to act on at the top.
4. **Borrow proven defaults.** Underlined blue links, serif text, black rules. Don't invent what's already solved.
5. **Standard structure.** Headings, links, rules, lists, tables, forms. If plain HTML has an element for it, use that.
6. **Subtract before adding.** Before adding a component, ask: would a heading, a link, a list, or a table do?

---

## 2. Tokens

### Color

| Token | Value | Use |
|---|---|---|
| `--bg` | `#FEF1CD` | Page background (warm paper) |
| `--surface-input` | `#FFFFFF` | Inside text inputs only |
| `--text` | `#000000` | All body text |
| `--text-muted` | `#333333` | Secondary notes, low priority, paused states |
| `--link` | `#0069C2` | Links, focus outline, selected-row marker |
| `--visited` | `#551A8B` | Visited links |
| `--error` | `#B00020` | Error text and borders; SLA breached; priority Critical |
| `--risk` | `#7A4A00` | SLA at risk only |
| `--line` | `#000000` | Rules, table borders, input and button borders |
| `--row-hover` | `#FAEBC2` | Table row hover background only |

That's the whole palette. No accent colors, gradients, or tinted surfaces.

- **Color is never the only signal.** Every colored state also has its word ("Error: …", "Breached 40m ago", "Critical").
- **Success has no color.** Say it in bold text: "Saved."
- **Only errors, SLA state, and priority get color.** Never ticket status, category, team, or tags.

Contrast (WCAG) on `--bg` / on `--row-hover`: text 18.7 / 16.9, muted 11.2 / 10.7, link 4.9 / 4.7, visited 9.8 / 9.2, error 6.5 / 6.2, risk 6.6 / 6.3. All pass AA. Don't lighten `--link` or darken `--row-hover`: either change makes links fail 4.5:1.

Set `color-scheme: light` so browsers don't darken form controls.

### Typography

| Token | Value |
|---|---|
| `--font` | `Georgia, "Times New Roman", serif` (everything, tables included) |
| `--font-mono` | `ui-monospace, Menlo, Consolas, monospace` (`code`/`pre` only) |
| `--size-body` | `1rem` (never below 16px, tables included) |
| `--size-h1` / `h2` / `h3` | `2rem` / `1.5rem` / `1.17rem`, bold |
| `--line-height` | `1.5` |

- No web fonts.
- Emphasis = **bold**. No colored, uppercase, or letter-spaced text (except the colored states in §2 Color).
- Links are always underlined.
- One `h1` per page. Don't skip heading levels.

### Spacing & shape

| Token | Value |
|---|---|
| `--max-width` | `900px` (the page column) |
| `--max-width-wide` | `1200px` (List pages only, §4.3) |
| `--measure` | `70ch` (prose and lists) |
| `--gap` | `0.5rem` / `1rem` / `2rem` (only these three) |
| `--target` | `2.75rem` (44px minimum height for anything clickable) |
| `--marker` | `4px solid` (left edge of a table row, §5.2) |
| `--radius` | `0` |
| `--shadow` | `none` |
| `--border` | `1px solid var(--line)` |

---

## 3. Page shell

Every page uses the same shell. No exceptions.

```
[logo] Site Name                    Home · Link · Link · Sign out
──────────────────────────────────────────────────────────────────
h1 Page title (count)                         status line
...page content...
```

- **Single column**, centered on the page at `--max-width` (List pages: `--max-width-wide`). Content inside the column is left-aligned.
- **Header**: one line. Small logo (~32px, black line art) and site name on the left, `<nav>` on the right, wrapping below on narrow screens. Then a full-width rule.
- **No sidebar, app bar, drawer, or footer chrome.**
- **Nav** shows the links for the user's role. Same links, same order, on every page. Current page is bold plain text with `aria-current="page"`, not a link. Queue counts go in parentheses as text: "Unassigned (7)".
- **Status line**: one `role="status"` element to the right of the `h1` (below it on narrow screens). It's inline text, replaced by the next message. It doesn't float, slide, or disappear on a timer.
- Generous top margin (`2rem`).
- Prose and lists cap at `--measure` and sit at the left edge of the column.
- Works at 320px wide with no sideways page scroll: images get `max-width: 100%`, body text gets `overflow-wrap: break-word`, and wide tables and `pre` scroll inside their own box (`overflow-x: auto`).
- At most two levels of navigation. Saved views are links on their page, not a second nav bar.

---

## 4. Page types

Every page is one of these. Pick the type first, then build.

### 4.1 Home

One job: **show what needs this user right now, then the fastest way to act on it.** Same URL for everyone; content depends on role. Sections are ordered by urgency. Empty sections are hidden, except the first, which shows its empty state: "Nothing needs you right now."

**Requester home**

```
h1 How can we help?
   Search the knowledge base [__________________] [Search]
   [Submit a ticket]  [Check my tickets]
h2 Waiting on you (1)         ← tickets that need the requester's reply
h2 Your open tickets (2)      ← table: ID, Subject, Status, Updated
h2 Popular articles           ← list of links
```

**Agent home**

```
h1 Good morning, Dyst                          status line
h2 Needs you now (3)          ← table: assigned to me AND (breached, at risk, or requester replied)
   Show all in My queue
h2 Queues                     ← one line of links with counts
h2 Recently updated on your tickets  ← list
```

- "Needs you now" shows at most 10 rows, then a "Show all in My queue" link.
- No charts or summary tiles on Home. Those go in Reports.
- The greeting is the only non-functional text allowed, and only one line.

### 4.2 Article (knowledge base, help pages)

- `h1`, then a one-line summary, then the body at `--measure`.
- "Last updated" date under the `h1` in `--text-muted`.
- Ends with: "Didn't fix it? Submit a ticket" link.

### 4.3 List (queues, search results, reports tables)

```
h1 My queue (24)                               status line
   Filters: [Status ▾] [Priority ▾] [Team ▾] [Apply] [Clear]
   Showing: Open, High or Critical. Clear filters
   3 selected. Assign to… · Change status… · Close     ← only when rows are selected
┌──┬──────┬───────────────────┬──────────┬──────────────────┬─────────┐
│☐ │ ID   │ Subject           │ Priority │ SLA              │ Updated │
└──┴──────┴───────────────────┴──────────┴──────────────────┴─────────┘
   3 new tickets. Show them
```

- Uses `--max-width-wide`. The only page type that does.
- Filters, bulk actions, and live updates: §6.

### 4.4 Detail (a ticket, a record)

- **Header block** at full column width: `h1` with ID and subject, then one line of facts as text ("Status: Open · Priority: **High** · SLA: 1h 10m left · Assigned: Dyst"), then action buttons.
- **Thread** below at `--measure`: each message is a `h3` (author, time), then the text, separated by rules. Internal notes start with the bold word "Internal note:".
- Reply form at the bottom (§5.4).

### 4.5 Form (submit a ticket, settings)

- `h1`, one sentence on what happens after submit, then the form at `--measure`.
- One column of fields. Submit button at the bottom, left-aligned.

---

## 5. Components

| Need | Use | Not |
|---|---|---|
| Navigation | `<nav>` with a row of underlined links | App bar, sidebar, drawer, tabs, hamburger |
| Section break | Horizontal rule (`<hr>`) or a heading | Cards, colored bands |
| Grouped data | Bordered table | Card grids, zebra stripes |
| Emphasis | Bold text | Badges, chips, pills, colored labels |
| Identity | Small line-art logo in the header (~32px) | Hero image, banner |
| Actions | Link if it navigates, `<button>` if it changes something | Filled, raised, rippling buttons |
| Feedback | Status line text (§7) | Toasts, snackbars, modals |

### 5.1 Tables

- `border-collapse: collapse`, `--border` on every cell, `0.5rem` padding.
- Bold header row (`<th scope="col">`), left-aligned. Right-align numeric columns (Georgia's numerals don't line up otherwise).
- Name the table with a heading above it or a `<caption>`.
- Wrap every table in a scrolling box with `role="region"`, an `aria-label` or `aria-labelledby`, and `tabindex="0"`.
- Row hover: `--row-hover` background, no transition. No zebra stripes.
- Sticky header row only on List pages.
- The **subject or name cell is the link**. Never make a whole row clickable.
- Long text truncates with `text-overflow: ellipsis`; full text stays in the link.
- Times are relative ("12m", "3h", "Yesterday") in a `<time datetime="…">` with the absolute time as its `title`.

### 5.2 Status: SLA and priority

| State | Text | Style |
|---|---|---|
| SLA breached | "Breached 40m ago" | `--error`, bold; row gets a `--marker` left edge in `--error` |
| SLA at risk | "1h 10m left" | `--risk`, bold; row gets a `--marker` left edge in `--risk` |
| SLA OK | "6h left" | Plain |
| SLA paused | "Paused" | `--text-muted` |
| Priority Critical | "Critical" | `--error`, bold |
| Priority High | "High" | Bold |
| Priority Normal | "Normal" | Plain |
| Priority Low | "Low" | `--text-muted` |

- "At risk" threshold: [SET FROM SLA POLICY, e.g. under 25% of time left or under 1h].
- The left-edge marker is the only colored shape in the app. No background fills for status.
- SLA times update once a minute. No ticking seconds.
- Requesters see ticket status only ("In progress"), never SLA timers.

### 5.3 Buttons

- Plain: `--border`, transparent background, `--text` color, at least `--target` tall, `0 1rem` padding.
- Verb-phrase label that says what happens: "Save changes", "Assign to me", not "Submit" or "OK".
- Prefer enabled buttons that explain what's missing on click over disabled buttons.
- A link that starts a main task ("Submit a ticket") may be styled as a button: same border and size, no underline.

### 5.4 Forms

- Visible label above every input. Placeholder is never the label.
- Inputs are at least `--target` tall, `font: inherit`, `--surface-input` background, `--border`, no radius, no shadow, no floating labels.
- Mark optional fields "(optional)", not required ones.
- Native controls only: `<select>`, `<input type="checkbox">`, `<input type="date">`. No custom dropdowns.
- Validate on submit. Show a summary at the top starting with "Error:" and move focus to it; mark each field `aria-invalid` with its message beside it. Keep what the user typed.
- Focus: 2px `--link` outline, 2px offset. That's the only "effect" allowed. Never remove it.

### 5.5 Icons

Allowed only in table columns too narrow for a word (attachment, internal note, waiting on requester).

- At most 6 icons in the whole app. One meaning each, everywhere.
- Inline SVG, 16px, `currentColor`, single stroke, no fill or color.
- Every icon has an accessible name.
- A one-line legend sits below any table that uses them, each icon followed by its word.
- Never in nav, buttons, headings, or prose. Never emoji.

---

## 6. Working with lists

### 6.1 Filters

- A plain `<form method="get">` above the table: native `<select>`s, a search input, "Apply" and "Clear".
- Filters live in the URL query string, so every view can be bookmarked, shared, and reloaded.
- Active filters are summarized as text: "Showing: Open, High or Critical. Clear filters". No chips.
- Search runs on submit, not on every keystroke.

### 6.2 Bulk actions

- Checkbox column on the left; the header checkbox selects all visible rows.
- When rows are selected, one line appears directly above the table: "3 selected. Assign to… · Change status… · Close".
- Mass and destructive actions confirm inline: "Close 3 tickets? Yes, close 3 · Cancel".

### 6.3 Live updates

- **Never insert, remove, or reorder rows under the user's cursor.** New items show as a line under the table header: "3 new tickets. Show them". The user decides when to refresh.
- If someone else changes the record you have open: "Updated by Ana 1m ago. Reload" at the top, before you can save over it.
- No sounds, flashing, or animated badges.

### 6.4 Keyboard

- Everything works with Tab and Enter alone. Shortcuts are extras.
- If shortcuts exist: `j` / `k` move the selected row, `Enter` opens, `x` toggles its checkbox, `/` focuses search, `?` opens a plain page listing them all.
- Shortcuts are off while focus is in a text field, and users can turn them off (WCAG 2.1.4).
- The selected row gets a `--marker` left edge in `--link` and `aria-selected="true"`. A breached or at-risk marker wins; selection then shows as the focus outline.

---

## 7. Feedback

- Every action gets visible text feedback in the status line: "Saved.", "Assigned 3 tickets to Dyst.", "Error: …". Errors use `role="alert"`.
- Loading: the text "Loading…". No spinners or skeletons.
- Empty: one sentence saying what's empty and what to do next, with a link or button to do it.
- Errors say what happened and how to fix it.
- Destructive actions name the thing ("Delete invoice 1042") and need an inline confirm step.

---

## 8. Don'ts

- No shadows, elevation, or rounded corners.
- No animations, transitions, or ripple effects.
- No sidebars, drawers, modals, toasts, popovers, or carousels.
- No icons where a word works. No emoji in the UI.
- No dark mode. A deliberate tradeoff; revisit if users ask.
- No more than two levels of navigation.
- No removed focus outlines.
- No component library defaults left unreviewed. Strip them back to these tokens.

---

## 9. Checklist before shipping a page

- [ ] Which page type (§4) is it? Does it follow that type?
- [ ] Is the most urgent thing at the top?
- [ ] Only colors, fonts, and gaps from §2? Color only on errors, SLA, and priority?
- [ ] Can breached, at-risk, and Critical be told apart in grayscale?
- [ ] Usable at 320px wide and with a keyboard alone?
- [ ] Every clickable target at least 44px tall?
- [ ] Every action has text feedback; empty, loading, and error states exist?
- [ ] Do filtered views survive a reload and a copied URL?
- [ ] Any element I could delete without losing information or usability? Delete it.