# Third-party notices

This product includes the open-source components below. Every one uses a permissive license (MIT,
Apache-2.0, BSD or the PostgreSQL license) that allows commercial use, modification and redistribution of
source and binaries. None charges per developer, per seat or by revenue. Versions are pinned in
[Directory.Packages.props](Directory.Packages.props).

**Rule for contributors:** before adding a package, check its license. Only permissive licenses go in, and
each one gets a row here in the same change.

## Shipped with the application

| Component | License | Used for |
|---|---|---|
| .NET runtime and ASP.NET Core (`Microsoft.*`, `System.*`) | MIT | Platform, web host, authentication |
| Microsoft.EntityFrameworkCore (+ Relational) | MIT | Writes and transactions |
| Npgsql | PostgreSQL | PostgreSQL driver |
| Npgsql.EntityFrameworkCore.PostgreSQL | PostgreSQL | EF Core provider for PostgreSQL |
| EFCore.NamingConventions | Apache-2.0 | snake_case table and column names |
| Dapper | Apache-2.0 | Read queries |
| Mediator.Abstractions / Mediator.SourceGenerator (martinothamar) | MIT | Command/query dispatch (source-generated) |
| Carter | MIT | Endpoint modules over minimal APIs |
| FluentValidation (+ DependencyInjectionExtensions) | Apache-2.0 | Request validation |
| ASP.NET Core Identity (Microsoft.AspNetCore.Identity.EntityFrameworkCore) | MIT | Sign-in, cookies, bearer + refresh tokens, lockout, password hashing |
| Microsoft.AspNetCore.DataProtection.EntityFrameworkCore | MIT | Persisting the keys that protect cookies and tokens |
| OpenTelemetry (.Extensions.Hosting, .Instrumentation.AspNetCore/Http/Runtime, .Exporter.OpenTelemetryProtocol) | Apache-2.0 | Traces, metrics and logs |
| Npgsql.OpenTelemetry | PostgreSQL | Database spans and metrics |
| Swashbuckle.AspNetCore | MIT | API documentation (development) |

## Used only to build and test (not shipped)

| Component | License |
|---|---|
| Microsoft.EntityFrameworkCore.Design, dotnet-ef | MIT |
| xUnit, xunit.runner.visualstudio | Apache-2.0 |
| AwesomeAssertions | Apache-2.0 |
| Microsoft.NET.Test.Sdk, Microsoft.AspNetCore.Mvc.Testing | MIT |
| Aspire (Aspire.AppHost.Sdk, dashboard, orchestrator) — development dashboard only | MIT |

## Required server software

| Component | License |
|---|---|
| PostgreSQL 16+ | PostgreSQL (free for any use; no license cost to the buyer) |

## Deliberately NOT used

| Package | Why |
|---|---|
| MediatR | Commercial license required for larger organizations since 2025 |
| FluentAssertions ≥ 8 | Paid license required for commercial use |
| AutoMapper | Commercial license since 2025 |
| QuestPDF | Free tier is revenue-gated, which would bind each buyer |
| Duende IdentityServer | Free tier is revenue-gated |
