# Answer Key — Every "Let's check!" Question

These are model answers, not the only right ones. If you said the same thing in your own words, you're
fine. What matters is whether you named the *reason*, because in this course the reason is the lesson.

Try each question first, out loud or on paper, and only then come here. Getting an answer wrong and then
seeing why plants it far deeper than reading the right one. Look first, and you get the warm feeling of
understanding without the understanding.

The course grows with the product, so this page grows with it: each lesson's answers arrive with the lesson.

**Jump to:** [01](#lesson-01--solution-setup) · [02](#lesson-02--testing)

---

## Lesson 01 — Solution setup

**1. Why doesn't `Yasda.Modules.Tickets` reference `Yasda.Shared.Infrastructure`? What could go wrong if it did?**
Infrastructure holds the real implementations: the PostgreSQL connection, the code that reads the user from
the HTTP request. Without the reference, Tickets physically can't reach a connection string or `HttpContext`;
it only sees the small interfaces in BuildingBlocks. With it, a handler could grab the raw request or open
its own connection, and every module would be tied to those details.

**2. You add a sixth shared project, and every module references it. What does that new project boundary
enforce?**
Nothing. A project boundary exists to make something a compile error. If every module may reference it, it
forbids nothing, and it should have been a folder in BuildingBlocks.

**3. How many files change when you upgrade a package the whole solution uses?**
One: `Directory.Packages.props`. Every project takes its version from there.

**4. Why vertical slices instead of `Controllers`, `Services` and `Repositories` folders?**
Because a change is almost always about one feature. With slices, everything for "file a ticket" sits in one
folder, like one drawer holding everything for tacos. With technical folders, one feature is spread over four
places, and shared "services" quietly tie features together.

**5. Why Blazor WebAssembly, and what does it cost on the first visit?**
One language and toolchain for the whole product, C# in the browser talking to our own API, and a server
that only answers API calls instead of holding a live connection per browser. The cost: the first visit
downloads the .NET runtime and the app, a few megabytes, so it takes a second or two to start. After that
it's cached.

**6. Why does the API live under `/api`, and what does `GET /api/typo` answer?**
The app wants page addresses like `/tickets` for itself, and the API wants addresses for data; one prefix
keeps them apart. `GET /api/typo` hits the API group's own fallback and answers 404, never the app's HTML.

**7. Why does YASDA use no component library, and what do we pay for that?**
DESIGN.md's look is plain HTML: if HTML has an element for something, we use that element. A component
library would bring its own designer's look, which we'd spend our time undoing, and a major version that
moves things on every upgrade. Without one, there's nothing to upgrade but .NET, and every page reads in order
with the CSS off. The price: what HTML doesn't have, like a sortable table or a status line, we write
ourselves, once.

**8. `dotnet --version` says "command not found" right after installing it. What's the most likely reason?**
The terminal was opened before the install. A terminal reads the list of program folders (the `PATH`) when it
starts, so it doesn't know about anything installed later. Close it and open a new one.

## Lesson 02 — Testing

**1. What are the three parts of a test, and what does each one do?**
Arrange gets everything ready (a client, a ticket, a user). Act does the one thing being tested. Assert
checks what happened. One act per test, so a failure points at one behavior.

**2. Why do we break the code on purpose after writing a test?**
A test that has never failed might not be able to fail: it could check the wrong thing, or nothing. Seeing it
go red when the code breaks, and green when it's fixed, is the only proof it guards what its name says.

**3. What does `WebApplicationFactory` do, and why doesn't it need a port?**
It runs our real `Program.cs` inside the test process, with an in-memory server instead of a network
listener. `CreateClient()` returns an `HttpClient` wired straight to that server, so requests go through the
real middleware and routing without touching the network.

**4. When would you write a `[Theory]` instead of a `[Fact]`?**
When the same check should hold for several inputs: every HTTP method, every boundary value. One theory with
`[InlineData]` rows instead of copies of one test, and each row reported on its own.

**5. Pick three rows of the edge-case checklist and give an example of each for a ticket.**
For example: *nothing*, a ticket with an empty or whitespace-only subject; *twice*, two agents clicking "Take"
on the same ticket; *who*, a requester opening someone else's ticket (404); *when*, a reply to a ticket that
was closed a moment ago. Any three with a concrete ticket example are right.

**6. What is a regression test? Name the one in this lesson and the bug it guards against.**
Any test rerun after a change to prove nothing broke; in the narrow sense, a test written after a bug so it
can't come back. `The_apps_own_files_are_served` guards against the `ReferenceOutputAssembly="false"`
reference that once left the app without its stylesheet.

**7. Why should most tests be unit tests, and only a few drive a browser?**
Unit tests run in microseconds, never flake, and a failure names one rule. Browser tests need a running app,
take seconds each, and break for reasons unrelated to the rule (timing, layout). Test rules underneath; keep a
few browser tests for the main flows.

**8. A test files a ticket through the API and reads it back, without a browser. Which kind, and what can't it
catch?**
An integration test: the API, the handlers, the rules and the database all run for real, but no screen does.
It can't catch a bug in the page itself: a button that calls the wrong address, a status shown in the wrong
place, a form that never sends the priority. That's what the end-to-end tests are for.
