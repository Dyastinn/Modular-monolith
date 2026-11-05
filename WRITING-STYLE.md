# Writing Style for the Lessons

Every lesson in [plan-business-tutorial/](plan-business-tutorial/) is written in this voice. It is adapted
from the study guides of the *Teach me Driving* course — the instructor talking to one learner — and
written in **English only**.

## The voice

A friendly instructor explaining something to one person sitting beside them. Warm, plain, patient. Not a
textbook, not a blog post, not a chatbot.

- **Open with a greeting** and what we'll do today, then invite them in:
  *"Good day, developer! Today we give our Sales module its memory: a real PostgreSQL database. Are you
  ready? Let's begin!"*
- **Talk to "you", and walk together as "we" and "let's":** *"Now let's talk about…"*, *"Let's look at…"*,
  *"Here's what happens when…"*
- **Name a thing, then say what it does:** *"This is the `ValidationBehavior`. It runs every validator
  before the handler, so a bad request never reaches it."*
- **Check in after a hard part** — once or twice a lesson, not after every paragraph: *"Easy, right?"*,
  *"Clear so far?"*, *"Not so hard, right?"*
- **Give a memory aid where one helps**, starting with *"To remember it easily:"* — *"To remember it
  easily: the endpoint addresses the envelope, the mediator delivers it, the handler opens it."*
- **Say costs and caveats in the voice, as a sentence**, never as a box: *"Now, let's be honest about the
  cost here: …"*, *"One thing to watch out for: …"*
- **For readers coming from WebForms**, say it plainly: *"If you came from WebForms, this is your
  `[WebMethod]`."*
- **Close warmly:** *"And that's where this lesson ends. Next time, we … Good luck, developer!"*

## The shape of every lesson

In this order:

1. **Greeting intro** — greeting, what we'll do today, "Are you ready? Let's begin!"
2. **What we'll build** — the concrete result.
3. **Why we build it this way** — the obvious approach first, why it hurts, then the fix.
4. **The steps** — numbered `##` sections with the code.
5. **How a request flows** — a short trace or a Mermaid diagram.
6. **Mistakes to avoid** — the traps, as short bullets.
7. **Let's run it!** — the checkpoint: commands and the output to expect.
8. **Let's check!** — recall questions. **No answers shown**; they live in
   [ANSWER-KEY.md](plan-business-tutorial/ANSWER-KEY.md). Answering from memory is what makes it stick.
9. **Closing line**, then the previous/next links.

## Format rules

- **No `>` blockquote callouts and no emoji markers** (no 💡 ⚠️ ❓ 🔑 🧭 📦). Say it as a sentence in the
  voice instead.
- **No terse labels** like "In one line:", "Memory tip:", "Heads-up:", "Note:", "TL;DR". They don't sound
  human.
- **Bold sparingly:** a key term the first time it appears, or the one fact to memorize. Never whole
  sentences, never bold everywhere.
- **Tables** for anything to compare or memorize: who does what, where logic lives, statuses, roles.
- **Short paragraphs, short bullets.** Vary sentence length. Let some points be one clause.
- **Code:** complete files, with the file's path as a comment on the first line. Diagrams in Mermaid.
- **Links** to other lessons and files as Markdown links, never bare paths.
- **American spelling** (behavior, license, organize), matching the code.
- **One analogy per idea, used consistently:** the mediator is a post office; a module is an island with one
  service window; vertical slices are a taco drawer; a value object is any ₱1,000 bill; atomicity is a
  turnstile.

## Introduce each thing only when it's needed

Write for someone following the course step by step. Introduce a package (and pin its version) only in the
lesson where it is first used, never earlier. The same rule applies to every tool, concept, file and config
option, not just packages.

- **One new thing at a time.** Each lesson introduces only what that lesson needs.
- **Explain everything you introduce.** When something new appears, say what it is, why it's needed here,
  and what problem it solves. The reader should never see an import, dependency or config line and wonder
  why it's there.
- **No unused code.** If something isn't used yet, it doesn't appear yet. Don't add packages "for later."
- **Remove what's no longer needed.** If a later lesson replaces a package or approach, say so explicitly
  and remove the old one.

In practice: `Directory.Packages.props` starts empty in lesson 01, and each lesson adds its packages with
`dotnet add package <Name> --version <x>` (which writes the pinned version there), saying what each one is
for.

### Build up, then refactor

When a topic hasn't been covered yet, solve the problem with what the reader already knows, even if it's not
the ideal way. Then, in the lesson that introduces the new package or concept:

1. Point out the limitation of the current approach.
2. Introduce the new package or concept and explain it.
3. Refactor the existing code to use it, showing the before and after.

This way the reader learns *why* each tool exists, not just how to use it.

Because lessons add things as they go, a reader's migration history is longer than the reference build's
(which squashes each module into one `InitialCreate`). Say so once, where it first happens; the resulting
schema is the same.

## What still applies from CLAUDE.md

Pain first, then the fix. Explain each idea once, where it's first needed. Be honest about costs. No
phantom APIs. Cut every word that isn't doing work. Never open with "In today's fast-paced world", "Let's
dive in", "It's important to note", or end a section by summarizing what the reader just read.

## A sample paragraph

> Good day, developer! Last time, our endpoints learned to hand their work to a handler. Today we'll give
> the Sales module its rules: what makes a customer blocked, when an order needs a manager, and why nobody
> can approve their own order. And we'll do it without a database in sight. Are you ready? Let's begin!
>
> Now let's talk about where a rule should live. The easy thing is to write the check right where you load
> the data, inside the handler. That works for the first feature. By the third, the credit check exists in
> three places, and one of them is already different. So instead, we put each rule on the thing it's about.
> Easy, right?
>
> To remember it easily: if the question is "is this allowed?", the answer lives in the domain.

(The `>` here only marks the sample. Lessons themselves don't use blockquotes.)
