# Phase 14b — Deploying to IIS (Optional)

> 🚫 **Outside the course, like [Phase 14](14-going-further.md).** The base build stops at a *correct* module;
> this is one way to get it *running on a server*. Nothing here changes the architecture — modules stay
> islands, the domain stays pure, and the only application code you touch in this whole phase is a single
> `if`. Read it when you have a server to deploy to.

---

> ⏱️ **Time for this phase:** ~1.5–2 hr for your first deploy, most of it spent on the server rather than in
> the code. Every subsequent deploy is minutes. Budget extra for the SQL Server permissions step — that's
> where first deploys actually die.

> 🧭 **From WebForms — this is the phase where your instincts will actively hurt you.** You've deployed to
> IIS a hundred times: copy the `.aspx` files up, point a site at the folder, set the app pool to .NET v4.0,
> done. **Every one of those steps is now wrong**, and one of them is wrong in a way that produces a blank
> error page with no explanation. Read §2 before you touch IIS Manager. It's the whole mental shift, and the
> other eleven steps are mechanical once it lands.

## 1. What you'll do

**In one sentence:** install one thing on the server, hand your app three settings that are wrong on your
laptop, copy a folder, and let a native IIS module start the app for you.

You'll finish with `https://your-server/health` returning `healthy` and a real login issuing a real token,
against a real SQL Server.

### The eleven steps, in the order you actually do them

Work top to bottom. The order is not arbitrary, and the two marked rows are the ones that punish you for
getting it wrong — both fail in a way that doesn't tell you the real reason.

| # | Where | Step | |
|---|---|---|---|
| 1 | server | Install **IIS**, then the **.NET Hosting Bundle** | ← order matters |
| 2 | server | Stand up a **real SQL Server**, and build the `Erp` database yourself | |
| 3 | your code | Stop the app building the database on startup — **the one code change** | |
| 4 | your machine | `dotnet publish` to a staging folder, and look at what came out | |
| 5 | your code | Put the server's settings in the project's `web.config`, republish | |
| 6 | server | Create the **app pool** (*No Managed Code*) and the **site**, copy the files in | |
| 7 | SQL | Create a login for the pool identity, `GRANT EXECUTE` on three schemas | ← needs step 6 |
| 8 | server | Browse `/health` and read what you get | |
| 9 | server | Bind a certificate — **HTTPS** | |
| 10 | server | Fix the app pool lifetime, or your background job silently never runs | |
| 11 | both | Redeploy without the file lock stopping you halfway | |

### Where everything lives when you're done

Deploying gets confusing because "the app" stops being one folder on one machine. It becomes six things in
six places:

| Thing | Where it ends up | Why there |
|---|---|---|
| Your source code | your machine only | never goes on the server — you deploy *output*, not source |
| The **publish output** | built on your machine, parked on the server at `C:\deploy\ErpApi` | a staging copy you can overwrite freely (step 4) |
| The **running site** | `C:\inetpub\ErpApi` on the server | what IIS points at; you copy staging *into* here (step 6) |
| The .NET runtime + **ANCM** | installed once, machine-wide | shared by every .NET app on the box (step 1) |
| The **database** | a real SQL Server instance | built by your deploy, not by your app (steps 2 & 7) |
| The **secrets** and connection string | environment variables on the app | so they're not in the artifact and not in Git (step 5) |

## 2. Why it's designed this way — start by unlearning IIS

### The naive version, and exactly how it fails

Do what WebForms taught you: publish the app, copy the folder to `C:\inetpub\wwwroot\ErpApi`, point an IIS
site at it, browse. You get:

```
HTTP Error 500.19 - Internal Server Error
The requested page cannot be accessed because the related configuration data for the page is invalid.
```

…or, on a good day, a prompt to **download `Erp.Api.dll`**. Neither mentions the actual problem, which is
this: **IIS has no idea what your app is.**

For WebForms, that instinct was right — IIS *was* the web server, it *was* the runtime host, and your
`.aspx` files were content it knew how to compile and execute. ASP.NET Core is a different animal:

> 🔑 **Your app is not a website that IIS runs. It's a self-contained program that already contains a web
> server** (Kestrel — it's been there since Phase 1, which is why `dotnet run` served HTTP without IIS
> anywhere). IIS's job is no longer *to be* the server. Its job is to **start your program and hand it
> traffic**.

That's the whole shift. IIS keeps the public address, the request queue, and the TLS certificate. It gives up
being the runtime.

Nothing in IIS knows how to start a .NET 10 program out of the box, though. That's what the **ASP.NET Core
Module (ANCM)** — a native IIS module, installed separately — exists for. Without it, IIS looks at
`Erp.Api.dll`, finds no handler that claims it, and gives you the 500.19 above. Step 1 installs it, and it is
the single thing people forget.

### The two hosting models

ANCM can start your app two ways. You want the first, you already have it, and the only reason to know the
second exists is that the error numbers differ (§16).

| | **In-process** (the default) | **Out-of-process** |
|---|---|---|
| Where your code runs | **inside** the IIS worker process (`w3wp.exe`) | in a separate `dotnet.exe`, started by ANCM |
| The web server used | `IISHttpServer` — Kestrel steps aside | **Kestrel**, with IIS reverse-proxying to it on a random local port |
| Speed | faster — no proxy hop | an extra network hop per request |
| Choose it when | ~always | you need the process isolated, or something in-process breaks you |

In-process has been the default since .NET Core 3.0, so there is nothing to set. The switch, if you ever need
it, is `<AspNetCoreHostingModel>OutOfProcess</AspNetCoreHostingModel>` in the `.csproj`.

> ❓ **"So is Kestrel gone?"** In-process, yes — `IISHttpServer` replaces it and IIS owns the socket. That's
> also why binding a port in `launchSettings.json` means nothing on a server: **IIS decides the address**, via
> the site's bindings. `launchSettings.json` is a *development-only* file and isn't even published.

---

## 3. Step 1 — Install IIS, then the Hosting Bundle

**What's happening:** you're installing the web server, and then the piece from §2 that teaches it how to
start a .NET app. Two installers, and **the order between them matters.**

**1) IIS itself.** *Server Manager → Add Roles and Features → Web Server (IIS)*. On Windows 10/11 for a test:
*Turn Windows features on or off → Internet Information Services*. If you want the preload feature from step
10, also tick **Application Initialization** (under *World Wide Web Services → Application Development
Features*).

**2) The .NET Hosting Bundle** — from <https://dotnet.microsoft.com/download/dotnet/10.0>, the
"**ASP.NET Core Runtime … Hosting Bundle**" (`dotnet-hosting-10.x.x-win.exe`). This one installer gives you
the .NET runtime **plus ANCM**.

Then restart IIS so it picks up the new native module:

```powershell
net stop was /y
net start w3svc
```

> ⚠️ **IIS *first*, the Hosting Bundle *second*.** The bundle registers ANCM **into IIS** — if IIS isn't
> there yet, it silently skips that part, and you're left with a perfectly good runtime and a 500.19 that
> won't go away. If you did it backwards (very common — the machine already had .NET on it), you don't need
> to uninstall anything. Repair the bundle:
>
> ```powershell
> .\dotnet-hosting-10.0.0-win.exe /repair
> ```

**Verify ANCM actually landed.** This is a 10-second check that saves an hour, and it's worth doing now
rather than discovering it at step 8:

```powershell
# The module file should exist...
Test-Path 'C:\Program Files\IIS\Asp.Net Core Module\V2\aspnetcorev2.dll'      # → True

# ...and IIS should have it registered globally:
& "$env:windir\system32\inetsrv\appcmd.exe" list module /name:AspNetCoreModuleV2
# → MODULE "AspNetCoreModuleV2" ( native )
```

If the first prints `True` and the second prints nothing, that's exactly the "bundle before IIS" case.
Repair it and re-check.

---

## 4. Step 2 — A real SQL Server, and you build the database

**What's happening:** your laptop's database was a developer toy that cannot work under IIS. You're replacing
it with a real one, and — importantly — **you** are creating the schema, by hand, as part of the deploy.

### LocalDB is over

Your `appsettings.json` has said this since Phase 3:

```json
"ErpDb": "Server=(localdb)\\MSSQLLocalDB;Database=Erp;Trusted_Connection=True;TrustServerCertificate=True"
```

> ⚠️ **LocalDB does not work under IIS, and this is not a configuration problem you can fix.** LocalDB is a
> *developer* feature: it starts an instance on demand, per Windows user, inside that user's interactive
> session. An IIS app pool runs as a service identity with no interactive session and (by default) no loaded
> user profile — so there's nothing for LocalDB to attach to. You get a login failure, or a "cannot open user
> default database," that looks like a permissions bug and isn't. **You need a real SQL Server.** Express is
> free and fine. This was always going to happen; LocalDB was a Phase 3 convenience and the bill comes due
> here.

Install SQL Server (Express is enough), and note the instance name — `SQLBOX01` in the examples below. You'll
use it in the connection string in step 5.

### Now create the database — yourself, once

Connect as an admin and run the same scripts the app has been running for you since Phase 3: create the `Erp`
database, the three schemas, the tables, and the stored procedures. Same scripts, different driver — this is
the tracked-runner (DbUp) option from [Phase 3 §7](03-database-schema.md), and here's where the distinction
stops being theoretical.

> 🔑 **The rule this encodes: schema changes belong to the deployment, not the application.** Run them once,
> before the new app version starts, with an account that has DDL rights. An app that migrates its own
> database on startup needs permissions it should never hold, and has a race condition the moment there are
> two of it. Step 3 is where you enforce that in code.

---

## 5. Step 3 — The one code change: stop the app building the database

**What's happening:** your `Program.cs` currently builds the database on every boot. On a laptop that's a
gift. On a server it's three problems at once, so you're switching it off outside Development.

You added this in [Phase 12 §4](12-testing.md):

```csharp
await DatabaseInitializer.RunAsync(app);
```

On a server that line means:

- It tries `CREATE DATABASE`, so your app's login would need **`dbcreator` or sysadmin** — which blows up the
  minimal grant you're about to make in step 7, for no benefit whatsoever.
- It runs **every script on every app-pool start** — including recycles, at 3am, unattended.
- Scale to two servers and **both** race to apply schema changes to one database.

Phase 12 flagged this at the time ("*a dev/test convenience, not a deploy strategy*"). Here's the collection:

```csharp
// Program.cs — the database is built by the DEPLOY (step 2), not by the app.
if (app.Environment.IsDevelopment())
    await DatabaseInitializer.RunAsync(app);
```

That `if` is the only line of application code this phase touches. Everything else is configuration and
server setup — which is the point, and §14 comes back to why that matters.

---

## 6. Step 4 — Publish, and look at what came out

**What's happening:** publishing compiles the app, gathers every dependency, and produces a folder that is
the deployable unit. It is not "copy the source." You're not deploying it yet in this step — you're building
it and reading what's inside, because one file in there will surprise you.

```powershell
dotnet publish src/Erp.Api -c Release -o C:\deploy\ErpApi
```

That output folder **is** the artifact — it's the entire thing you deploy. How it gets onto the server is
your call and none of IIS's business: a file copy, a network share, a build agent, a zip and an RDP session.
All that matters is that it ends up on the server at `C:\deploy\ErpApi`, because every step from 6 onward
assumes it's sitting there. (Publishing on the server itself is fine for a first deploy — you just need the
SDK on it.)

> ⚠️ **Publish to a staging folder, not straight onto the live site.** While the app is running, IIS holds
> its DLLs **locked**, and publishing straight into `C:\inetpub\ErpApi` fails halfway with
> `The process cannot access the file 'Erp.Api.dll'` — leaving a half-updated site. Staging keeps the
> "build it" step and the "swap it in" step separate, which is exactly what step 11 needs.

### Framework-dependent or self-contained?

A real choice, and the default is right:

| | **Framework-dependent** (default) | **Self-contained** |
|---|---|---|
| Command | `dotnet publish -c Release` | `dotnet publish -c Release -r win-x64 --self-contained` |
| Size | small (~a few MB) | large (~70 MB+ — the whole runtime ships with it) |
| Needs .NET on the server? | **Yes** — but step 1's Hosting Bundle already installed it | **No** |
| Patching the runtime | one server-wide update fixes every app | you rebuild and redeploy **every** app |

**Take framework-dependent.** You installed the Hosting Bundle anyway (you need ANCM regardless), so the
runtime is already there — and a security patch to .NET then fixes every app on the box without a redeploy.
Self-contained earns its keep when you *can't* control the server's runtime.

### The file you thought you'd left behind

Look in the publish folder. There's a `web.config` in it, and you didn't write it — `dotnet publish`
generated it:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <handlers>
        <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      </handlers>
      <aspNetCore processPath="dotnet" arguments=".\Erp.Api.dll"
                  stdoutLogEnabled="false" stdoutLogFile=".\logs\stdout"
                  hostingModel="inprocess" />
    </system.webServer>
  </location>
</configuration>
```

Read it and notice how *small* its job is. It says exactly two things: **"route every request to ANCM"** and
**"ANCM, here's how to start the app."** That's all it does now.

> 🧭 **From WebForms — this is the trap.** `web.config` used to be *the* config file: connection strings, app
> settings, auth, session, the lot. Now it configures **IIS and the module**, and **nothing about your
> application**. Your connection string and JWT key come from `appsettings.json` and environment variables
> through `IConfiguration` ([Phase 4b §7](04b-jwt-login.md)) — the same pipeline whether you run under IIS,
> under `dotnet run`, or in a container. Put a `<connectionStrings>` section in here and it will be ignored
> completely and silently, and you will lose an afternoon.

> ⚠️ **Don't hand-edit the `web.config` in the publish folder.** `dotnet publish` regenerates it, so your
> next deploy erases the change. To make an edit stick, put a `web.config` in the **project** (next to
> `Program.cs`); the SDK *merges* its own `<aspNetCore>` settings into yours on publish. That's exactly what
> step 5 does.

---

## 7. Step 5 — The three settings that are wrong on a server

**What's happening:** your app runs on your laptop because of three settings that are all wrong on a server.
None of them is code. This is the real work of the phase, and it's where step 8 will fail if you skip it.

| Setting | On your laptop | On the server |
|---|---|---|
| `ConnectionStrings:ErpDb` | LocalDB | the real SQL Server from step 2 |
| `Jwt:SigningKey` (+ `ApiKeys:*`) | user-secrets | an environment variable — **user secrets don't exist in production** |
| `ASPNETCORE_ENVIRONMENT` | `Development` | **`Production`** |

The second one matters because [Phase 14 §1](14-going-further.md) moved the signing key to user-secrets, and
that provider **only loads in Development, by design**. There is no such thing as user-secrets on a server.
The value has to come from somewhere else, and an environment variable is the simplest somewhere.

The third one is quieter and worse. Unset means Production, which is what you want — but be deliberate,
because this is the switch that makes [Phase 11's](11-cross-cutting.md) `GlobalExceptionHandler` stop putting
stack traces in 500 responses. Deploy with it set to `Development` and **your production API hands attackers
your stack traces**, while *looking* perfectly fine, because everything works.

Set all of them on the app, in the **project's** `web.config` — the one that survives a republish, per step 4:

```xml
<aspNetCore processPath="dotnet" arguments=".\Erp.Api.dll" hostingModel="inprocess">
  <environmentVariables>
    <environmentVariable name="ASPNETCORE_ENVIRONMENT"     value="Production" />
    <environmentVariable name="ConnectionStrings__ErpDb"   value="Server=SQLBOX01;Database=Erp;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True" />
    <environmentVariable name="Jwt__SigningKey"            value="..." />
    <environmentVariable name="ApiKeys__web"               value="..." />
  </environmentVariables>
</aspNetCore>
```

That **double underscore** is the same `__` = "child key" rule from [Phase 4c §11](04c-jwt-refresh-apikey.md)
— `ConnectionStrings__ErpDb` is exactly `ConnectionStrings:ErpDb`. Your code doesn't change, because
`IConfiguration` doesn't care where a value came from. That's the payoff of never reading `appsettings.json`
directly.

> ⚠️ `TrustServerCertificate=True` means "encrypt, but don't verify who I'm talking to" — acceptable on a
> trusted LAN, **not** acceptable across an untrusted network, where it leaves you open to an impersonation
> attack. The real fix is a certificate SQL Server can present that the app trusts; then drop the flag. It's
> here because that's a certificate story, not a deployment one.

> ⚠️ **A secret in `web.config` is a secret in a file on disk, in your deploy artifact.** It's a genuine step
> up from `appsettings.json` in Git, and it's where most IIS shops land — but be clear that it's a
> *convenience*, not a vault. The stronger options, in order: set the env vars on the app pool itself
> (IIS Manager → *Configuration Editor* → `system.applicationHost/applicationPools`), so they live in
> `applicationHost.config` outside your deploy folder; or read them from a real secret store at startup.
> Whichever you pick, **the signing key must never be in source control** — [Phase 14 §1](14-going-further.md)
> also shows how to make a missing one fail the *deploy* instead of the first login.

Now republish, so the merged `web.config` and the step 3 guard are both in the folder you're about to copy:

```powershell
dotnet publish src/Erp.Api -c Release -o C:\deploy\ErpApi
```

---

## 8. Step 6 — Create the app pool and the site, and copy the files in

**What's happening:** an **app pool** is the Windows process your app will live inside, with an identity of
its own; a **site** is the address IIS answers on and the folder it serves. You're creating both, then
putting your published files where the site points.

**The app pool first** — and here is the gotcha that will get you:

```powershell
Import-Module WebAdministration

New-WebAppPool -Name 'ErpApi'
# ↓↓↓ THE ONE THAT BITES ↓↓↓
Set-ItemProperty IIS:\AppPools\ErpApi -Name managedRuntimeVersion -Value ''   # = "No Managed Code"
```

> ⚠️ **The app pool must be set to "No Managed Code" — not .NET v4.0.** In IIS Manager that's *Basic
> Settings → .NET CLR version → No Managed Code*; in PowerShell it's the empty `managedRuntimeVersion`
> above. Every WebForms instinct says v4.0, and it is exactly backwards: that setting loads the **.NET
> Framework** CLR into the worker process, and your app doesn't want it — **ANCM loads .NET 10 itself.**
> Leaving it at v4.0 gets you a startup failure whose message will not mention the app pool.
>
> It reads as a paradox — "*No* Managed Code, for an app that is nothing but managed code?" The setting
> doesn't mean "no .NET here." It means "**IIS**, don't *you* load a CLR — somebody else is handling that."

**Then the site, and the files.** The site points at `C:\inetpub\ErpApi`; your publish output is sitting in
`C:\deploy\ErpApi` from step 5. Copy it across:

```powershell
New-Item -ItemType Directory 'C:\inetpub\ErpApi' -Force
Copy-Item C:\deploy\ErpApi\* C:\inetpub\ErpApi\ -Recurse -Force

New-Website -Name 'ErpApi' -PhysicalPath 'C:\inetpub\ErpApi' -Port 80 -ApplicationPool 'ErpApi'
```

**Then grant the pool identity read access to the folder:**

```powershell
icacls 'C:\inetpub\ErpApi' /grant 'IIS AppPool\ErpApi:(OI)(CI)RX' /T
```

Read is all it needs. It doesn't write to its own folder — with one exception, `logs\`, if you turn on stdout
logging while debugging (§16).

---

## 9. Step 7 — Give the app pool a SQL login, and almost no rights

**What's happening:** your app now exists as a Windows identity, and SQL Server has never heard of it. You're
introducing them — and then granting a startlingly small set of permissions.

> ⚠️ **Do this *after* step 6, not before.** The identity `IIS APPPOOL\ErpApi` is a virtual account that
> **comes into existence when the app pool is created**. Run `CREATE LOGIN` first and SQL Server can't
> resolve the name, and tells you so in a way that sounds like you typed it wrong.

### Who your app *is* to SQL Server

By default an app pool runs as **`ApplicationPoolIdentity`**, a virtual account named after the pool. So your
app authenticates to SQL as:

| SQL Server is… | The login you must create |
|---|---|
| **on the same machine** | `IIS APPPOOL\ErpApi` (the pool name) |
| **on another machine** | `DOMAIN\WEBSERVER$` — the *machine account*. ApplicationPoolIdentity has no network identity of its own, so it goes out onto the network as the computer. |
| you'd rather be explicit | a **domain service account** set as the pool identity — usually the right answer for remote SQL |

### The grant

Run this as an admin, against the `Erp` database you built in step 2. Local SQL + ApplicationPoolIdentity
shown:

```sql
CREATE LOGIN [IIS APPPOOL\ErpApi] FROM WINDOWS;
GO
USE Erp;
CREATE USER [IIS APPPOOL\ErpApi] FOR LOGIN [IIS APPPOOL\ErpApi];
GO
-- EXECUTE on each module's schema. That is the WHOLE grant.
GRANT EXECUTE ON SCHEMA::sales      TO [IIS APPPOOL\ErpApi];
GRANT EXECUTE ON SCHEMA::inventory  TO [IIS APPPOOL\ErpApi];
GRANT EXECUTE ON SCHEMA::[identity] TO [IIS APPPOOL\ErpApi];
GO
```

> 🔑 **Stop and notice what you did *not* grant: any permission on any table.** No `SELECT` on `Customer`, no
> `INSERT` on `SalesOrder`, no `db_datareader`, no `db_datawriter`. Nothing. And the app works — because
> **every single database call it makes is `EXEC`ing a stored procedure** ([Phase 3](03-database-schema.md)),
> and SQL Server's *ownership chaining* means a caller with `EXECUTE` on a proc may do whatever that proc
> does to tables in the same schema, without holding rights on the tables themselves.
>
> That is a real, load-bearing security payoff, and it's the moment Phase 3's "why stored procedures?"
> finally cashes out. A SQL-injection hole or a compromised app identity gets an attacker exactly the
> operations you wrote procs for — it cannot `SELECT * FROM [identity].AppUser`, cannot `DROP TABLE`, cannot
> read a column no proc returns. Compare an app holding `db_datawriter`: one hole and the attacker has your
> whole database. Ask the honest question here — *would I have gotten this from an ORM?* Not for free. This
> is the dividend of the choice Phase 3 made, and most teams never collect it, because they grant `db_owner`
> on day one and move on.

---

## 10. Step 8 — Start it, and read the result

**What's happening:** nothing starts your app until the first request arrives. That request is the test.

```powershell
Invoke-RestMethod http://localhost/health      # on the server itself
```

The first request is the slow one: IIS matches the binding, hands the request to ANCM, and ANCM starts .NET
10 inside `w3wp.exe` and runs your `Program.cs`. Every request after that skips straight to the app.

- **`healthy`** — the app started, the config landed, and SQL answered. Go to step 9.
- **Anything else** — you have a number. **Go to §16 and read it**, because 500.19, 500.30, 500.31 and 500.0
  are four genuinely different problems, and guessing between them turns a five-minute fix into an afternoon.

---

## 11. Step 9 — HTTPS

**What's happening:** IIS owns the certificate now, because IIS owns the socket (§2). Your app doesn't
terminate TLS anymore — but it should still refuse to be talked to in plaintext.

Bind a certificate to the site (IIS Manager → *Bindings → Add → https*, pick the cert), and make sure the
redirect + HSTS from [Phase 14 §1](14-going-further.md) are on:

```csharp
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
```

Your JWT is a bearer token: whoever holds it *is* the user. The signature stops forgery, not **copying** — so
a token on plain HTTP is a festival wristband anyone on the wire can photocopy. TLS is the only thing that
stops the copy.

---

## 12. Step 10 — IIS will quietly kill your background work

**What's happening:** you don't own your app's lifetime anymore. IIS does — and its defaults will stop your
process while you're not looking.

By default an app pool **shuts down after 20 minutes with no requests** (Idle Time-out), and **recycles every
29 hours** (Regular Time Interval). For a WebForms site that was harmless: no requests, nobody cares, the
next request spins it back up.

But if you deployed the in-process refresh-token sweeper from
[Phase 4c §10](04c-jwt-refresh-apikey.md) — a `BackgroundService` on a 6-hour `PeriodicTimer` — then **a
quiet night kills it.** The pool idles out at 20 minutes, the process dies, the timer dies with it, and the
sweep never runs. Nothing errors. Nothing logs. The `RefreshToken` table just quietly grows forever, and the
first you hear of it is a performance complaint months later.

Two honest fixes, and the first is better:

**1) Move the schedule out of the app** — the **SQL Agent job** from
[Phase 4c §10](04c-jwt-refresh-apikey.md). This is that section's "prefer Agent where you have it" advice
suddenly becoming very concrete: the database is always running, it doesn't idle out, and it doesn't care how
many web servers you have. *If your background work is a database sweep, put the schedule in the database.*

**2) Or keep the pool alive**, if the work genuinely must live in the app:

```powershell
Set-ItemProperty IIS:\AppPools\ErpApi -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)  # never idle out
Set-ItemProperty IIS:\AppPools\ErpApi -Name recycling.periodicRestart.time -Value ([TimeSpan]::Zero)
Set-ItemProperty IIS:\AppPools\ErpApi -Name startMode -Value 'AlwaysRunning'   # start on boot, don't wait
Set-ItemProperty 'IIS:\Sites\ErpApi' -Name applicationDefaults.preloadEnabled -Value $true
```

(`preloadEnabled` needs the **Application Initialization** feature from step 1.)

> 🔑 **The transferable lesson, and it outlives IIS:** *in-process background work has exactly the lifetime
> of the process that hosts it* — and on a web server, you don't own that lifetime. The host decides. Any
> "run this every N hours" that must actually happen belongs somewhere with its own lifetime: the database's
> scheduler, Task Scheduler, a worker service, or a cloud timer. This is the same instinct as
> [Phase 8](08-approval-cancellation.md)'s best-effort event dispatch — know which of your side effects are
> guaranteed and which are hopeful.

---

## 13. Step 11 — Deploying again: the file lock

**What's happening:** the app is running, so IIS holds its DLLs locked, so a plain `Copy-Item` over the top
fails partway and leaves you with half of one version and half of another. Two answers.

**`app_offline.htm`** — the classic. Drop a file with that exact name in the app root and ANCM gracefully
shuts the app down and serves that file to everyone. Copy your files, delete it, the app restarts:

```powershell
'<h1>Erp API — back in a minute.</h1>' | Set-Content C:\inetpub\ErpApi\app_offline.htm
Copy-Item C:\deploy\ErpApi\* C:\inetpub\ErpApi\ -Recurse -Force
Remove-Item C:\inetpub\ErpApi\app_offline.htm
```

**Shadow copying** — ANCM copies the app elsewhere and runs *that*, so the originals are never locked and you
can deploy over a running site with no downtime. Add it to the project's `web.config`:

```xml
<aspNetCore processPath="dotnet" arguments=".\Erp.Api.dll" hostingModel="inprocess">
  <handlerSettings>
    <handlerSetting name="enableShadowCopy"    value="true" />
    <handlerSetting name="shadowCopyDirectory" value="../ShadowCopy/" />
  </handlerSettings>
</aspNetCore>
```

Start with `app_offline.htm`. It's one file, it's obvious, and a visible "back in a minute" is honest. Reach
for shadow copying when a few seconds of downtime per deploy actually costs something.

So a routine redeploy, end to end, is: `dotnet publish` → copy to `C:\deploy\ErpApi` → `app_offline.htm` in →
copy across → `app_offline.htm` out. Steps 1, 2, 6 and 7 were one-time server setup; you never repeat them.

---

## 14. How a request flows now

Nothing you built changed. One layer appeared in front of it:

```
Browser  →  https://erp.company.local/sales-orders
    │
    ▼
IIS  — site binding matches, TLS terminated here          (step 6 / step 9)
    │
    ▼
ANCM (AspNetCoreModuleV2)  — the handler web.config pointed every request at
    │  (first request only: starts .NET 10 INSIDE w3wp.exe, runs Program.cs)
    ▼
IISHttpServer  — where Kestrel would have been under `dotnet run`
    │
    ▼
━━━━━━━━ from here down, identical to your laptop ━━━━━━━━
ApiKeyMiddleware   → which app is calling?          (Phase 4c §11)
UseAuthentication  → which user?                    (Phase 4b §7)
UseAuthorization   → are they allowed?              (Phase 4b §9)
Carter endpoint → ISender → behaviors → handler → domain → Dapper → proc
```

> 🔑 **Everything below the line is untouched, and that's the point of the phase.** IIS is a hosting
> decision, not an architectural one. The same publish folder runs behind Nginx, in a container, or on a
> different box tomorrow — because your app was never coupled to its host, only to `IConfiguration`. Eleven
> steps, and the only code you edited was one `if` about who builds the database. If deploying had forced you
> to change a handler, something would have been wrong upstream.

## 15. Why each piece exists

| Piece | Why it's there |
|---|---|
| **Hosting Bundle** (step 1) | Installs **ANCM** (the missing link from §2) plus the runtime. Without it IIS can't start a .NET app at all. |
| **ANCM / `AspNetCoreModuleV2`** | The native module that launches your app and hands it requests. The whole reason IIS can host something it doesn't understand. |
| **`web.config`** (step 4) | Configures *ANCM and IIS only* — never your app. Auto-generated; edit the project's copy to persist changes. |
| **App pool = "No Managed Code"** (step 6) | Stops IIS loading the .NET Framework CLR. ANCM loads .NET 10 itself. |
| **App pool identity** (step 7) | Who your app *is* to Windows and SQL Server. Gets `EXECUTE` on three schemas — and nothing else. |
| **`ASPNETCORE_ENVIRONMENT=Production`** (step 5) | Switches Phase 11's handler to bland 500s. The difference between a safe API and one that narrates its stack traces. |
| **Env vars over `appsettings.json`** (step 5) | The signing key and connection string stay out of the artifact and out of Git. |
| **Guarding `DatabaseInitializer`** (step 3) | Schema is the deploy's job. Keeps the app's login free of DDL rights, and two servers from racing. |
| **`app_offline.htm`** / shadow copy (step 11) | Deals with the file lock a running app holds on its own DLLs. |
| **SQL Agent job** (step 10) | A schedule that survives the app pool sleeping. |

## 16. When it breaks — read the number

IIS failures are famously opaque, but the number is a real diagnosis. This table is most of what you need:

| Error | It means | Fix |
|---|---|---|
| **500.19** (`0x8007000d`) | IIS can't read `web.config` — usually **ANCM isn't installed** | Install/repair the Hosting Bundle (step 1) |
| **500.30** In-Process Start Failure | Your app **threw during startup** — this is *your* bug | stdout log / Event Viewer, below. Usually config: a missing signing key or a bad connection string (step 5) |
| **500.31** Failed to load .NET runtime | The runtime version your app targets isn't on the box | Install the matching Hosting Bundle (step 1) |
| **500.0** In-Process Handler Load Failure | App pool is on **v4.0** instead of No Managed Code | step 6 |
| **502.5** Process Failure | Out-of-process startup crash (only if you switched models) | Same as 500.30 |
| **HTTP 200 but everything 500s** | App started; something at runtime fails — usually **SQL login** | steps 5 & 7 — check for `Login failed for user 'IIS APPPOOL\ErpApi'` |

**Where the truth actually is**, in the order you should look:

**1) Event Viewer** — *Windows Logs → Application*, source **`IIS AspNetCoreModule V2`**. Startup crashes land
here with the real exception. This is the first place to look and the one people forget.

**2) The stdout log** — turn it on *temporarily* to capture what the app printed as it died:

```xml
<aspNetCore ... stdoutLogEnabled="true" stdoutLogFile=".\logs\stdout" />
```

```powershell
New-Item -ItemType Directory C:\inetpub\ErpApi\logs                        # ANCM won't create it
icacls 'C:\inetpub\ErpApi\logs' /grant 'IIS AppPool\ErpApi:(OI)(CI)M'      # and it needs write access
```

> ⚠️ **Turn stdout logging back off when you're done.** It never rotates and never truncates — it will
> cheerfully fill the disk over a few months, and "the web server ran out of disk" is a bad way to learn
> this. It's a diagnostic, not logging. Real logging is the `ILogger` you already have
> ([Phase 14 §1](14-going-further.md)), pointed at a real sink.

## 17. Checkpoint — the module runs on a server ✅

You're proving three separate things: that IIS starts your app, that the deployed configuration is right, and
that **every rule you wrote still fires in production**.

From another machine (or the server itself), with `$Base` set to the real URL:

```powershell
# 1) IIS starts the app at all
Invoke-RestMethod "$Base/health"                       # → healthy

# 2) The API-key gate still guards the anonymous door
(Invoke-Api '/auth/login' -Method Post -Body @{ username='rep'; password='Password123!' }).Status
#    with a WRONG key → 401 "Missing or invalid API key."

# 3) Login works — which proves the SQL login, the connection string, AND the signing key all landed
$login = Invoke-Api '/auth/login' -Method Post -Body @{ username='rep'; password='Password123!' }
$login.Status                                          # → 200
$token = $login.Body.accessToken

# 4) A business rule still fires — the domain doesn't care that it's in production
(Invoke-Api '/sales-orders' -Method Post -Token $token -Body @{
    customerId = 3; lines = @( @{ itemId = 1; quantity = 1 } ) }).Status     # → 422 (Gamma is blocked)
```

(The `Invoke-Api` helper is in [00b-WINDOWS-POWERSHELL.md](00b-WINDOWS-POWERSHELL.md); `curl` works the same.)

**5) Now the one people skip — prove `Production` is real.** Force an unexpected error and read the body:

```powershell
(Invoke-Api '/sales-orders/not-a-number' -Token $token).Body.detail
```

You want a **bland** message and **no stack trace**. If you can see a stack trace, `ASPNETCORE_ENVIRONMENT`
is wrong — go back to [§7](#7-step-5--the-three-settings-that-are-wrong-on-a-server). You just found it
*before* an attacker did, which is the entire reason this check exists.

### You're done when

| # | You proved | Expected |
|---|---|---|
| 1 | IIS starts the app through ANCM | `/health` → **200** `healthy` |
| 2 | The gate deployed with it | wrong/missing key → **401** |
| 3 | Config landed: SQL login + connection string + signing key | login → **200** with a token |
| 4 | The business rules are untouched by hosting | blocked customer → **422** |
| 5 | It's genuinely in Production mode | a 500 shows **no** stack trace |
| 6 | The app has no rights it shouldn't | it works with `EXECUTE`-only, no table grants (deploy step 7) |

If all six hold, **Phase 14b is done and verifiable**: the module you built on a laptop is serving real
traffic, with an app identity that can't read a table directly, and it took **one line of application code**
to get there.

## 18. Common mistakes & best practices

- ❌ **Hosting Bundle installed before IIS** → 500.19 forever. ✅ IIS first, then the bundle, then
  `net stop was /y; net start w3svc`. Already wrong? `/repair`.
- ❌ **App pool set to .NET CLR v4.0** (the WebForms reflex). ✅ **No Managed Code** — ANCM loads .NET itself.
- ❌ **Connection strings or secrets in `web.config`'s `<connectionStrings>`.** ✅ It's ignored — silently.
  `IConfiguration` reads `appsettings.json` + env vars; use `__` for nesting.
- ❌ **Editing the `web.config` in the publish folder.** ✅ The next publish overwrites it. Edit the
  **project's** copy; the SDK merges it.
- ❌ **LocalDB on the server.** ✅ It cannot work under an app pool identity. Real SQL Server.
- ❌ **`CREATE LOGIN [IIS APPPOOL\ErpApi]` before the app pool exists.** ✅ The virtual account is created
  *with the pool*. Pool first, login second.
- ❌ **Granting the app `db_owner`/`db_datawriter` "to get it working."** ✅ `EXECUTE` on the three schemas is
  genuinely all it needs — because everything goes through procs. Don't throw that away on a bad afternoon.
- ❌ **Letting the app create/migrate the database on startup.** ✅ Guard `DatabaseInitializer` to Development;
  make the deploy apply scripts once, with an account that has DDL rights.
- ❌ **Deploying with `ASPNETCORE_ENVIRONMENT=Development`.** ✅ It works perfectly — while leaking stack
  traces to anyone who triggers a 500. Verify it (checkpoint step 5).
- ❌ **`dotnet publish` straight into the live site.** ✅ Publish to staging; deploy with `app_offline.htm` or
  shadow copying.
- ❌ **Trusting an in-process `BackgroundService` to run on a schedule.** ✅ The pool idles out and it dies
  silently. SQL Agent, or `idleTimeout=0` + `AlwaysRunning`.
- ❌ **Leaving `stdoutLogEnabled="true"`.** ✅ Diagnostic only; it never rotates. Off when you're done.
- ✅ **Best practice: read the error number before you touch anything.** 500.19 / 500.30 / 500.31 / 500.0 are
  four *different* problems (§16), and guessing turns a five-minute fix into an afternoon.
- ✅ **Best practice: check Event Viewer first.** *Application → `IIS AspNetCoreModule V2`* has the real
  exception, every time.

---

## ✅ Check Yourself

> Try each one before you look. [Model answers](ANSWER-KEY.md#phase-14b-deploying-to-iis) — but attempting first is what makes them stick.

1. Under WebForms, IIS *was* the runtime. What is it now, and what does ANCM actually do?
2. Why must the app pool be "No Managed Code" for an app made entirely of managed code?
3. `web.config` is back. What does it configure now — and where does your connection string come from
   instead?
4. Your deployed app has `EXECUTE` on three schemas and no permission on any table, and it works. Why — and
   what does that buy you if someone finds a SQL-injection hole?
5. Why is letting the app run `DatabaseInitializer` on startup fine on your laptop and wrong on a server?
6. You deployed the Phase 4c refresh-token sweeper as a hosted service. Why might it never run for weeks
   without a single error appearing anywhere?
7. Your API works perfectly in production. Why should you deliberately trigger a 500 anyway?

---

← Previous: [Phase 14 — Going Further](14-going-further.md) · [Tutorial overview](00-START-HERE.md) · Next: [Phase 15 — Scaling the Wall](15-scaling-the-wall.md) →
