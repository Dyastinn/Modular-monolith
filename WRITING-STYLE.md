# Writing Style for the Lessons

Every lesson in [plan-business-tutorial/](plan-business-tutorial/) is written in this voice. It is adapted
from the study guides of the *Teach me Driving* course — the instructor talking to one learner — and
written in **English only**.

**Read this whole page before writing or editing a lesson, every time.** Then, before calling the lesson
done, go through [the checklist at the end](#before-you-call-a-lesson-done). A lesson whose code is correct
but whose style is off is not finished: the course is only as good as it is readable.

What each part of this page applies to:

| Page | The voice and the lesson shape | The format rules and the teaching rules |
|---|---|---|
| A numbered lesson (`01-…md` to `25-…md`) | Yes, all of it | Yes |
| A companion page (`00-START-HERE`, `00a-FOUNDATIONS`, `GLOSSARY`, `LEARNING-RESOURCES`) | The voice, yes. The nine-part shape, no: a reference page is organized by what the reader looks up | Yes |
| `ANSWER-KEY.md` | Plain answers, in the same friendly "you" | Yes |

## The reader

Write for one person: a working developer, often coming from **WebForms**, who is following the course step
by step, alone, and typing every line. They are smart and they are new to this. They have not met DDD, CQRS,
dependency injection, `async`/`await`, EF Core or a race condition until a lesson has taught it. They cannot
ask you a question, so anything a lesson leaves unsaid stays unknown.

## The voice

A friendly instructor explaining something to one person sitting beside them. Warm, plain, patient. Not a
textbook, not a blog post, not a chatbot.

- **Open with a greeting** and what we'll do today, then invite them in:
  *"Good day, developer! Today we give our Sales module its memory: a real PostgreSQL database. Are you
  ready? Let's begin!"*
- **Talk to "you", and walk together as "we" and "let's":** *"Now let's talk about…"*, *"Let's look at…"*,
  *"Here's what happens when…"*
- **Name a thing, then say what it does:** *"This is the validation filter. It runs every validator
  before the endpoint, so a bad request never reaches the handler."*
- **Check in after a hard part** — once or twice a lesson, not after every paragraph: *"Easy, right?"*,
  *"Clear so far?"*, *"Not so hard, right?"*
- **Give a memory aid where one helps**, starting with *"To remember it easily:"* — *"To remember it
  easily: the aggregate writes the letter, the outbox is the mailbox, the dispatcher is the post office."*
- **Say costs and caveats in the voice, as a sentence**, never as a box: *"Now, let's be honest about the
  cost here: …"*, *"One thing to watch out for: …"*
- **For readers coming from WebForms**, say it plainly: *"If you came from WebForms, this is your
  `[WebMethod]`."*
- **Close warmly:** *"And that's where this lesson ends. Next time, we … Good luck, developer!"*

### The same thing, wrong and right

| Don't write | Write | Why |
|---|---|---|
| "Note: validators must be registered with `includeInternalTypes: true`." | "One thing to watch out for: our validators are `internal`, and FluentValidation skips those unless we pass `includeInternalTypes: true`." | A label is not a sentence, and the reason is missing |
| "In this lesson, we will dive into the domain model." | "Good day, developer! Today we give the Sales module its rules. Are you ready? Let's begin!" | The greeting says what today builds |
| "The handler leverages the DbContext to persist the aggregate." | "The handler loads the order, asks it to submit itself, and saves." | Plain verbs, in the order things happen |
| "As you know, EF Core tracks changes." | "EF Core remembers every object it loaded, and on save it writes only what changed. That is **change tracking**." | Never assume; name the term where it is first needed |
| "In summary, we added a validator, a handler and an endpoint." | *(nothing: end the section on its last real point)* | Don't repeat what the reader just read |
| "A large order needs approval." | "An order over ₱50,000 waits for a `SalesManager`." | Real numbers and real names from this domain |

## The teaching rules

These decide *what* a lesson says and *when*. They are the same rules as CLAUDE.md §2, written out here so
this page stands on its own.

1. **Assume nothing, explain once.** Teach each concept one time, in the lesson that first needs it, in the
   fewest words that make it land. Later lessons link back to it; they don't explain it again.
2. **Progressive.** A lesson uses only what earlier lessons established. Never mention an API the reader
   hasn't met, and never say "as you know." Every lesson ends in something the reader can run and check.
3. **No phantom APIs.** Every type and method shown is defined right there or in an earlier lesson, with its
   DI registration. Nothing the reader needs is "left as an exercise."
4. **Pain first, then the fix, briefly.** Show the obvious version and why it hurts in a few lines, then the
   better one. Don't stage a long drama for a small point.
5. **Say why.** One or two sentences of reason for each decision, not only the steps.
6. **Be honest about costs.** Name what each pattern costs, in the voice: *"Now, let's be honest about the
   cost here: …"*
7. **Prove it with tests.** From lesson 02 on, every lesson tests what it built. See
   [Tests in every lesson](#tests-in-every-lesson).
8. **The course never ends on a shortcut.** A lesson may start with a simpler version to teach an idea. A
   later lesson replaces it, and says so.
9. **Lean.** If a paragraph can lose half its words and keep its meaning, it should. Burying the point under
   explanation fails the reader as surely as skipping the reason.

## The shape of every lesson

In this order:

1. **Greeting intro** — greeting, what we'll do today, "Are you ready? Let's begin!"
2. **What we'll build** — the concrete result.
3. **Why we build it this way** — the obvious approach first, why it hurts, then the fix.
4. **The steps** — numbered `##` sections with the code. From lesson 02 on, the last numbered step is the
   tests, titled **"Prove it: …"** (for example *"5. Prove it: the customer's edges"*).
5. **How a request flows** — a short trace or a Mermaid diagram.
6. **Mistakes to avoid** — the traps, as short bullets.
7. **Let's run it!** — the checkpoint: commands and the output to expect.
8. **Let's check!** — recall questions. **No answers shown**; they live in
   [ANSWER-KEY.md](plan-business-tutorial/ANSWER-KEY.md). Answering from memory is what makes it stick.
9. **Closing line**, then the previous/next links.

The heading names above are used exactly as written (*What we'll build*, *Why we build it this way*, *How a
request flows*, *Mistakes to avoid*, *Let's run it!*, *Let's check!*), so every lesson looks the same to a
reader who comes back to look something up. A lesson with no request to trace (setup, deployment) leaves out
*How a request flows*; nothing else is optional.

## Tests in every lesson

- **Which tests appear in full:** the ones that teach something new. Show the whole test, then say what it
  proves.
- **Every other edge case goes in a table:** the test's name, and why that case matters. The tests
  themselves are in the reference build.
- **Each kind of test is set up in the lesson that first needs it**, and explained there: unit, architecture,
  property-based, integration, browser, race, coverage, mutation.
- **Make each test fail once.** A test that has never failed proves nothing. Tell the reader what to break,
  what the failure looks like, and to put it back.
- **Every test named in a lesson exists in the reference build**, with the same name.

## Format rules

- **No `>` blockquote callouts and no emoji markers** (no 💡 ⚠️ ❓ 🔑 🧭 📦). Say it as a sentence in the
  voice instead.
- **No terse labels** like "In one line:", "Memory tip:", "Heads-up:", "Note:", "Tip:", "TL;DR". They don't
  sound human.
- **No stock openers or closers:** never "In today's fast-paced world", "Let's dive in", "It's important to
  note that", "By leveraging", "In conclusion". Never end a section by summarizing what the reader just read.
- **Bold sparingly:** a key term the first time it appears, or the one fact to memorize. Never whole
  sentences, never bold everywhere.
- **Tables** for anything to compare or memorize: who does what, where logic lives, statuses, roles.
- **Short paragraphs, short bullets.** Vary sentence length. Let some points be one clause.
- **Code:** complete files, with the file's path as a comment on the first line.
- **Diagrams in Mermaid.** An image (in [plan-business-tutorial/images/](plan-business-tutorial/images/))
  only when Mermaid can't express it, such as a screenshot of a screen.
- **Links** to other lessons and files as Markdown links, never bare paths and never a path in backticks.
- **American spelling** (behavior, license, organize, color), matching the code.
- **Concrete numbers and real names from this domain:** `SalesOrder`, `SalesManager`, ₱50,000. Not "an
  entity", "a user", "some amount".
- **One analogy per idea, used consistently:** the outbox dispatcher is a post office; a module is an island with one
  service window; vertical slices are a taco drawer; a value object is any ₱1,000 bill; atomicity is a
  turnstile. Don't invent a second analogy for an idea that already has one.

## Write to be read

These keep a lesson easy to follow for someone typing along.

- **One idea per paragraph.** When a paragraph starts a second idea, it becomes two paragraphs.
- **Short sentences for hard ideas.** If a sentence needs a second comma to hold together, split it.
- **A new term is defined in the sentence that introduces it**, in bold, once. After that, always use the
  same word for it. Don't call one thing a "handler" here and a "use case" there.
- **Say what a code block is for before it appears**, in one sentence. After it, explain only the lines the
  reader couldn't have guessed. Don't walk through every line.
- **Never show code the reader can't place.** Each block says which file it belongs to, and whether the file
  is new or being changed.
- **A change to an existing file shows what changed.** Show the before and the after, or the whole new file,
  so the reader never has to guess what to delete.
- **Steps happen in the order the reader does them.** If a command must run before the code compiles, it
  comes first.
- **Say what to expect.** After a command, show the output the reader should see, so they know it worked.
- **Lists have a reason.** Use bullets for things that are parallel, numbers for things done in order, and a
  sentence for everything else.

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

## Before you call a lesson done

Go through every line. Each one is a yes or a no.

**The voice**

- [ ] It opens with a greeting, what we'll do today, and "Are you ready? Let's begin!"
- [ ] It speaks to "you" and walks as "we" and "let's" from start to finish.
- [ ] It checks in once or twice ("Easy, right?"), not after every paragraph.
- [ ] Every cost and caveat is a sentence in the voice.
- [ ] It closes warmly, with what comes next and "Good luck, developer!"

**The shape**

- [ ] The nine parts are there, in order, with the exact heading names (*How a request flows* only where
      there is a request to trace).
- [ ] From lesson 02 on, the last numbered step is "Prove it: …", with the new tests in full and a table of
      the edge cases.
- [ ] "Let's check!" shows no answers, and each answer is in
      [ANSWER-KEY.md](plan-business-tutorial/ANSWER-KEY.md).
- [ ] The previous and next links work.

**The teaching**

- [ ] Nothing appears that the reader hasn't met: no type, method, package, tool or config option.
- [ ] Each new thing says what it is, why it's needed here, and what problem it solves.
- [ ] The obvious approach and its pain come before the fix.
- [ ] Each decision has its reason, and each pattern its cost.
- [ ] A replaced approach is shown before and after, and the old one is removed.
- [ ] The lesson ends in something the reader can run, with the output to expect.

**The format**

- [ ] No `>` callouts, no emoji markers, no terse labels, no stock openers, no summary endings.
- [ ] Bold only on a term's first appearance or the one fact to memorize.
- [ ] Every code block says which file it's in; every file reference in prose is a Markdown link.
- [ ] Diagrams are Mermaid. American spelling throughout.
- [ ] The numbers and names are real ones from this domain.
- [ ] No second analogy for an idea that already has one.

**The match with the build**

- [ ] The code in the lesson is the code in
      [course-validation/business/](course-validation/business/), and it builds.
- [ ] Every test the lesson names exists there, with the same name, and passes.

**Lean**

- [ ] Read it once more and cut every sentence the reader wouldn't miss.
