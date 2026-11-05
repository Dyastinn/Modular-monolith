# Deploying to the Windows Server 2012 R2 dev box — status

This runbook used to walk the old SQL Server build onto the 2012 R2 dev server. The product has moved on
(PostgreSQL, EF Core migration bundles, ASP.NET Core Identity), and so has the calendar. Before anything is
deployed there again, a decision is needed.

## Why lesson 15 doesn't work on this box as written

| Problem | Detail |
|---|---|
| The OS is out of support | 2012 R2's Extended Security Updates ended on **2026-10-13**. .NET 10 was supported there only with ESU, so it's now unsupported on this box, and the box gets no security fixes |
| No app pool environment variables | Lesson 15 keeps every setting in the app pool's `environmentVariables`. That collection was added in IIS 10; this box has IIS 8.5 |
| No `removeServerHeader` | Also IIS 10 only. On IIS 8.5 the `Server: Microsoft-IIS/8.5` header stays |
| PostgreSQL support | The current PostgreSQL Windows installers don't list 2012 R2 among their supported platforms. Check EDB's platform support page; most likely the database needs its own, newer server |

## The recommendation

Move the app to Windows Server 2019 or later (2022 or 2025 for the longest runway), and follow
[lesson 15](../plan-business-tutorial/15-deploy-iis.md) as written. That's also what a buyer will run.

## If this box must be used anyway, short-term

Two old landmines still apply, before anything else:

1. **The Universal C Runtime.** .NET 10 needs it, and on 2012 R2 it's a separate update: **KB2919355** first
   (needs a reboot), then **KB2999226**. Without it, .NET fails with
   `api-ms-win-crt-runtime-l1-1-0.dll is missing`. Check with `Get-HotFix -Id KB2919355, KB2999226`.
2. **TLS 1.2 for downloads.** PowerShell 4 defaults to TLS 1.0, which Microsoft's servers refuse. Run
   `[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12` first in each session,
   or download the Hosting Bundle on another machine and copy it over.

Beyond that, lesson 15's settings step would have to move into the live site's `web.config`
(`<aspNetCore><environmentVariables>`), and the redeploy routine would have to stop copying `web.config` over
it. That keeps secrets in the site folder, which lesson 15 deliberately avoids. It hasn't been written or
tested; ask for it explicitly if the upgrade can't happen.
