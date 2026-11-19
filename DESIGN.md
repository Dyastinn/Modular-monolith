# DESIGN.md

> Plain on purpose. Every element earns its place, or it's removed.

Inspired by hand-coded personal homepages: standard structure, one column, warm paper, blue links. The design comes from what's left out.

---

## 1. Principles

1. **Content is the interface.** No decoration that doesn't carry information.
2. **Importance flows top-down.** Layout widens as you go down: narrow identity at the top, wide detail at the bottom (the "triangle").
3. **Borrow proven defaults.** Colors and type come from sites already tuned for long reading (Project Gutenberg paper, MDN links). Don't invent what's already solved.
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
| `--link` | `#0069C2` | Links, primary actions |
| `--line` | `#000000` | Rules, table borders |

That's the whole palette. No accent colors, no gradients, no tinted surfaces. Status colors (error/success) only where function demands it.

### Typography

| Token | Value |
|---|---|
| `--font` | `Georgia, "Times New Roman", serif` |
| `--size-body` | `1rem` (16–18px) |
| `--size-title` | `2rem`, bold |
| `--line-height` | `1.5` |

- One font family. No web fonts.
- Emphasis = **bold**. No colored, uppercase, or letter-spaced text.
- Links are always underlined.

### Spacing & shape

| Token | Value |
|---|---|
| `--max-width` | `900px` |
| `--gap` | `0.5rem` / `1rem` / `2rem` (only these three) |
| `--radius` | `0` |
| `--shadow` | `none` |
| `--border` | `1px solid var(--line)` |

---

## 3. Layout: the triangle

Single centered column. Each section is wider than the one above it.

```
            [ logo ]                    ← narrowest: identity
          Site Name
     one-line tagline
   link · link · link · link            ← primary navigation
        ──────────────                  ← short rule (~50%)
 topic · topic · topic · topic · topic  ← secondary navigation (wraps)
──────────────────────────────────────  ← full-width rule
      Bold one-line heading
┌──────┬──────────┬──────────┬────────┐
│      │          │          │        │ ← widest: detail
└──────┴──────────┴──────────┴────────┘
```

- Text is centered in the header area; long-form prose is left-aligned.
- No sidebar, no app bar, no footer chrome. Navigation is a row of links.
- Generous top margin (~2–3rem). The page breathes from the top.

---

## 4. Components

| Need | Use | Not |
|---|---|---|
| Navigation | Row of underlined links separated by spacing | App bar, drawer, tabs, hamburger |
| Section break | Horizontal rule (`<hr>`) | Cards, colored bands |
| Grouped data | Bordered table, collapsed 1px black borders | Cards grid, zebra stripes |
| Emphasis | Bold text | Badges, chips, colored labels |
| Identity | Small line-art logo (~70px, black strokes) | Hero image, banner |
| Actions | Link, or a plain bordered button | Filled, raised, rippling buttons |

### Tables
- `border-collapse: collapse`, 1px solid black on every cell.
- Bold header row, small padding (`0.25rem 0.5rem`).
- No hover, no stripes, no sticky headers unless the table is long.

### Forms (when unavoidable)
- Label above input. 1px black border, no radius, no shadow, no floating labels.
- Focus state: link-blue outline. That's the only "effect" allowed.

---

## 5. Don'ts

- No shadows, elevation, or rounded corners.
- No animations, transitions, or ripple effects.
- No icons where a word works.
- No dark mode (one palette, done well).
- No more than two levels of navigation.
- No component library defaults left unreviewed. Strip them back to these tokens.

---

## 6. Checklist before shipping a page

- [ ] Would it read fine with CSS disabled?
- [ ] Is the most important thing at the top and narrowest?
- [ ] Only colors from §2?
- [ ] Any element I could delete without losing information? Delete it.