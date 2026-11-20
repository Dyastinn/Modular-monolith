# Third-party notices

YASDA includes the open-source components below. Every one uses a permissive license (MIT,
Apache-2.0, BSD or the PostgreSQL license) that allows commercial use, modification and redistribution of
source and binaries. None charges per developer, per seat or by revenue. Versions are pinned in
[Directory.Packages.props](Directory.Packages.props).

**Rule for contributors:** before adding or upgrading a package, check the license *of that version*: projects
change licenses (PDFsharp moved to AGPL in 6.5). Only permissive licenses go in, and each one gets a row here in
the same change.

## Shipped with the application

| Component | License | Used for |
|---|---|---|
| .NET runtime and ASP.NET Core (`Microsoft.*`, `System.*`) | MIT | Platform, web host, authentication |
| Blazor (Microsoft.AspNetCore.Components.WebAssembly, .Server, .Authorization) | MIT | The browser app (a Blazor Web App running as WebAssembly) and its hosting |
| Microsoft.EntityFrameworkCore (+ Relational) | MIT | Writes and transactions |
| Npgsql | PostgreSQL | PostgreSQL driver |
| Npgsql.EntityFrameworkCore.PostgreSQL | PostgreSQL | EF Core provider for PostgreSQL |
| EFCore.NamingConventions | Apache-2.0 | snake_case table and column names |
| Dapper | Apache-2.0 | Read queries |
| FluentValidation (+ DependencyInjectionExtensions) | Apache-2.0 | Request validation |
| ASP.NET Core Identity (Microsoft.AspNetCore.Identity.EntityFrameworkCore) | MIT | Sign-in, cookies, bearer + refresh tokens, lockout, password hashing |
| Microsoft.AspNetCore.DataProtection.EntityFrameworkCore | MIT | Persisting (certificate-encrypted) the keys that protect cookies and tokens |
| MailKit / MimeKit | MIT | Email over SMTP (password resets; ticket notifications later) |
| BouncyCastle.Cryptography (via MimeKit) | MIT | Cryptography used by MimeKit |
| OpenTelemetry (.Extensions.Hosting, .Instrumentation.AspNetCore/Http/Runtime, .Exporter.OpenTelemetryProtocol) | Apache-2.0 | Traces, metrics and logs |
| Npgsql.OpenTelemetry | PostgreSQL | Database spans and metrics |
| Microsoft.AspNetCore.OpenApi | MIT | The API description (/openapi/v1.json) |
| Scalar.AspNetCore 2.18.7 | MIT | A page to browse and try the API (development only) |

## Used only to build and test (not shipped)

| Component | License |
|---|---|
| Microsoft.EntityFrameworkCore.Design, dotnet-ef | MIT |
| xUnit v3 (xunit.v3), running on Microsoft.Testing.Platform | Apache-2.0 |
| AwesomeAssertions | Apache-2.0 |
| FsCheck, FsCheck.Xunit.v3 (property-based tests) | BSD-3-Clause |
| Microsoft.AspNetCore.Mvc.Testing | MIT |
| Microsoft.Extensions.TimeProvider.Testing (FakeTimeProvider) | MIT |
| Microsoft.Playwright (browser tests; drives the browser already installed) | MIT |
| Testcontainers, Testcontainers.PostgreSql (a throwaway PostgreSQL per test run) | MIT |
| coverlet.MTP (code coverage) | MIT |
| dotnet-reportgenerator-globaltool (coverage report) | Apache-2.0 |
| dotnet-stryker (mutation testing) | Apache-2.0 |
| Podman (containers on developer machines and in CI-like runs) | Apache-2.0 |
| Aspire (Aspire.AppHost.Sdk, dashboard, orchestrator) — development dashboard only | MIT |

## Server software whoever installs YASDA runs (not part of this source)

| Component | License |
|---|---|
| PostgreSQL 16+ (`postgres:18` image) | PostgreSQL |
| .NET ASP.NET Core runtime images (`mcr.microsoft.com/dotnet/aspnet`) | MIT |
| Caddy (`caddy:2` image): HTTPS for the container install | Apache-2.0 |
| Podman on the server (the course's path) | Apache-2.0 |
| docker-compose v2, the Compose program `podman compose` hands the file to | Apache-2.0 |
| Docker Engine, if preferred (the same files run unchanged) | Apache-2.0. Docker *Desktop* is a developer tool, free only for small businesses; servers don't need it |

## Deliberately NOT used

| Package | Why |
|---|---|
| MediatR | Commercial license required for larger organizations since 2025. (No mediator is used at all: endpoints call their handlers directly.) |
| FluentAssertions ≥ 8 | Paid license required for commercial use |
| AutoMapper | Commercial license since 2025 |
| QuestPDF | Free tier is revenue-gated, which would bind every user above the threshold |
| PDFsharp / MigraDoc ≥ 6.5 | AGPL-3.0 with a runtime license check (if PDFs are ever needed, 6.2.4 is the last MIT release) |
| Duende IdentityServer | Free tier is revenue-gated |
