# Third-party notices

This product includes the open-source components below. Every one uses a permissive license (MIT,
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
| Blazor WebAssembly (Microsoft.AspNetCore.Components.WebAssembly, .Server, .Authorization) | MIT | The browser app and its hosting |
| MudBlazor 9.12.0 | MIT | Screen components |
| Microsoft.EntityFrameworkCore (+ Relational) | MIT | Writes and transactions |
| Npgsql | PostgreSQL | PostgreSQL driver |
| Npgsql.EntityFrameworkCore.PostgreSQL | PostgreSQL | EF Core provider for PostgreSQL |
| EFCore.NamingConventions | Apache-2.0 | snake_case table and column names |
| Dapper | Apache-2.0 | Read queries |
| Mediator.Abstractions / Mediator.SourceGenerator (martinothamar) | MIT | Command/query dispatch (source-generated) |
| Carter | MIT | Endpoint modules over minimal APIs |
| FluentValidation (+ DependencyInjectionExtensions) | Apache-2.0 | Request validation |
| ASP.NET Core Identity (Microsoft.AspNetCore.Identity.EntityFrameworkCore) | MIT | Sign-in, cookies, bearer + refresh tokens, lockout, password hashing |
| Microsoft.AspNetCore.DataProtection.EntityFrameworkCore | MIT | Persisting (certificate-encrypted) the keys that protect cookies and tokens |
| MailKit / MimeKit | MIT | Password-reset email over SMTP |
| BouncyCastle.Cryptography (via MimeKit) | MIT | Cryptography used by MimeKit |
| PDFsharp-MigraDoc **6.2.4** | MIT | Invoice PDFs. 6.2.4 is the last MIT release; 6.5+ is AGPL-3.0 and must not be used |
| System.Security.Cryptography.Pkcs (pinned 10.0.14) | MIT | Pulled in by PDFsharp; pinned past a vulnerable version |
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
| Microsoft.Playwright (browser tests; drives the browser already installed) | MIT |
| Aspire (Aspire.AppHost.Sdk, dashboard, orchestrator) — development dashboard only | MIT |

## Server software the buyer runs (not part of this source)

| Component | License |
|---|---|
| PostgreSQL 16+ (`postgres:18` image for Docker) | PostgreSQL |
| .NET ASP.NET Core runtime images (`mcr.microsoft.com/dotnet/aspnet`) | MIT |
| Caddy (`caddy:2` image): HTTPS for the Docker install | Apache-2.0 |
| DejaVu fonts (installed in the app image from Debian, used for invoice PDFs) | Bitstream Vera / public domain (free to use and redistribute) |
| Docker Engine on the server | Apache-2.0. Docker *Desktop* is a developer tool, free only for small businesses; servers don't need it |

## Deliberately NOT used

| Package | Why |
|---|---|
| MediatR | Commercial license required for larger organizations since 2025 |
| FluentAssertions ≥ 8 | Paid license required for commercial use |
| AutoMapper | Commercial license since 2025 |
| QuestPDF | Free tier is revenue-gated, which would bind each buyer |
| PDFsharp / MigraDoc ≥ 6.5 | AGPL-3.0 (with a runtime license check): copyleft would bind each buyer |
| Duende IdentityServer | Free tier is revenue-gated |
