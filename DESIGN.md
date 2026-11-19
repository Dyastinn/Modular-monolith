# DESIGN.md

> Plain on purpose. Every element earns its place, or it's removed.

Inspired by hand-coded personal homepages: standard structure, one column, warm paper, blue links. The design comes from what's left out.

For agents: every rule here is a requirement. Where this file is silent, follow WCAG 2.2 AA, then pick the plainer option.

---

## 1. Principles

**When rules conflict:** the user can finish their task > plain standard HTML > minimal. Minimalism never justifies a worse experience. If a rule blocks usability, break it and leave a one-line comment saying why.

1. **Content is the interface.** No decoration that doesn't carry information.
2. **The user always knows where they are, what they can do, and what happened.** Current page is marked, actions look like actions, every action gets visible feedback.
3. **Borrow proven defaults.** Underlined blue links, serif text, black rules. Don't invent what's already solved.
4. **Standard structure.** Headings, links, rules, tables. If plain HTML has an element for it, use that.
5. **Subtract before adding.** Before adding a component, ask: would a heading, a link, or a table do?

---

## 2. Tokens

### Color

| Token | Value | Use |
|---|---|---|
| `--bg` | `#FEF1CD` | Page background (warm paper) |
| `--text` | `#000000` | All body text |
| `--text-muted` | `#333333` | Taglines, secondary notes (use sparingly) |
| `--link` | `#0069C2` | Links, focus outline |
| `--visited` | `#551A8B` | Visited links |
| `--error` | `#B00020` | Error text and error borders only |
| `--line` | `#000000` | Rules, table borders, input borders |

That's the whole palette. No accent colors, no gradients, no tinted surfaces. Success has no color: say it in bold text ("Saved."). Color is never the only signal; errors always start with the word "Error".

Contrast on `--bg` (WCAG): text 18.7, muted 11.2, link 4.9, visited 9.8, error 6.5. Link barely passes AA; don't lighten it.

Set `color-scheme: light` so browsers don't darken form controls.

### Typography

| Token | Value |
|---|---|
| `--font` | `Georgia, "Times New Roman", serif` |
| `--font-mono` | `ui-monospace, Menlo, Consolas, monospace` (`code`/`pre` only) |
| `--size-body` | `1rem` (never below 16px) |
| `--size-h1` / `h2` / `h3` | `2rem` / `1.5rem` / `1.17rem`, bold |
| `--line-height` | `1.5` |

- No web fonts.
- Emphasis = **bold**. No colored, uppercase, or letter-spaced text.
- Links are always underlined.

### Spacing & shape

| Token | Value |
|---|---|
| `--max-width` | `900px` (page, tables, code) |
| `--measure` | `70ch` (prose and lists) |
| `--gap` | `0.5rem` / `1rem` / `2rem` (only these three) |
| `--target` | `2.75rem` (44px minimum height for anything clickable) |
| `--radius` | `0` |
| `--shadow` | `none` |
| `--border` | `1px solid var(--line)` |

---

## 3. Layout

Single centered column. No sidebar, no app bar, no footer chrome.

- **Measure follows content.** Prose caps at `--measure`; tables and code use the full `--max-width`.
- Header area (logo, name, tagline, nav) is centered. Everything else is left-aligned. Centered blocks that have a `--measure` cap also need `margin-inline: auto`, or they render off-center.
- Generous top margin (`2rem`). The page breathes from the top.
- Works at 320px wide with no sideways page scroll: images get `max-width: 100%`, body text gets `overflow-wrap: break-word`, and wide tables and `pre` scroll inside their own box (`overflow-x: auto`).

### Example: homepage

```
            [ logo ]
          Site Name
     one-line tagline
   link · link · link · link            ← primary navigation
        ──────────────                  ← short rule (~50%)
 topic · topic · topic · topic · topic  ← secondary navigation (wraps)
──────────────────────────────────────  ← full-width rule
Bold one-line heading
┌──────┬──────────┬──────────┬────────┐
│      │          │          │        │ ← table, full width
└──────┴──────────┴──────────┴────────┘
```

This is one page type, not a template for every page.

---

## 4. Components

| Need | Use | Not |
|---|---|---|
| Navigation | `<nav>` with a row of underlined links | App bar, drawer, tabs, hamburger |
| Section break | Horizontal rule (`<hr>`) | Cards, colored bands |
| Grouped data | Bordered table, collapsed 1px black borders | Cards grid, zebra stripes |
| Emphasis | Bold text | Badges, chips, colored labels |
| Identity | Small line-art logo (~70px, black strokes) | Hero image, banner |
| Actions | Link if it navigates, `<button>` if it changes something | Filled, raised, rippling buttons |

### Navigation
- Each link is at least `--target` tall. Padding alone won't get there; use `min-height` with `inline-flex`.
- Current page is bold plain text with `aria-current="page"`, not a link.
- Same links, same order, on every page.

### Tables
- Bold header row (`<th>`), `0.5rem` padding, 1px black borders.
- Name the table with a heading above it or a `<caption>`.
- Wrap wide tables in a scrolling box with `role="region"`, an `aria-label`, and `tabindex="0"` so keyboard users can scroll it.
- No hover, no stripes, no sticky headers unless the table is long.

### Buttons
- Plain bordered, at least `--target` tall, with a verb-phrase label ("Save changes", not "Submit").
- Prefer enabled buttons that explain what's missing on click over disabled buttons.

### Forms (when unavoidable)
- Visible label above every input. Placeholder is never the label.
- Inputs are at least `--target` tall, with `font: inherit`, a 1px black border, no radius, no shadow, and no floating labels.
- Mark optional fields "(optional)".
- Validate on submit. Show a summary at the top starting with "Error:" and move focus to it; mark each field `aria-invalid` with its message beside it. Keep what the user typed.
- Focus: link-blue outline. That's the only "effect" allowed.

---

## 5. Feedback

- Every action gets visible text feedback where the user acted: "Saved.", "Deleted 3 items.", "Error: …". Put it in a `role="status"` element (errors: `role="alert"`).
- Loading: the text "Loading…". No spinners or skeletons.
- Empty: one sentence saying what's empty and what to do next, with a link or button to do it.
- Errors say what happened and how to fix it.
- Destructive actions name the thing ("Delete invoice 1042") and need a confirm step.

---

## 6. Don'ts

- No shadows, elevation, or rounded corners.
- No animations, transitions, or ripple effects.
- No icons where a word works.
- No modals, toasts, or carousels. Feedback stays inline.
- No dark mode (one palette, done well). A deliberate tradeoff; revisit if users ask.
- No more than two levels of navigation.
- No removed focus outlines.
- No component library defaults left unreviewed. Strip them back to these tokens.

---

## 7. Checklist before shipping a page

- [ ] Would it read fine with CSS disabled?
- [ ] Only colors, fonts, and gaps from §2?
- [ ] Usable at 320px wide and with a keyboard alone?
- [ ] Every clickable target at least 44px tall?
- [ ] Every action has text feedback; empty, loading, and error states exist?
- [ ] Any element I could delete without losing information or usability? Delete it.