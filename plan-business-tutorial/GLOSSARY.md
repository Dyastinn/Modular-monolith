# Glossary — Every Term in Plain English

Keep this open while you read the lessons. Terms are grouped by topic, so related ideas sit together. Each
one has a plain meaning, the lesson where it first appears (in brackets), and sometimes an everyday picture
to hold on to. The glossary grows with the course: each lesson's new words arrive with the lesson.

---

## Your tools

**SDK** (01)
*Software development kit.* For .NET, the `dotnet` command plus the compiler and runtime: everything needed
to create, build, test and run an app.

**Package manager** (01)
A program that installs other programs with one command and keeps them up to date: **winget** on Windows,
**Homebrew** on macOS, **dnf** on Fedora, **apt** on Ubuntu.

**Terminal** (01)
The window where you type commands. This course uses Bash: **Git Bash** on Windows, Terminal on macOS and
Linux.

**PATH** (01)
The list of folders a terminal searches for programs. It's read when the terminal opens, which is why a
freshly installed tool needs a new terminal.

**Git** (01)
Keeps every saved version of your code. A **commit** is one saved version, with a message saying what
changed. A **repository** is a folder whose history Git keeps.

**NuGet package** (01)
A library someone published for .NET, downloaded by `dotnet` when a project asks for it.

**Central package management** (01)
Each project says *which* packages it uses; one file, `Directory.Packages.props`, says *which version*.

## The big architecture words

**Monolith** (01)
One program you deploy and run, as opposed to many separate services talking over a network.

**Modular monolith** (01)
One program, split inside into modules with real walls between them. *An island with one service window.*

**Module** (01)
One area of the business with its own code and its own data: Tickets, Sla, Knowledge, Identity. Everything
inside is `internal`.

**Microservices** (01)
Each area as its own program, talking over the network. Real walls, but every call between areas can fail
on the way.

**Vertical slice architecture** (01)
One folder per feature, holding everything that feature needs. *The taco drawer.*

**BuildingBlocks** (01)
The shared project every module may use: base classes and small interfaces.

**Infrastructure** (01)
The shared project holding the real implementations (database connection, current user). Only the host
references it.

**Host** (01)
The one program that runs: `Yasda.Api`. It plugs the modules in and serves the browser app.

**Dependency injection** (01)
Classes ask for what they need in their constructor, and the app hands it to them from its list of services
(`IServiceCollection`).

## The browser app

**Blazor Web App** (01)
Microsoft's shape for a .NET web app: the server sends the page, and the screens inside it run where you
choose.

**WebAssembly** (01)
A way for browsers to run compiled code. Blazor uses it to run .NET, and our C#, inside the browser.

**Render mode** (01)
Where a screen runs. Ours is interactive WebAssembly: in the browser.

**Prerendering** (01)
The server drawing a screen first, before the browser takes over. Switched off here, because our screens are
API data the server would fetch twice.

**Router** (01)
Reads the address and shows the page whose `@page` line matches.

**Layout** (01)
The frame around every page. The WebForms master page.

**Landmark** (01)
An HTML element that says what part of the page it is (`<header>`, `<nav>`, `<main>`), so screen readers and
tests can jump straight to it.

**Custom property** (01)
A named value in CSS (`--link: #0069C2`), used elsewhere as `var(--link)`. Our whole look is a handful of
them.

**SVG** (01)
A picture written as text. Sharp at any size, tiny to download. Our logo.

**Static assets** (01)
The files the server sends unchanged: the app itself, `app.css`, the logo. `MapStaticAssets` serves them,
compressed and cached.

**Fingerprint** (01)
A short code in a file's name (`app.tl0z91yx0a.css`) that changes whenever the file does, so a browser never
uses an old copy.

## Testing

**Test** (02)
A check written down once and run forever.

**Arrange, Act, Assert** (02)
The three parts of a test: get ready, do the one thing, check what happened.

**Unit test** (02)
Tests one rule or class alone. No server, no database. *One bolt, on the bench.*

**Integration test** (02)
Tests the real API, started in memory, with real requests. *The engine, on a stand.*

**End-to-end test** (02)
A real browser clicking through the real app. *A test drive.*

**Test pyramid** (02)
Many fast unit tests, fewer integration tests, a few end-to-end tests.

**Fact / Theory** (02)
In xUnit, a `[Fact]` is one test; a `[Theory]` is one test run with several sets of inputs (`[InlineData]`).

**Fixture** (02)
Something expensive that several tests share, created once: for us, the started app.

**WebApplicationFactory** (02)
Starts our real app inside the test process, with no port, and hands out clients wired straight to it.

**Microsoft.Testing.Platform** (02)
The test runner .NET 10 uses; `global.json` tells `dotnet test` to use it.

**Edge case** (02)
An input at the border of what's allowed: empty, at the limit, twice, at the same instant.

**Regression test** (02)
A test run again after every change; in the narrow sense, one written after a bug so the bug can't come
back.

**Smoke test** (02)
A few quick checks that the app starts and its front page loads.
