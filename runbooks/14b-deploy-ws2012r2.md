# Deploy the Erp API to IIS — Windows Server 2012 R2 (dev server)

**This is a runbook, not a lesson.** It's the [Phase 14b](../plan-business-tutorial/14b-deploy-iis.md)
content with every branch already resolved for your specific box. Run it top to bottom. If you want to know
*why* any of it works the way it does, 14b explains it — this file just tells you what to type.

**Your box, as I understand it:**

| | |
|---|---|
| OS | Windows Server 2012 R2, Desktop Experience (has a GUI) |
| IIS | 8.5 (not 10 — the version number differs, nothing else does) |
| PowerShell | 4.0 (the stock one; this matters twice, in steps 1 and 3) |
| .NET | 10 Hosting Bundle — you'll install it in step 3 |
| SQL Server | already running; you have a working connection string |
| Purpose | dev server |

**Three things to know before you start, once, and then I'll stop mentioning them:**

1. **2012 R2 is out of support.** Extended support ended October 10, 2023. Microsoft says .NET 10 is
   supported here *only with ESU*, and [ESU itself ends October 13, 2026](https://learn.microsoft.com/en-us/windows-server/get-started/extended-security-updates-overview)
   — about three months from now. It's a dev server, so this is a scheduling problem, not an emergency. But
   the runway is real, and it's shorter than it looks.
2. **Everything here works on .NET 10.** You don't need .NET 8 for 2012 R2 — [.NET 8 has the exact same ESU
   footnote](https://github.com/dotnet/core/blob/main/release-notes/8.0/supported-os.md) and reaches end of
   support in November 2026, sooner than the OS does. See [the .NET 8 note](#appendix--if-you-really-do-need-net-8) at the bottom.
3. **2012 R2 has two landmines that no other Windows version has.** They're steps 1 and 3, and they're the
   reason a normal IIS runbook fails on this box. Don't skip step 1 because it looks like boilerplate.

Everything below runs in an **elevated PowerShell** on the server — right-click → *Run as administrator*.

---

## Step 0 — Confirm the box is what we think it is

Thirty seconds, and it saves you from following a runbook written for the wrong machine.

```powershell
# OS version and edition
(Get-WmiObject Win32_OperatingSystem).Caption
(Get-WmiObject Win32_OperatingSystem).Version
# → Microsoft Windows Server 2012 R2 Standard  /  6.3.9600

# PowerShell version
$PSVersionTable.PSVersion
# → Major 4

# Is IIS here already, and which version?
(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\InetStp' -ErrorAction SilentlyContinue).VersionString
# → "Version 8.5"   = IIS is installed
# → nothing         = IIS is not installed (fine, step 2 handles it)
```

If `Version` comes back `6.3.9600`, you're on 2012 R2 and this runbook is correct for you. If it says
`10.0.x`, you're on 2016 or newer — **stop and use [Phase 14b](../plan-business-tutorial/14b-deploy-iis.md)
directly**, because steps 1 and 3 below are 2012 R2-specific workarounds you won't need.

---

## Step 1 — The Universal C Runtime (2012 R2 only)

**This is landmine #1, and it is the one that wastes an afternoon.** .NET 10 is built against the Universal
C Runtime, which shipped as standard from Windows 10 / Server 2016 onward. On 2012 R2 it's a separate
Windows update, and without it .NET does not start. The error you get names a DLL and nothing else:

```
The program can't start because api-ms-win-crt-runtime-l1-1-0.dll is missing from your computer.
```

Nothing in that message says "Windows update." Nothing points at IIS. Install it first and never meet it.

```powershell
# Is the UCRT already here?
Get-HotFix -Id KB2999226 -ErrorAction SilentlyContinue
# → a row = installed, skip the rest of this step
# → nothing = install it, below

# KB2999226 needs the April 2014 rollup first. Check for that too:
Get-HotFix -Id KB2919355 -ErrorAction SilentlyContinue
```

Install what's missing, **KB2919355 first if it's absent**:

- **KB2919355** (Windows 8.1 / Server 2012 R2 Update) — via Windows Update, or the Microsoft Update Catalog.
  Needs a reboot.
- **KB2999226** (Update for Universal C Runtime) —
  <https://www.microsoft.com/en-us/download/details.aspx?id=49063>. Take the **Windows Server 2012 R2 x64**
  package.

Re-run the `Get-HotFix` checks afterward. Both should return a row. Don't move on until they do — every
later step assumes .NET can actually start.

---

## Step 2 — Install IIS

`Install-WindowsFeature` exists on 2012 R2 and works exactly as you'd expect. Features already present are
left alone, so this is safe on a box that's half-configured.

```powershell
Install-WindowsFeature -Name Web-Server, Web-Mgmt-Console, Web-Scripting-Tools, Web-App-Init `
                       -IncludeManagementTools
```

You want `Success: True` and `ExitCode: Success`.

What each one is for, so you know what breaks if it's missing:

| Feature | Needed for |
|---|---|
| `Web-Server` | IIS itself |
| `Web-Scripting-Tools` | the `WebAdministration` PowerShell module — **step 6 dies at line 1 without it** |
| `Web-Mgmt-Console` | IIS Manager, the GUI. You have a desktop, so this is worth having |
| `Web-App-Init` | only if you want `preloadEnabled` (step 9). Harmless to install |

> **If this box ever hosted a WebForms app**, you'll see `Web-Asp-Net45` and `Web-Net-Ext45` already
> installed. Leave them. They do nothing for your app — that's the .NET Framework pipeline, and your app
> loads .NET 10 through a different mechanism entirely — but they're harmless and something else on the box
> may want them.

---

## Step 3 — The .NET 10 Hosting Bundle

**Landmine #2 is getting the file.** PowerShell 4.0 on 2012 R2 defaults to TLS 1.0 for web requests, and
Microsoft's download servers stopped accepting TLS 1.0 years ago. So the obvious command fails with an error
that sounds like a certificate problem and isn't:

```
Invoke-WebRequest : The request was aborted: Could not create SSL/TLS secure channel.
```

Force TLS 1.2 for the session first. This is per-session — open a new PowerShell and you must set it again:

```powershell
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

Invoke-WebRequest -Uri 'https://dotnet.microsoft.com/download/dotnet/10.0' -UseBasicParsing
```

The same limitation hits **Internet Explorer** on this box, so downloading through the browser may fail too.
If both fight you, download the installer on your dev machine and copy it over — that's a completely
legitimate answer and often the faster one.

You want the file named **`dotnet-hosting-10.x.x-win.exe`** — the "**ASP.NET Core Runtime … Hosting
Bundle**". Not the SDK, not the plain runtime. This one installer gives you the .NET runtime *plus* the
ASP.NET Core Module, which is the piece that lets IIS start a .NET app at all.

Run it, then restart IIS so it picks up the new native module:

```powershell
.\dotnet-hosting-10.0.0-win.exe          # accept the prompts

net stop was /y
net start w3svc
```

> **Order matters, and you just got it right.** The bundle registers its IIS module *into IIS*. If IIS isn't
> installed yet, it silently skips that part and you get a permanent, unexplained 500.19 later. You did IIS
> in step 2 and the bundle in step 3, so you're fine. **But** if this dev server already had .NET on it from
> some earlier project — installed back when IIS wasn't here — then that registration never happened. Step 4
> is what catches it. The fix is `.\dotnet-hosting-10.0.0-win.exe /repair`.

---

## Step 4 — Prove the server is ready

Run the whole block. This is the checkpoint that decides whether the next seven steps are pleasant.

```powershell
# 1) The ASP.NET Core Module file exists
Test-Path 'C:\Program Files\IIS\Asp.Net Core Module\V2\aspnetcorev2.dll'

# 2) ...and IIS actually has it registered
& "$env:windir\system32\inetsrv\appcmd.exe" list module /name:AspNetCoreModuleV2

# 3) The runtime is on the box
dotnet --list-runtimes | Select-String 'Microsoft.AspNetCore.App 10\.'

# 4) The management module loads (step 6 needs this)
Import-Module WebAdministration; Get-Command Get-Website | Select-Object Name, Source

# 5) What's already using port 80
Get-Website | Format-Table Name, ID, State, PhysicalPath,
    @{ n='Bindings'; e={ ($_.Bindings.Collection | ForEach-Object BindingInformation) -join '  ' } } -AutoSize
```

| # | You want | If not |
|---|---|---|
| 1 | `True` | The bundle didn't install. Redo step 3. |
| 2 | `MODULE "AspNetCoreModuleV2" ( native )` | **File there, registration missing** — the bundle-before-IIS case. Run `/repair`. |
| 3 | a `Microsoft.AspNetCore.App 10.0.x` line | Wrong bundle. You'd get a 500.31 later. |
| 4 | no error | `Web-Scripting-Tools` missing — back to step 2. |
| 5 | note anything on `*:80:` | Usually `Default Web Site`. Step 6 tells you what to do about it. |

**Check 1 printing `True` while check 2 prints nothing is the classic failure.** It means the runtime is
installed but IIS doesn't know about it. `/repair` the bundle and re-run.

---

## Step 5 — The one code change

Your `Program.cs` builds the database on every startup. On your laptop that's a convenience; here it's wrong
three ways — it needs `CREATE DATABASE` rights your app shouldn't hold, it re-runs on every app pool recycle
(including unattended ones at 3am), and two servers would race each other.

In [Program.cs:106](../course-validation/business/src/Erp.Api/Program.cs#L106), change this:

```csharp
await DatabaseInitializer.RunAsync(app);
```

to this:

```csharp
if (app.Environment.IsDevelopment())
    await DatabaseInitializer.RunAsync(app);
```

**That is the only line of application code this whole runbook touches.** Everything else is configuration
and server setup.

Because of that guard, your database schema is now *your* job. Run the module SQL scripts against the `Erp`
database once, as an admin, before the app starts. If the database doesn't exist yet on this dev server,
create it and run the scripts now — the app will no longer do it for you.

---

## Step 6 — Check your connection string before you trust it

You said your connection string works. **Please check which kind it is**, because one kind works when you
test it and then fails under IIS, and the failure looks like a permissions bug.

Look at the string:

- **Has `User Id=...;Password=...`** → SQL authentication. **You're fine.** The app authenticates as that SQL
  user no matter which Windows account it runs under. Skip to step 7.
- **Has `Trusted_Connection=True`** or `Integrated Security=true` → Windows authentication, and **this is
  the trap**. It works when *you* test it because it connects as *you*. Under IIS the app runs as a virtual
  account called `IIS APPPOOL\ErpApi`, which SQL Server has never heard of. You'll get
  `Login failed for user 'IIS APPPOOL\ErpApi'`.

If you're on Windows auth, run this against your `Erp` database as an admin **after step 7** (the account
doesn't exist until the app pool does):

```sql
CREATE LOGIN [IIS APPPOOL\ErpApi] FROM WINDOWS;
GO
USE Erp;
CREATE USER [IIS APPPOOL\ErpApi] FOR LOGIN [IIS APPPOOL\ErpApi];
GO
GRANT EXECUTE ON SCHEMA::sales      TO [IIS APPPOOL\ErpApi];
GRANT EXECUTE ON SCHEMA::inventory  TO [IIS APPPOOL\ErpApi];
GRANT EXECUTE ON SCHEMA::[identity] TO [IIS APPPOOL\ErpApi];
GO
```

Note there are **no table permissions** in there, and the app still works — every database call it makes goes
through a stored procedure, and SQL Server's ownership chaining does the rest. That's not a shortcut; it's
the payoff for the stored-procedure design.

> **If SQL Server is on a different machine** and you're using Windows auth, the login isn't
> `IIS APPPOOL\ErpApi` at all — that virtual account has no network identity, so the app goes out onto the
> network as the *machine account*, `DOMAIN\YOURSERVER$`. Use a domain service account as the pool identity
> instead; it's the sane answer for remote SQL.

---

## Step 7 — Publish, and set the server's config

Publishing produces a folder that **is** the deployable thing. Do this on your dev machine.

First, put the server's settings in the **project's** `web.config` — next to `Program.cs`, in
`src/Erp.Api/`. Create the file if it isn't there. The SDK merges this into the generated one on publish,
which is why editing the *published* `web.config` is pointless — the next publish overwrites it.

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <aspNetCore processPath="dotnet" arguments=".\Erp.Api.dll" hostingModel="inprocess">
        <environmentVariables>
          <environmentVariable name="ASPNETCORE_ENVIRONMENT"   value="Production" />
          <environmentVariable name="ConnectionStrings__ErpDb" value="YOUR CONNECTION STRING HERE" />
          <environmentVariable name="Jwt__SigningKey"          value="YOUR SIGNING KEY HERE" />
          <environmentVariable name="ApiKeys__web"             value="YOUR API KEY HERE" />
        </environmentVariables>
      </aspNetCore>
    </system.webServer>
  </location>
</configuration>
```

Three things about that file:

- **The double underscore is not a typo.** `ConnectionStrings__ErpDb` is how an environment variable spells
  `ConnectionStrings:ErpDb`. Your code doesn't change at all.
- **The signing key has to be here** (or on the app pool) because it lives in user-secrets on your laptop,
  and user-secrets only load in Development. There is no such thing as user-secrets on a server.
- **`ASPNETCORE_ENVIRONMENT=Production` is load-bearing.** Deploy with `Development` and your API cheerfully
  serves stack traces to anyone who triggers a 500, while looking perfectly healthy. It's a dev server so the
  stakes are low, but get in the habit — step 10 verifies it.

Now publish to a **staging** folder, not the live site:

```powershell
dotnet publish src/Erp.Api -c Release -o C:\deploy\ErpApi
```

Get that folder onto the server at `C:\deploy\ErpApi` however you like — file copy, share, RDP, zip. IIS
doesn't care. Staging matters because a running app holds its own DLLs locked; publishing straight into the
live site fails halfway and leaves you with half of two versions.

> **A secret in `web.config` is a secret in a file, in your deploy artifact.** Fine for a dev server, and
> it's where most IIS shops land. Just never let that file reach source control. The better spot is the app
> pool itself (IIS Manager → *Configuration Editor* → `system.applicationHost/applicationPools`), which keeps
> them out of the deploy folder entirely.

---

## Step 8 — App pool and site

**The app pool first, and the second line is the one that gets everybody:**

```powershell
Import-Module WebAdministration

New-WebAppPool -Name 'ErpApi'
Set-ItemProperty IIS:\AppPools\ErpApi -Name managedRuntimeVersion -Value ''    # = "No Managed Code"
```

> **"No Managed Code" — for an app made entirely of managed code.** Yes. Every instinct says .NET v4.0 and
> it is exactly backwards. That setting means "IIS, load the .NET *Framework* CLR into the worker process,"
> and your app doesn't want it — the ASP.NET Core Module loads .NET 10 itself. Leave it on v4.0 and you get a
> startup failure whose message never mentions the app pool. In the GUI it's *Basic Settings → .NET CLR
> version → No Managed Code*.

**Then the files and the site:**

```powershell
New-Item -ItemType Directory 'C:\inetpub\ErpApi' -Force
Copy-Item C:\deploy\ErpApi\* C:\inetpub\ErpApi\ -Recurse -Force

New-Website -Name 'ErpApi' -PhysicalPath 'C:\inetpub\ErpApi' -Port 80 -ApplicationPool 'ErpApi'
```

If step 4's check 5 showed something already on `*:80:` — almost always `Default Web Site` — that last line
fails with a binding conflict. On a dev server, just stop the other one:

```powershell
Stop-Website 'Default Web Site'
```

(Or use a different port: `New-Website ... -Port 8080`.)

**Then let the pool identity read its own folder:**

```powershell
icacls 'C:\inetpub\ErpApi' /grant 'IIS AppPool\ErpApi:(OI)(CI)RX' /T
```

Read is genuinely all it needs.

---

## Step 9 — Keep the pool alive

Skip this if you haven't deployed the refresh-token sweeper. If you have, read it — it's a silent failure.

By default an app pool **shuts down after 20 minutes with no requests**. On a dev server that nobody hits
overnight, that means your `BackgroundService` on its 6-hour timer simply never runs. No error. No log entry.
The `RefreshToken` table just grows.

The right fix is to move the schedule out of the app and into a SQL Agent job — the database is always
running and doesn't idle out. The expedient fix, if the work must stay in the app:

```powershell
Set-ItemProperty IIS:\AppPools\ErpApi -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
Set-ItemProperty IIS:\AppPools\ErpApi -Name recycling.periodicRestart.time -Value ([TimeSpan]::Zero)
Set-ItemProperty IIS:\AppPools\ErpApi -Name startMode -Value 'AlwaysRunning'
Set-ItemProperty 'IIS:\Sites\ErpApi' -Name applicationDefaults.preloadEnabled -Value $true
```

(That last line needs `Web-App-Init` from step 2.)

The lesson generalizes past IIS: **in-process background work has exactly the lifetime of its host process,
and on a web server you don't own that lifetime.**

---

## Step 10 — Start it and check it

Nothing starts your app until the first request arrives. That request is the test.

```powershell
Invoke-RestMethod http://localhost/health
# → healthy
```

The first one is slow — IIS is starting .NET inside the worker process. After that it's instant.

**If you get `healthy`, you're deployed.** Now confirm the config actually landed, because `/health` doesn't
touch the database:

```powershell
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12   # PS 4.0, again

$Base = 'http://localhost'
$Key  = 'YOUR API KEY'

# Login proves three things at once: SQL connection, SQL permissions, and the signing key
$body = @{ username = 'rep'; password = 'Password123!' } | ConvertTo-Json
$r = Invoke-WebRequest "$Base/auth/login" -Method Post -Body $body -ContentType 'application/json' `
        -Headers @{ 'X-Api-Key' = $Key } -UseBasicParsing
$r.StatusCode                                    # → 200
$token = ($r.Content | ConvertFrom-Json).accessToken
```

`-UseBasicParsing` is not optional on PowerShell 4.0 — without it, PowerShell tries to parse the response
with the Internet Explorer engine, and on a server where IE was never configured it throws *after* the
request already succeeded. Maddening symptom: failures report correctly, successes blow up.

**Then prove Production is real.** Trigger an error and read the body:

```powershell
$headers = @{ 'X-Api-Key' = $Key; 'Authorization' = "Bearer $token" }
try {
    Invoke-WebRequest "$Base/sales-orders/not-a-number" -Headers $headers -UseBasicParsing
} catch {
    (New-Object IO.StreamReader($_.Exception.Response.GetResponseStream())).ReadToEnd()
}
```

You want a bland message and **no stack trace**. A stack trace means `ASPNETCORE_ENVIRONMENT` didn't land —
back to step 7.

**Anything other than `healthy` at the top?** Go to the error table below. Don't guess between the numbers.

---

## Step 11 — Redeploying

The app is running, so IIS holds its DLLs locked, so a plain copy fails partway. Drop an `app_offline.htm`
in first — ANCM sees it, shuts the app down gracefully, and serves that file to everyone:

```powershell
'<h1>Erp API — back in a minute.</h1>' | Set-Content C:\inetpub\ErpApi\app_offline.htm
Copy-Item C:\deploy\ErpApi\* C:\inetpub\ErpApi\ -Recurse -Force
Remove-Item C:\inetpub\ErpApi\app_offline.htm
```

So a routine redeploy is: `dotnet publish` → get it to `C:\deploy\ErpApi` → those three lines. Steps 1–4, 6,
8 and 9 were one-time setup. You never repeat them.

---

## When it breaks — read the number

IIS errors are opaque but the number is a real diagnosis. Guessing between these turns five minutes into an
afternoon.

| Error | Means | Fix |
|---|---|---|
| **500.19** | IIS can't read `web.config` — usually ANCM isn't registered | step 3, `/repair` |
| **500.30** | Your app threw during startup — **this is your bug** | Event Viewer, below. Usually the connection string or a missing signing key (step 7) |
| **500.31** | Runtime version missing | wrong bundle — step 3 |
| **500.0** | App pool is on v4.0, not No Managed Code | step 8 |
| **200 on /health, 500 on everything else** | App started, database doesn't answer | steps 6 & 7 — look for `Login failed for user` |
| **`api-ms-win-crt-runtime-l1-1-0.dll` missing** | **The 2012 R2 one** — UCRT never installed | step 1 |

**Where the truth actually is, in order:**

1. **Event Viewer** → *Windows Logs → Application*, source **`IIS AspNetCoreModule V2`**. Startup crashes land
   here with the real exception. First place to look, and the one everybody forgets.
2. **The stdout log** — turn it on temporarily in the published `web.config`:
   ```xml
   <aspNetCore ... stdoutLogEnabled="true" stdoutLogFile=".\logs\stdout" />
   ```
   ```powershell
   New-Item -ItemType Directory C:\inetpub\ErpApi\logs
   icacls 'C:\inetpub\ErpApi\logs' /grant 'IIS AppPool\ErpApi:(OI)(CI)M'
   ```
   **Turn it back off when you're done.** It never rotates and will fill the disk given a few months.

---

## Appendix — if you really do need .NET 8

I'd push back on this (see the top of the file), but if something on this box forces .NET 8, here's the
honest scope.

**In this runbook, three things change:**

1. Step 3: download `dotnet-hosting-8.x.x-win.exe` instead of 10.
2. Step 4, check 3: `dotnet --list-runtimes | Select-String 'Microsoft.AspNetCore.App 8\.'`
3. Nothing else. IIS, the app pool, the module, `web.config`, the deploy — all identical.

**In the solution, it's a real port, not a flag flip:**

1. Retarget 7 `.csproj` files from `net10.0` to `net8.0`.
2. In `Directory.Packages.props`, drop every pinned 10.x package to its 8.x line — Carter, JwtBearer,
   the three `Microsoft.Extensions.*`, `Mvc.Testing`.
3. **Rewrite the Swagger block** in [Program.cs:18-50](../course-validation/business/src/Erp.Api/Program.cs#L18-L50).
   It uses the Swashbuckle 10 / OpenAPI.NET v2 API — `OpenApiSecuritySchemeReference` and the
   `AddSecurityRequirement(doc => …)` overload — and neither exists in the Swashbuckle 6.x line you'd land
   on. This is the part that actually costs you.
4. Re-run the 14 unit + 7 integration tests.

And it buys nothing on this OS: [.NET 8 carries the identical "supported with ESU" footnote for Server 2012
R2](https://github.com/dotnet/core/blob/main/release-notes/8.0/supported-os.md), and it reaches
[end of support in November 2026](https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/) —
before your OS's ESU window closes. You'd be porting backwards onto the thing that expires first.
