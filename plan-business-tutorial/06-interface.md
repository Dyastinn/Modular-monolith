# Lesson 06 — The Interface: A Blazor App in the Browser

Good day, developer! Our ERP has rules, a database and sign-in. But the only way to use it is `curl`, and no
sales rep will ever type `curl`. Today we give it a face: a real app in the browser, with a sign-in page, a
menu and a home screen. From now on, every lesson that adds a feature also adds its screen, so by the end of
the course the app is complete. Give yourself about an hour and a half. Are you ready? Let's begin!

## What we'll build

| Piece | What it is |
|---|---|
| `Erp.Web` | A **Blazor WebAssembly** app: C# that runs in the browser |
| MudBlazor | Ready-made screen components: tables, forms, menus, dialogs |
| Hosting | `Erp.Api` serves the app's files, from the same address as the API |
| `/api` | Every API route moves under one prefix, so the app's pages and the API never collide |
| Sign-in | A sign-in page using Identity's cookie, a layout with the user's menu, and a home page |

## Why we build it this way

### Which kind of front end

| Option | What it costs us |
|---|---|
| React, Angular or Vue | A second language (TypeScript), a second toolchain (npm), and DTOs written twice |
| Blazor Server | The screen lives on the server; every click is a message over a live connection, and every open browser holds server memory |
| **Blazor WebAssembly** | **C# in the browser, talking to our own API.** One language, one toolchain, and the server only answers API calls |

Blazor WebAssembly downloads the .NET runtime and our app into the browser once, then runs there. Now,
let's be honest about the cost: the first visit downloads a few megabytes, so the app takes a second or two
to start. After that, the browser caches it. For an ERP that staff open every morning and keep open all day,
that's a fine trade.

If you came from WebForms, think of each page as a `.aspx` page with its code-behind in the same file,
except the code runs in the browser and fetches its data from the API, instead of the server rebuilding the
page on every postback.

### Which components

Writing tables, date pickers and dialogs from scratch is weeks of work. **MudBlazor** gives us all of them,
in Material Design, under the MIT license. The well-known alternatives (Telerik, Syncfusion, DevExpress)
need a paid license per developer, and the buyer of this source would inherit that bill, so they're out.

### Same address, cookie sign-in

The app is served by `Erp.Api` itself, so the app and the API share one address. That makes sign-in simple
and safe:

- The browser signs in with Identity's **cookie** (lesson 05). The cookie is `HttpOnly`, so no script on
  the page can read it, not even ours.
- Because the app and API are the same site, the browser sends the cookie with every API call on its own.
  No token to store, refresh or leak.
- `SameSite=Strict` (lesson 05) means another website can't make the browser send it.

You might wonder about API keys, a common way to "protect" an API. A key that ships inside a browser app
is public: anyone can open the browser's developer tools and copy it. So we use none. Every call is
protected by who's signed in. To remember it easily: in a browser, the user is the key.

### One prefix for the API

Today the API answers at `/auth/me`, and soon at `/sales-orders`. But the app will want an address for its
own *page* of sales orders, and both can't be `/sales-orders`. So the whole API moves under `/api`, and
everything else belongs to the app.

## 1. Move the API under `/api`

Here's the end of `Program.cs` from lesson 05:

```csharp
app.MapIdentityModule();
app.MapCarter();
```

And here it is now:

```csharp
// The browser app is served from the same host, so the API gets a prefix of its own.
var api = app.MapGroup("/api");
api.MapIdentityModule();
api.MapCarter();
api.MapFallback(() => Results.NotFound());   // an unknown API address is a 404, never the app's page
```

`MapGroup("/api")` puts every route mapped through `api` under the prefix: `/auth/login` becomes
`/api/auth/login`, and every Carter module we write later lands under `/api` too, without changing a line
in it.

One thing breaks. The sign-up filter from lesson 05 checks for a path that *starts with* `/auth/register`,
and the path is now `/api/auth/register`. Open `IdentityModule.MapIdentityModule` and change the check:

```csharp
// before
ctx.HttpContext.Request.Path.StartsWithSegments("/auth/register")

// after: works under any prefix
ctx.HttpContext.Request.Path.Value?.EndsWith("/auth/register", StringComparison.OrdinalIgnoreCase) == true
```

This is exactly the kind of bug the "Let's run it!" checks catch, so don't skip them.

## 2. The app project

```bash
dotnet new blazorwasm --empty -o src/Erp.Web
dotnet sln add src/Erp.Web
```

The template gives us a project with versions on its packages, which central package management forbids,
and a sample page. Replace the generated `.csproj` with this:

```xml
<!-- src/Erp.Web/Erp.Web.csproj -->
<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">

  <!-- The browser app. It talks to Erp.Api over HTTP only: no project references to any module. -->
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly" />
    <PackageReference Include="Microsoft.AspNetCore.Components.Authorization" />
    <PackageReference Include="MudBlazor" />
  </ItemGroup>

  <!-- Role names are defined once, in BuildingBlocks. The file is compiled in here too, not copied. -->
  <ItemGroup>
    <Compile Include="..\Shared\Erp.Shared.BuildingBlocks\Abstractions\Roles.cs" Link="Shared\Roles.cs" />
  </ItemGroup>

</Project>
```

Delete the `Properties` folder and everything in `wwwroot` and `Pages`; we'll write our own. Then add the
three versions to `Directory.Packages.props`:

```xml
<PackageVersion Include="Microsoft.AspNetCore.Components.WebAssembly" Version="10.0.14" />      <!-- MIT -->
<PackageVersion Include="Microsoft.AspNetCore.Components.Authorization" Version="10.0.14" />    <!-- MIT -->
<PackageVersion Include="MudBlazor" Version="9.12.0" />                                         <!-- MIT -->
```

| Package | What it's for |
|---|---|
| `Components.WebAssembly` | Runs Blazor in the browser |
| `Components.Authorization` | `<AuthorizeView>` and `[Authorize]` for pages: show or hide things by sign-in and role |
| `MudBlazor` | The components |

Look at the `Compile Include ... Link` line. The app needs the role names to decide which menu items to
show, but it must not reference BuildingBlocks: that would pull Mediator and FluentValidation into the
browser. So it compiles that one file. The names still live in exactly one place.

Is the browser app allowed to hide things by role? Yes, as a courtesy. A hidden button isn't security. The
API checks every call on its own, and a rep who builds the URL by hand still gets a 403.

## 3. The page the browser loads first

```html
<!-- src/Erp.Web/wwwroot/index.html -->
<!DOCTYPE html>
<html lang="en">

<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>ERP</title>
    <base href="/" />
    <link rel="stylesheet" href="_content/MudBlazor/MudBlazor.min.css" />
    <link rel="stylesheet" href="css/app.css" />
</head>

<body>
    <div id="app">
        <div class="boot">Loading…</div>
    </div>

    <div id="blazor-error-ui">
        Something went wrong. <a href="." class="reload">Reload</a>
    </div>

    <!-- No inline scripts anywhere: the Content-Security-Policy forbids them. -->
    <script src="_framework/blazor.webassembly.js"></script>
    <script src="_content/MudBlazor/MudBlazor.min.js"></script>
</body>

</html>
```

Blazor replaces whatever is inside `<div id="app">` once it has started, so "Loading…" shows for the second
or two that takes. `blazor-error-ui` is the bar Blazor shows if the app crashes. And notice there's no
`<script>` with code inside it. [Lesson 22](22-security-hardening.md) adds a Content-Security-Policy that
forbids inline scripts, the classic way an attacker's script gets onto a page, so we never write one.

```css
/* src/Erp.Web/wwwroot/css/app.css */
html, body {
    font-family: system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif;
}

.boot {
    display: flex;
    align-items: center;
    justify-content: center;
    height: 100vh;
    color: #666;
}

#blazor-error-ui {
    display: none;
    position: fixed;
    bottom: 0;
    left: 0;
    right: 0;
    padding: 0.75rem 1.25rem;
    background: #fff3cd;
    box-shadow: 0 -1px 2px rgba(0, 0, 0, 0.2);
    z-index: 2000;
}
```

## 4. Who's signed in, according to the API

The browser can't read the cookie, so how does the app know who's signed in? It asks: `GET /api/auth/me`.
A 200 means signed in, and says as whom; a 401 means not. Blazor wants that answer from an
**`AuthenticationStateProvider`**, the one class every `<AuthorizeView>` and `[Authorize]` consults:

```csharp
// src/Erp.Web/Services/CookieAuthenticationStateProvider.cs
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Erp.Web.Services;

public sealed record CurrentUser(long Id, string Username, string Role);

// Who is signed in, according to the API. The cookie itself is HttpOnly: the browser app never sees it, it only
// asks /api/auth/me. Signing in and out go through Identity's own endpoints.
public sealed class CookieAuthenticationStateProvider(HttpClient http) : AuthenticationStateProvider
{
    private static readonly AuthenticationState SignedOut = new(new ClaimsPrincipal(new ClaimsIdentity()));
    private CurrentUser? _user;
    private bool _loaded;

    public CurrentUser? User => _user;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (!_loaded)
        {
            var response = await http.GetAsync("api/auth/me");
            _user = response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<CurrentUser>() : null;
            _loaded = true;
        }
        return _user is null ? SignedOut : new AuthenticationState(Principal(_user));
    }

    // null = signed in; otherwise the reason, in words a person can act on.
    public async Task<string?> SignInAsync(string email, string password)
    {
        var response = await http.PostAsJsonAsync("api/auth/login?useCookies=true", new { email, password });
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            return body.Contains("LockedOut")
                ? "This account is locked for 15 minutes after too many wrong passwords."
                : "Wrong email or password.";
        }
        Refresh();
        return null;
    }

    public async Task SignOutAsync(bool everywhere = false)
    {
        await http.PostAsync(everywhere ? "api/auth/logout-everywhere" : "api/auth/logout", null);
        Refresh();
    }

    // Forget what we knew and ask the API again (after sign-in, sign-out, or a 401).
    public void Refresh()
    {
        _loaded = false;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    private static ClaimsPrincipal Principal(CurrentUser user) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.Username),
        new Claim(ClaimTypes.Role, user.Role),
    ], authenticationType: "cookie", ClaimTypes.Name, ClaimTypes.Role));
}
```

- **`?useCookies=true`** tells Identity's login endpoint to answer with a cookie instead of tokens.
- **It asks once**, then remembers (`_loaded`). `Refresh()` forgets and asks again, and
  `NotifyAuthenticationStateChanged` tells every `<AuthorizeView>` on screen to redraw.
- **The claims** turn the API's answer into the same shape the server uses, so `<AuthorizeView
  Roles="...">` works.
- **Wrong password and unknown email get the same message.** Saying "no such user" would tell an attacker
  which emails are real.

## 5. Start the app

```csharp
// src/Erp.Web/Program.cs
using Erp.Web;
using Erp.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Same origin as the API, so the browser sends the sign-in cookie with every call on its own.
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<CookieAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<CookieAuthenticationStateProvider>());

builder.Services.AddMudServices();

await builder.Build().RunAsync();
```

It's the same dependency injection you know from `Erp.Api`, running in the browser. In a browser app,
`Scoped` means "one for as long as the tab is open". The provider is registered twice on purpose: once as
itself, so the sign-in page can call `SignInAsync`, and once as the `AuthenticationStateProvider` Blazor
looks for. Both registrations hand out the same object.

`HeadOutlet` lets each page set the browser tab's title with `<PageTitle>`.

```razor
@* src/Erp.Web/_Imports.razor *@
@using System.Net.Http
@using System.Net.Http.Json
@using Microsoft.AspNetCore.Authorization
@using Microsoft.AspNetCore.Components.Authorization
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using Microsoft.AspNetCore.Components.WebAssembly.Http
@using MudBlazor
@using Erp.Shared.BuildingBlocks
@using Erp.Web
@using Erp.Web.Layout
@using Erp.Web.Services
@using Erp.Web.Shared
@attribute [Authorize]
```

`_Imports.razor` applies to every `.razor` file in the project. The last line is the important one:
**every page requires a signed-in user** unless it says otherwise with `[AllowAnonymous]`. Forgetting to
protect a page is impossible; forgetting to *unprotect* the sign-in page is a mistake you'll notice in
seconds.

## 6. The router

```razor
@* src/Erp.Web/App.razor *@
@* Every page needs a signed-in user (the [Authorize] in _Imports), unless it says [AllowAnonymous]. *@
<Router AppAssembly="typeof(App).Assembly" NotFoundPage="typeof(Pages.NotFound)">
    <Found Context="routeData">
        <AuthorizeRouteView RouteData="routeData" DefaultLayout="typeof(MainLayout)">
            <NotAuthorized>
                @if (context.User.Identity?.IsAuthenticated == true)
                {
                    <MudAlert Severity="Severity.Warning" Class="ma-6">You don't have access to this page.</MudAlert>
                }
                else
                {
                    <RedirectToLogin />
                }
            </NotAuthorized>
            <Authorizing>
                <MudProgressLinear Indeterminate="true" Color="Color.Primary" />
            </Authorizing>
        </AuthorizeRouteView>
        <FocusOnNavigate RouteData="routeData" Selector="h1" />
    </Found>
</Router>
```

The **router** looks at the address and picks the page whose `@page` matches. **`AuthorizeRouteView`**
checks the page's `[Authorize]` first. Not signed in? Off to the sign-in page. Signed in but the wrong role?
A polite "no access" message.

```razor
@* src/Erp.Web/Shared/RedirectToLogin.razor *@
@attribute [AllowAnonymous]
@inject NavigationManager Nav

@code {
    protected override void OnInitialized()
    {
        var back = Nav.ToBaseRelativePath(Nav.Uri);
        Nav.NavigateTo(string.IsNullOrEmpty(back) ? "login" : $"login?returnUrl={Uri.EscapeDataString(back)}");
    }
}
```

It remembers where you were going, so after signing in you land there instead of on the home page.

```razor
@* src/Erp.Web/Pages/NotFound.razor *@
@page "/not-found"
@attribute [AllowAnonymous]

<PageTitle>Not found · ERP</PageTitle>

<MudAlert Severity="Severity.Info" Class="ma-6">There's nothing at this address. <MudLink Href="">Go home</MudLink></MudAlert>
```

## 7. The sign-in page

```razor
@* src/Erp.Web/Pages/Login.razor *@
@page "/login"
@attribute [AllowAnonymous]
@inject CookieAuthenticationStateProvider Auth
@inject NavigationManager Nav

<PageTitle>Sign in · ERP</PageTitle>

<MudContainer MaxWidth="MaxWidth.ExtraSmall" Class="mt-16">
    <MudPaper Class="pa-8" Elevation="2">
        <MudText Typo="Typo.h5" GutterBottom="true">Sign in</MudText>
        <EditForm Model="this" OnSubmit="SignIn">
            <MudStack Spacing="3">
                <MudTextField @bind-Value="_email" Label="Email" InputType="InputType.Email" AutoFocus="true" Required="true" />
                <MudTextField @bind-Value="_password" Label="Password" InputType="InputType.Password" Required="true" />
                @if (_error is not null)
                {
                    <MudAlert Severity="Severity.Error" Dense="true">@_error</MudAlert>
                }
                <MudButton ButtonType="ButtonType.Submit" Variant="Variant.Filled" Color="Color.Primary" Disabled="_busy">Sign in</MudButton>
            </MudStack>
        </EditForm>
    </MudPaper>
</MudContainer>

@code {
    [SupplyParameterFromQuery] public string? ReturnUrl { get; set; }

    private string _email = "", _password = "";
    private string? _error;
    private bool _busy;

    private async Task SignIn()
    {
        _busy = true;
        _error = await Auth.SignInAsync(_email.Trim(), _password);
        _busy = false;
        // Only ever go back inside this app: an outside returnUrl would make this page an open redirect.
        if (_error is null)
            Nav.NavigateTo(ReturnUrl is { } back && !back.Contains("//") && !back.Contains(':') ? back : "");
    }
}
```

A Razor component is markup on top and C# in `@code` below. `@bind-Value` ties a text field to a field in
the code both ways: type in the box, and `_email` changes. `EditForm` with a submit button means pressing
Enter signs in.

Look at the last line. Without that check, an attacker could send someone a link to
`/login?returnUrl=https://evil.example`, and after a real sign-in on our real site, the user would land on
the attacker's look-alike page. That trick is called an **open redirect**. We only follow paths inside our
own app.

## 8. The layout, the menu and the home page

The **layout** is the frame around every page: the top bar, the menu on the left, and the page in the
middle (`@Body`). If you came from WebForms, it's the master page.

```razor
@* src/Erp.Web/Layout/MainLayout.razor *@
@inherits LayoutComponentBase
@inject CookieAuthenticationStateProvider Auth
@inject NavigationManager Nav

<MudThemeProvider Theme="Theme" />
<MudPopoverProvider />

<AuthorizeView>
    <Authorized>
        <MudLayout>
            <MudAppBar Elevation="1" Dense="true">
                <MudIconButton Icon="@Icons.Material.Filled.Menu" Color="Color.Inherit" Edge="Edge.Start"
                               OnClick="() => _drawerOpen = !_drawerOpen" aria-label="Menu" />
                <MudText Typo="Typo.h6" Class="ml-2">ERP</MudText>
                <MudSpacer />
                <MudMenu Icon="@Icons.Material.Filled.AccountCircle" Color="Color.Inherit" AnchorOrigin="Origin.BottomRight"
                         aria-label="Account">
                    <MudText Typo="Typo.body2" Class="px-4 pt-2">@context.User.Identity?.Name</MudText>
                    <MudText Typo="Typo.caption" Class="px-4 pb-2">@RoleOf(context)</MudText>
                    <MudDivider />
                    <MudMenuItem OnClick="() => SignOut(false)">Sign out</MudMenuItem>
                    <MudMenuItem OnClick="() => SignOut(true)">Sign out everywhere</MudMenuItem>
                </MudMenu>
            </MudAppBar>
            <MudDrawer @bind-Open="_drawerOpen" ClipMode="DrawerClipMode.Always" Elevation="1">
                <NavMenu />
            </MudDrawer>
            <MudMainContent>
                <MudContainer MaxWidth="MaxWidth.ExtraLarge" Class="py-6">
                    @Body
                </MudContainer>
            </MudMainContent>
        </MudLayout>
    </Authorized>
    <NotAuthorized>
        @Body
    </NotAuthorized>
</AuthorizeView>

@code {
    private bool _drawerOpen = true;

    private static readonly MudTheme Theme = new()
    {
        PaletteLight = new PaletteLight { Primary = "#1e5aa8", AppbarBackground = "#1e5aa8" }
    };

    private static string RoleOf(AuthenticationState state) =>
        state.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";

    private async Task SignOut(bool everywhere)
    {
        await Auth.SignOutAsync(everywhere);
        Nav.NavigateTo("login");
    }
}
```

- **`<AuthorizeView>`** shows `<Authorized>` to a signed-in user and `<NotAuthorized>` to everyone else, so
  the sign-in page appears without the menu around it. Inside it, `context` is the signed-in user.
- **`MudThemeProvider`** applies the colors; **`MudPopoverProvider`** is where MudBlazor draws anything
  that pops up, like the account menu. Both appear once, here.
- **Sign out everywhere** is the endpoint from lesson 05, finally one click away.

```razor
@* src/Erp.Web/Layout/NavMenu.razor *@
@* Each person sees the parts of the system their role uses. The API enforces the same rules; hiding a link is
   only a convenience. *@
<MudNavMenu>
    <MudNavLink Href="" Match="NavLinkMatch.All" Icon="@Icons.Material.Filled.Dashboard">Home</MudNavLink>
</MudNavMenu>
```

One link for now. Every feature lesson adds its own.

```razor
@* src/Erp.Web/Pages/Home.razor *@
@page "/"

<PageTitle>Home · ERP</PageTitle>

<AuthorizeView Context="auth">
    <MudText Typo="Typo.h4" GutterBottom="true">Good day, @auth.User.Identity?.Name</MudText>
    <MudText Class="mb-6 mud-text-secondary">Here's where your work starts.</MudText>
</AuthorizeView>
```

## 9. Let the API serve the app

`Erp.Api` needs the app's files, and one package that knows how to serve them:

```bash
dotnet add src/Erp.Api package Microsoft.AspNetCore.Components.WebAssembly.Server --version 10.0.14
```

And a reference to the app project, in `Erp.Api.csproj`:

```xml
<!-- The browser app's files, served by this host from the same address as the API. Its code is not referenced. -->
<ProjectReference Include="..\Erp.Web\Erp.Web.csproj" ReferenceOutputAssembly="false" />
```

`ReferenceOutputAssembly="false"` means "build it and copy its files, but don't let me use its classes".
The host never calls the app's code. And without it, `Roles` would exist twice, once in BuildingBlocks and
once in `Erp.Web`, and the compiler would refuse with `CS0433`.

At the end of `Program.cs`, after the `/api` group:

```csharp
app.MapStaticAssets();
app.MapFallbackToFile("index.html");          // any other address is a page of the app: let it route
```

**`MapStaticAssets`** serves the app's files: the `.wasm` runtime, our compiled app, MudBlazor's CSS. It
compresses them at build time and gives them cache headers, so a returning user downloads nothing.
**`MapFallbackToFile`** answers every other address with `index.html`. Type
`http://localhost:5264/sales-orders` into the address bar, and the server has no such file, so it sends the
app, and the app's router shows the right page.

The order of the three fallbacks matters. `/api/anything-unknown` hits the API's own fallback first and gets
an honest 404 instead of a page of HTML, which would confuse every API client.

## How a request flows

```mermaid
sequenceDiagram
  participant B as Browser
  participant H as Erp.Api
  B->>H: GET /sales-orders
  H-->>B: index.html (fallback)
  B->>H: GET /_framework/... (cached after the first visit)
  Note over B: The app starts; the router picks the page
  B->>H: GET /api/auth/me
  H-->>B: 401
  Note over B: RedirectToLogin → /login?returnUrl=sales-orders
  B->>H: POST /api/auth/login?useCookies=true
  H-->>B: 200 + Set-Cookie (HttpOnly, SameSite=Strict)
  B->>H: GET /api/auth/me (cookie sent automatically)
  H-->>B: 200 { id, username, role }
```

## Mistakes to avoid

- An API key in the browser app. Anything shipped to a browser is public.
- Treating a hidden button as security. The API must check every call; hiding is for convenience.
- Calling `UseBlazorFrameworkFiles()` from older tutorials as well as `MapStaticAssets()`. In .NET 10 they
  serve the same files twice and `/_framework` breaks with a 500. `MapStaticAssets` alone is right.
- Forgetting `api.MapFallback`. A typo in an API path then returns the app's HTML with a 200, and the
  client fails somewhere confusing.
- Trusting `returnUrl`. Only follow paths inside your own app.
- Inline `<script>` in `index.html`. The Content-Security-Policy in lesson 22 will block it.

## Let's run it!

```bash
dotnet run --project src/Erp.Api
```

Open **http://localhost:5264** in your browser. You land on the sign-in page. Sign in as `rep@erp.local`
with `Password123!`, and you see "Good day, rep@erp.local". Open the account menu at the top right: your
role says `SalesRep`. Sign out, and you're back at the sign-in page.

Now type a wrong password five times as a user you don't need (or skip it; lesson 13 tests lockout). And
check that the API and the pages each answer their own way:

```bash
curl -i http://localhost:5264/api/auth/me          # 401: not signed in
curl -i http://localhost:5264/api/no-such-thing    # 404: an API address that doesn't exist
curl -i http://localhost:5264/sales-orders         # 200 and HTML: a page of the app
curl -i -X POST http://localhost:5264/api/auth/register -H "Content-Type: application/json" \
  -d '{"email":"intruder@example.com","password":"LongEnoughPassword1"}'   # 404: sign-up is still blocked
```

That last one is the regression check for the filter we fixed in step 1. If it answers 200, the fix didn't
land.

## Let's check!

1. Why did we choose Blazor WebAssembly over Blazor Server, and what does it cost on the first visit?
2. Why doesn't the app use an API key, and what protects each API call instead?
3. The browser app can't read the sign-in cookie. How does it know who's signed in?
4. Why did the API move under `/api`, and what happens to `GET /api/typo`?
5. What is an open redirect, and which line on the sign-in page prevents it?
6. Why does `Erp.Api` reference `Erp.Web` with `ReferenceOutputAssembly="false"`?

And that's where this lesson ends. The ERP now has a face: a sign-in page, a menu and a home screen, served
by our own API. Next time, we build our first real feature from end to end, from the handler to its screen:
creating a sales order. Good luck, developer!

---

← [Lesson 05 — Sign-in](05-sign-in.md) · Next: [Lesson 07 — Create an order](07-create-order.md) →
