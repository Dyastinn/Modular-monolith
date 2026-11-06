# Lesson 15 — Users and Email: Staff Accounts an Admin Runs

Good day, developer! Our ERP still has exactly three users, and they were born in a seed script. A real
company hires a warehouse clerk on Monday, loses a password on Tuesday, and has someone leave on Friday.
Today an administrator gets a screen for all of that, three new roles join the company, every sign-in
attempt is written down, and a forgotten password can be reset by email. Identity does the dangerous parts,
as always; we only fill the gaps it leaves. About two hours. Are you ready? Let's begin!

## What we'll build

| Endpoint | Who | What it does |
|---|---|---|
| `GET /api/users`, `POST /api/users`, `PUT /api/users/{id}` | Admin | List, create (with a temporary password), rename or change role |
| `POST /api/users/{id}/disable` and `/enable` | Admin | Someone left; someone came back |
| `POST /api/users/{id}/reset-password` | Admin | Set a new temporary password, and unlock the account |
| `POST /api/auth/change-password` | Anyone signed in | Choose your own password |
| `GET /api/sign-in-attempts` | Admin | Who tried to sign in, when, from where, and what happened |
| `GET /api/users/directory` | Anyone signed in | Names for ids: "approved by Manuel Santos" |

Plus three roles (InventoryClerk, Purchaser, Accountant), password-reset email with **MailKit**, and the
screens: Users, Sign-in log, Change password and Forgot password.

## Why we build it this way

### Self sign-up stays blocked

Lesson 05 blocked `/auth/register`, and it stays blocked. Staff accounts are made by an administrator, who
also decides the role. The admin types a **temporary password**, tells it to the person, and the app makes
them choose their own at first sign-in. That way the admin never knows anyone's real password.

### Disable, never delete

When someone leaves, their account must stop working immediately. But their name is on hundreds of orders,
approvals and shipments. Deleting the user would leave "approved by user 7" pointing at nobody. So we
**disable**: they can't sign in, and their history stays readable.

### The rules that stop a company locking itself out

| Rule | Why |
|---|---|
| You can't disable yourself | One wrong click, and you're out |
| You can't change your own role | An admin can't quietly demote themselves into a lockout |
| The last active admin can't be disabled or demoted | Then nobody could manage users ever again |

These are business rules, so they live in a small domain class with unit tests, like every other rule.

### Extend Identity, don't replace it

Identity has documented places to extend it. We use exactly two:

- **`SignInManager.CanSignInAsync`** decides whether a user may sign in at all. We add "and isn't
  disabled".
- **`SignInManager.PasswordSignInAsync`** is the password check. We let Identity do it, then write one row
  saying what happened.

Hashing, lockout, cookies and tokens stay entirely Identity's. That's lesson 05's rule: library code for
security, never our own.

### When does a change take effect?

A user's cookie says who they are and what role they have. If an admin disables them, their *existing*
cookie still works until it's checked again. Identity checks it against the user's **security stamp**, a
random value that changes whenever something security-related changes, every 30 minutes by default. We set
that to **one minute**. Now, let's be honest about the cost: each check is one small database query, once
a minute per signed-in browser. For an office of fifty people, that's nothing.

## 1. Six roles

In BuildingBlocks' `Roles.cs`, after:

```csharp
// The role names, defined once. A typo in a role string silently breaks an auth check, so nobody types them.
public static class Roles
{
    public const string SalesRep = "SalesRep";
    public const string SalesManager = "SalesManager";
    public const string Admin = "Admin";
    public const string InventoryClerk = "InventoryClerk";
    public const string Purchaser = "Purchaser";
    public const string Accountant = "Accountant";

    public static readonly string[] All = [SalesRep, SalesManager, Admin, InventoryClerk, Purchaser, Accountant];
}
```

| Role | Does |
|---|---|
| InventoryClerk | Keeps the item catalog and stock, ships orders, receives deliveries |
| Purchaser | Buys from suppliers |
| Accountant | Invoices, payments, receivables |

The roles table gets them too. In `ErpIdentityDbContext`, the `HasData` line grows:

```csharp
b.Entity<IdentityRole<long>>().ToTable("roles").HasData(
    Role(1, AppRoles.SalesRep), Role(2, AppRoles.SalesManager), Role(3, AppRoles.Admin),
    Role(4, AppRoles.InventoryClerk), Role(5, AppRoles.Purchaser), Role(6, AppRoles.Accountant));
```

The ids are fixed and only ever added to, so a migration can insert the new rows without touching the old
ones.

## 2. The user grows

```csharp
// src/Modules/Identity/Erp.Modules.Identity/Infrastructure/AppUser.cs
using Microsoft.AspNetCore.Identity;

namespace Erp.Modules.Identity.Infrastructure;

// Identity's user, plus the three things an ERP's staff list needs.
internal sealed class AppUser : IdentityUser<long>
{
    public string FullName { get; set; } = "";
    public bool IsDisabled { get; set; }            // left the company: can't sign in, history stays
    public bool MustChangePassword { get; set; }    // set by an admin-chosen temporary password
}
```

Lesson 05 said `AppUser` existed "so we have our own type to grow later". This is later. Public setters are
fine here: `AppUser` isn't a domain aggregate, it's Identity's storage record, and `UserManager` is its
gatekeeper.

And the record of every sign-in:

```csharp
// src/Modules/Identity/Erp.Modules.Identity/Infrastructure/SignInAttempt.cs
namespace Erp.Modules.Identity.Infrastructure;

// One row per password sign-in, successful or not: who, from where, and what happened. Never changed.
internal sealed class SignInAttempt
{
    public long Id { get; private set; }
    public string Email { get; private set; } = default!;
    public long? UserId { get; private set; }   // null when the email matched nobody
    public string Outcome { get; private set; } = default!;
    public string? IpAddress { get; private set; }
    public DateTime OccurredUtc { get; private set; }

    private SignInAttempt() { }

    public SignInAttempt(string email, long? userId, string outcome, string? ipAddress, DateTime occurredUtc)
    { Email = email; UserId = userId; Outcome = outcome; IpAddress = ipAddress; OccurredUtc = occurredUtc; }
}
```

In `ErpIdentityDbContext`, add `public DbSet<SignInAttempt> SignInAttempts => Set<SignInAttempt>();` and map
it next to the outbox:

```csharp
b.Entity<SignInAttempt>(e =>
{
    e.ToTable("sign_in_attempt");
    e.HasKey(x => x.Id);
    e.Property(x => x.Id).UseIdentityColumn();
    e.Property(x => x.Email).HasMaxLength(256);
    e.Property(x => x.Outcome).HasMaxLength(30);
    e.Property(x => x.IpAddress).HasMaxLength(45);
    e.HasIndex(x => x.OccurredUtc);
    e.HasIndex(x => x.Email);
});
```

`45` is the longest an IP address can be written (an IPv6 address with an IPv4 tail).

## 3. The account rules

```csharp
// src/Modules/Identity/Erp.Modules.Identity/Domain/StaffAccountRules.cs
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Identity.Domain;

// What an administrator may do to a staff account. The rules that stop a company locking itself out.
internal static class StaffAccountRules
{
    public static void EnsureRoleExists(string role)
    {
        if (!Roles.All.Contains(role)) throw new DomainException($"'{role}' is not a role.");
    }

    public static void EnsureCanChangeRole(long actorId, long targetId, string currentRole, string newRole, int activeAdmins)
    {
        EnsureRoleExists(newRole);
        if (currentRole == newRole) return;
        if (actorId == targetId) throw new DomainException("You can't change your own role. Ask another admin.");
        EnsureNotLastAdmin(currentRole, activeAdmins);
    }

    public static void EnsureCanDisable(long actorId, long targetId, string targetRole, int activeAdmins)
    {
        if (actorId == targetId) throw new DomainException("You can't disable your own account.");
        EnsureNotLastAdmin(targetRole, activeAdmins);
    }

    private static void EnsureNotLastAdmin(string role, int activeAdmins)
    {
        if (role == Roles.Admin && activeAdmins <= 1)
            throw new DomainException("This is the last active admin. Make someone else an admin first.");
    }
}
```

It takes plain numbers and strings, not `AppUser`, so it stays pure: no Identity, no database, testable in
milliseconds. The handler looks the facts up; the rules decide.

A test project for the Identity module, the same way as lesson 03's (delete the template's
`<PackageReference>` lines first):

```bash
dotnet new xunit -o tests/Erp.Modules.Identity.UnitTests
dotnet sln add tests/Erp.Modules.Identity.UnitTests
dotnet add tests/Erp.Modules.Identity.UnitTests reference src/Modules/Identity/Erp.Modules.Identity
dotnet add tests/Erp.Modules.Identity.UnitTests package Microsoft.NET.Test.Sdk --version 17.14.1
dotnet add tests/Erp.Modules.Identity.UnitTests package xunit --version 2.9.3
dotnet add tests/Erp.Modules.Identity.UnitTests package xunit.runner.visualstudio --version 3.1.4
dotnet add tests/Erp.Modules.Identity.UnitTests package AwesomeAssertions --version 9.6.0
```

and `<InternalsVisibleTo Include="Erp.Modules.Identity.UnitTests" />` in `Erp.Modules.Identity.csproj`.

```csharp
// tests/Erp.Modules.Identity.UnitTests/StaffAccountRulesTests.cs
using AwesomeAssertions;
using Erp.Modules.Identity.Domain;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Identity.UnitTests;

public class StaffAccountRulesTests
{
    private const long Me = 3, Someone = 7;

    [Fact]
    public void Nobody_disables_their_own_account() =>
        FluentActions.Invoking(() => StaffAccountRules.EnsureCanDisable(Me, Me, Roles.Admin, activeAdmins: 2))
            .Should().Throw<DomainException>().WithMessage("*your own*");

    [Fact]
    public void The_last_admin_stays()
    {
        FluentActions.Invoking(() => StaffAccountRules.EnsureCanDisable(Me, Someone, Roles.Admin, activeAdmins: 1))
            .Should().Throw<DomainException>().WithMessage("*last active admin*");
        FluentActions.Invoking(() => StaffAccountRules.EnsureCanChangeRole(Me, Someone, Roles.Admin, Roles.SalesRep, activeAdmins: 1))
            .Should().Throw<DomainException>();
        FluentActions.Invoking(() => StaffAccountRules.EnsureCanDisable(Me, Someone, Roles.Admin, activeAdmins: 2))
            .Should().NotThrow();
    }

    [Fact]
    public void Nobody_changes_their_own_role() =>
        FluentActions.Invoking(() => StaffAccountRules.EnsureCanChangeRole(Me, Me, Roles.Admin, Roles.SalesRep, activeAdmins: 3))
            .Should().Throw<DomainException>().WithMessage("*your own role*");

    [Fact]
    public void Only_real_roles_are_accepted() =>
        FluentActions.Invoking(() => StaffAccountRules.EnsureRoleExists("SuperUser")).Should().Throw<DomainException>();
}
```

## 4. Disabled users can't sign in, and every attempt is recorded

```csharp
// src/Modules/Identity/Erp.Modules.Identity/Infrastructure/ErpSignInManager.cs
using Erp.Shared.BuildingBlocks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Erp.Modules.Identity.Infrastructure;

// Identity's own sign-in, extended at its two documented hooks: a disabled account may not sign in, and every
// password attempt is recorded. Hashing, lockout and tokens stay entirely Identity's.
internal sealed class ErpSignInManager(
    UserManager<AppUser> users,
    IHttpContextAccessor context,
    IUserClaimsPrincipalFactory<AppUser> claims,
    IOptions<IdentityOptions> options,
    ILogger<SignInManager<AppUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<AppUser> confirmation,
    ErpIdentityDbContext db,
    IClock clock)
    : SignInManager<AppUser>(users, context, claims, options, logger, schemes, confirmation)
{
    public override async Task<bool> CanSignInAsync(AppUser user) =>
        !user.IsDisabled && await base.CanSignInAsync(user);

    public override async Task<SignInResult> PasswordSignInAsync(string userName, string password, bool isPersistent, bool lockoutOnFailure)
    {
        var user = await UserManager.FindByNameAsync(userName);
        var result = user is null
            ? SignInResult.Failed
            : await PasswordSignInAsync(user, password, isPersistent, lockoutOnFailure);

        var outcome = result.Succeeded ? "Succeeded"
            : result.IsLockedOut ? "LockedOut"
            : result.IsNotAllowed ? "NotAllowed"
            : result.RequiresTwoFactor ? "RequiresTwoFactor"
            : "InvalidCredentials";
        db.SignInAttempts.Add(new SignInAttempt(userName, user?.Id, outcome,
            Context.Connection.RemoteIpAddress?.ToString(), clock.UtcNow));
        await db.SaveChangesAsync();
        return result;
    }
}
```

The long constructor is just Identity's own, passed straight through, plus our two extras (`db` and
`clock`). When `CanSignInAsync` says no, Identity answers the login with "NotAllowed", the outcome we
record.

Register it in `IdentityModule`, at the end of the `AddIdentityApiEndpoints` chain:

```csharp
.AddEntityFrameworkStores<ErpIdentityDbContext>()
.AddSignInManager<ErpSignInManager>();
```

and right below, the one-minute security-stamp check:

```csharp
// How soon a disabled account or a changed role ends a browser session (each check is one query).
services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));
```

Then one migration for the new columns, the sign-in table and the three roles:

```bash
dotnet ef migrations add AddStaffAccounts \
  --project src/Modules/Identity/Erp.Modules.Identity --startup-project src/Modules/Identity/Erp.Modules.Identity \
  --output-dir Infrastructure/Migrations
```

## 5. Managing users

Identity reports a refused password ("too short") or a taken email as an `IdentityResult` rather than an
exception. Three small helpers turn that into our usual 422, and answer two questions every handler asks:

```csharp
// src/Modules/Identity/Erp.Modules.Identity/Infrastructure/IdentityResults.cs
using Erp.Shared.BuildingBlocks;
using Microsoft.AspNetCore.Identity;

namespace Erp.Modules.Identity.Infrastructure;

internal static class IdentityResults
{
    // Identity reports rule failures ("password too short", "email taken") as results; the API answers them as 422.
    public static void ThrowIfFailed(this IdentityResult result)
    {
        if (!result.Succeeded)
            throw new DomainException(string.Join(" ", result.Errors.Select(e => e.Description)));
    }

    public static async Task<int> CountActiveAdminsAsync(this UserManager<AppUser> users) =>
        (await users.GetUsersInRoleAsync(Roles.Admin)).Count(u => !u.IsDisabled);

    public static async Task<string> GetRoleAsync(this UserManager<AppUser> users, AppUser user) =>
        (await users.GetRolesAsync(user)).SingleOrDefault() ?? "";
}
```

Users are written through `UserManager`, never EF directly: it keeps the normalized email, the security
stamp and the password hash right.

```csharp
// src/Modules/Identity/Erp.Modules.Identity/Features/CreateUser/CreateUserHandler.cs
using Erp.Modules.Identity.Domain;
using Erp.Modules.Identity.Infrastructure;
using FluentValidation;
using Mediator;
using Microsoft.AspNetCore.Identity;

namespace Erp.Modules.Identity.Features;

internal sealed record CreateUserCommand(string Email, string FullName, string Role, string TemporaryPassword) : ICommand<long>;

internal sealed class CreateUserValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Role).NotEmpty();
        RuleFor(x => x.TemporaryPassword).NotEmpty();
    }
}

// Staff accounts are made by an admin, with a temporary password the person must change at first sign-in.
internal sealed class CreateUserHandler(UserManager<AppUser> users) : ICommandHandler<CreateUserCommand, long>
{
    public async ValueTask<long> Handle(CreateUserCommand cmd, CancellationToken ct)
    {
        StaffAccountRules.EnsureRoleExists(cmd.Role);

        var email = cmd.Email.Trim();
        var user = new AppUser
        {
            UserName = email, Email = email, EmailConfirmed = true, FullName = cmd.FullName.Trim(), MustChangePassword = true
        };
        (await users.CreateAsync(user, cmd.TemporaryPassword)).ThrowIfFailed();
        (await users.AddToRoleAsync(user, cmd.Role)).ThrowIfFailed();
        return user.Id;
    }
}
```

The validator only checks that a password was given. Whether it's long enough is Identity's password
policy from lesson 05, and `CreateAsync` enforces it; writing the rule twice would let them drift.

```csharp
// src/Modules/Identity/Erp.Modules.Identity/Features/CreateUser/CreateUserEndpoint.cs
using Carter;
using Erp.Shared.BuildingBlocks;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Identity.Features;

internal sealed class CreateUserEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/users", async (CreateUserCommand body, ISender sender, CancellationToken ct) =>
            {
                var id = await sender.Send(body, ct);
                return Results.Created($"/api/users/{id}", new { id });
            })
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));
}
```

Updating, disabling and resetting live together, because they're all "an admin changes an existing
account":

```csharp
// src/Modules/Identity/Erp.Modules.Identity/Features/UpdateUser/UpdateUserHandler.cs
using Erp.Modules.Identity.Domain;
using Erp.Modules.Identity.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;
using Microsoft.AspNetCore.Identity;

namespace Erp.Modules.Identity.Features;

internal sealed record UpdateUserCommand(long UserId, string FullName, string Role) : ICommand;

internal sealed class UpdateUserValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserValidator()
    {
        RuleFor(x => x.UserId).GreaterThan(0);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Role).NotEmpty();
    }
}

internal sealed class UpdateUserHandler(UserManager<AppUser> users, ICurrentUser current) : ICommandHandler<UpdateUserCommand>
{
    public async ValueTask<Unit> Handle(UpdateUserCommand cmd, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(cmd.UserId.ToString()) ?? throw new NotFoundException($"User {cmd.UserId} not found.");
        var currentRole = await users.GetRoleAsync(user);
        StaffAccountRules.EnsureCanChangeRole(current.UserId, user.Id, currentRole, cmd.Role, await users.CountActiveAdminsAsync());

        user.FullName = cmd.FullName.Trim();
        (await users.UpdateAsync(user)).ThrowIfFailed();
        if (currentRole != cmd.Role)
        {
            if (currentRole != "") (await users.RemoveFromRoleAsync(user, currentRole)).ThrowIfFailed();
            (await users.AddToRoleAsync(user, cmd.Role)).ThrowIfFailed();
            (await users.UpdateSecurityStampAsync(user)).ThrowIfFailed();   // their sessions carry the old role: end them
        }
        return Unit.Value;
    }
}

internal sealed record SetUserDisabledCommand(long UserId, bool Disabled) : ICommand;

internal sealed class SetUserDisabledHandler(UserManager<AppUser> users, ICurrentUser current) : ICommandHandler<SetUserDisabledCommand>
{
    public async ValueTask<Unit> Handle(SetUserDisabledCommand cmd, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(cmd.UserId.ToString()) ?? throw new NotFoundException($"User {cmd.UserId} not found.");
        if (cmd.Disabled)
            StaffAccountRules.EnsureCanDisable(current.UserId, user.Id, await users.GetRoleAsync(user), await users.CountActiveAdminsAsync());

        user.IsDisabled = cmd.Disabled;
        (await users.UpdateAsync(user)).ThrowIfFailed();
        (await users.UpdateSecurityStampAsync(user)).ThrowIfFailed();   // ends every session they have
        return Unit.Value;
    }
}

internal sealed record ResetUserPasswordCommand(long UserId, string TemporaryPassword) : ICommand;

internal sealed class ResetUserPasswordValidator : AbstractValidator<ResetUserPasswordCommand>
{
    public ResetUserPasswordValidator() => RuleFor(x => x.TemporaryPassword).NotEmpty();
}

internal sealed class ResetUserPasswordHandler(UserManager<AppUser> users) : ICommandHandler<ResetUserPasswordCommand>
{
    public async ValueTask<Unit> Handle(ResetUserPasswordCommand cmd, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(cmd.UserId.ToString()) ?? throw new NotFoundException($"User {cmd.UserId} not found.");
        var token = await users.GeneratePasswordResetTokenAsync(user);
        (await users.ResetPasswordAsync(user, token, cmd.TemporaryPassword)).ThrowIfFailed();
        (await users.SetLockoutEndDateAsync(user, null)).ThrowIfFailed();
        user.MustChangePassword = true;
        (await users.UpdateAsync(user)).ThrowIfFailed();
        return Unit.Value;
    }
}
```

**`UpdateSecurityStampAsync`** is the "sign out everywhere" from lesson 05, done to someone else. After a
role change or a disable, their cookie fails its next one-minute check and they're signed out. A reset
password also unlocks the account, because "I locked myself out" is the usual reason to ask for one.

The endpoints follow the pattern you know:

```csharp
// src/Modules/Identity/Erp.Modules.Identity/Features/UpdateUser/UpdateUserEndpoint.cs
using Carter;
using Erp.Shared.BuildingBlocks;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Identity.Features;

internal sealed record UpdateUserRequest(string FullName, string Role);
internal sealed record ResetUserPasswordRequest(string TemporaryPassword);

internal sealed class UpdateUserEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut("/users/{id:long}", async (long id, UpdateUserRequest body, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new UpdateUserCommand(id, body.FullName, body.Role), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));

        app.MapPost("/users/{id:long}/disable", async (long id, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new SetUserDisabledCommand(id, true), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));

        app.MapPost("/users/{id:long}/enable", async (long id, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new SetUserDisabledCommand(id, false), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));

        app.MapPost("/users/{id:long}/reset-password", async (long id, ResetUserPasswordRequest body, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new ResetUserPasswordCommand(id, body.TemporaryPassword), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));
    }
}
```

Now the person's own side: choosing a new password. Identity's `/auth/manage/info` can change a password,
but it doesn't know about `MustChangePassword`. So we add our own:

```csharp
// src/Modules/Identity/Erp.Modules.Identity/Features/ChangePassword/ChangePasswordHandler.cs
using Erp.Modules.Identity.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;
using Microsoft.AspNetCore.Identity;

namespace Erp.Modules.Identity.Features;

internal sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword) : ICommand;

internal sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().NotEqual(x => x.CurrentPassword)
            .WithMessage("The new password must be different from the current one.");
    }
}

// Like Identity's /manage/info, but it also clears "must change password" after a temporary password.
internal sealed class ChangePasswordHandler(UserManager<AppUser> users, SignInManager<AppUser> signIn, ICurrentUser current)
    : ICommandHandler<ChangePasswordCommand>
{
    public async ValueTask<Unit> Handle(ChangePasswordCommand cmd, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(current.UserId.ToString())
            ?? throw new NotFoundException("The signed-in user no longer exists.");

        (await users.ChangePasswordAsync(user, cmd.CurrentPassword, cmd.NewPassword)).ThrowIfFailed();
        user.MustChangePassword = false;
        (await users.UpdateAsync(user)).ThrowIfFailed();
        await signIn.RefreshSignInAsync(user);   // the new security stamp would end this session too; re-issue it
        return Unit.Value;
    }
}
```

```csharp
// src/Modules/Identity/Erp.Modules.Identity/Features/ChangePassword/ChangePasswordEndpoint.cs
using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Identity.Features;

internal sealed class ChangePasswordEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/auth/change-password", async (ChangePasswordCommand body, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(body, ct);
                return Results.NoContent();
            })
            .RequireAuthorization();
}
```

Identity now has validators, so `IdentityModule` registers them the way Sales does, with internal types
included:

```csharp
services.AddValidatorsFromAssembly(typeof(IdentityModule).Assembly, includeInternalTypes: true);
```

```bash
dotnet add src/Modules/Identity/Erp.Modules.Identity package FluentValidation.DependencyInjectionExtensions --version 12.1.1
```

## 6. Who am I, now with a name

`/api/auth/me` returned what the cookie knew: id, email, role. The app now needs the person's name and
whether they must change their password, which only the database knows. Before:

```csharp
internal sealed record CurrentUserDto(long Id, string Username, string Role);

internal sealed class GetCurrentUserHandler(ICurrentUser user) : IQueryHandler<GetCurrentUserQuery, CurrentUserDto>
{
    public ValueTask<CurrentUserDto> Handle(GetCurrentUserQuery query, CancellationToken ct) =>
        ValueTask.FromResult(new CurrentUserDto(user.UserId, user.Username, user.Role));
}
```

After:

```csharp
internal sealed record CurrentUserDto(long Id, string Username, string FullName, string Role, bool MustChangePassword);

internal sealed class GetCurrentUserHandler(ICurrentUser current, UserManager<AppUser> users)
    : IQueryHandler<GetCurrentUserQuery, CurrentUserDto>
{
    public async ValueTask<CurrentUserDto> Handle(GetCurrentUserQuery query, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(current.UserId.ToString())
            ?? throw new NotFoundException("The signed-in user no longer exists.");
        return new CurrentUserDto(user.Id, user.UserName!, user.FullName, current.Role, user.MustChangePassword);
    }
}
```

(add `using Erp.Modules.Identity.Infrastructure;` and `using Microsoft.AspNetCore.Identity;`).

## 7. Reading users and the sign-in log

Identity needs Dapper for its reads now, and a home for its SQL:

```bash
dotnet add src/Modules/Identity/Erp.Modules.Identity package Dapper --version 2.1.79
```

```xml
<!-- in Erp.Modules.Identity.csproj -->
<ItemGroup>
  <EmbeddedResource Include="Infrastructure\Sql\**\*.sql" />
</ItemGroup>
```

```sql
-- src/Modules/Identity/Erp.Modules.Identity/Infrastructure/Sql/Functions/identity.search_users.v1.sql
-- Released scripts are never edited: change a function with a .v2 file and a new migration.
CREATE OR REPLACE FUNCTION identity.search_users(p_search text, p_page integer, p_page_size integer)
RETURNS TABLE (id bigint, email text, full_name text, role text, is_disabled boolean, must_change_password boolean,
               locked_out_until timestamptz, last_sign_in timestamptz)
LANGUAGE sql STABLE
AS $$
    SELECT u.id, u.email::text, u.full_name::text, COALESCE(r.name, '')::text, u.is_disabled, u.must_change_password,
           CASE WHEN u.lockout_end > now() THEN u.lockout_end END,
           (SELECT MAX(a.occurred_utc) FROM identity.sign_in_attempt a WHERE a.user_id = u.id AND a.outcome = 'Succeeded')
    FROM identity.users u
    LEFT JOIN identity.user_roles ur ON ur.user_id = u.id
    LEFT JOIN identity.roles r ON r.id = ur.role_id
    WHERE p_search IS NULL OR u.email ILIKE '%' || p_search || '%' OR u.full_name ILIKE '%' || p_search || '%'
    ORDER BY u.full_name, u.id
    OFFSET (p_page - 1) * p_page_size
    LIMIT p_page_size;
$$;

CREATE OR REPLACE FUNCTION identity.count_users(p_search text)
RETURNS bigint
LANGUAGE sql STABLE
AS $$
    SELECT COUNT(*) FROM identity.users u
    WHERE p_search IS NULL OR u.email ILIKE '%' || p_search || '%' OR u.full_name ILIKE '%' || p_search || '%';
$$;

CREATE OR REPLACE FUNCTION identity.user_directory()
RETURNS TABLE (id bigint, full_name text)
LANGUAGE sql STABLE
AS $$
    SELECT u.id, COALESCE(NULLIF(u.full_name, ''), u.email)::text FROM identity.users u ORDER BY u.id;
$$;

CREATE OR REPLACE FUNCTION identity.search_sign_in_attempts(p_email text, p_failed_only boolean, p_page integer, p_page_size integer)
RETURNS TABLE (id bigint, email text, user_id bigint, outcome text, ip_address text, occurred_utc timestamptz)
LANGUAGE sql STABLE
AS $$
    SELECT a.id, a.email::text, a.user_id, a.outcome::text, a.ip_address::text, a.occurred_utc
    FROM identity.sign_in_attempt a
    WHERE (p_email IS NULL OR a.email ILIKE '%' || p_email || '%')
      AND (NOT p_failed_only OR a.outcome <> 'Succeeded')
    ORDER BY a.occurred_utc DESC, a.id DESC
    OFFSET (p_page - 1) * p_page_size
    LIMIT p_page_size;
$$;

CREATE OR REPLACE FUNCTION identity.count_sign_in_attempts(p_email text, p_failed_only boolean)
RETURNS bigint
LANGUAGE sql STABLE
AS $$
    SELECT COUNT(*) FROM identity.sign_in_attempt a
    WHERE (p_email IS NULL OR a.email ILIKE '%' || p_email || '%')
      AND (NOT p_failed_only OR a.outcome <> 'Succeeded');
$$;
```

Every user search joins three Identity tables, which is fine: they're all in the `identity` schema. The
user's role comes from Identity's own tables, so there's no second copy to keep in step. Ship it with an
empty migration filled in by hand, exactly like lesson 07's:

```bash
dotnet ef migrations add AddReadFunctions \
  --project src/Modules/Identity/Erp.Modules.Identity --startup-project src/Modules/Identity/Erp.Modules.Identity \
  --output-dir Infrastructure/Migrations
```

with `migrationBuilder.Sql(EmbeddedSql.Read(typeof(AddReadFunctions).Assembly, "identity.search_users.v1.sql"));`
in `Up` and a `DROP FUNCTION IF EXISTS` for each of the five functions in `Down`.

The two read slices are lesson 07's pattern once more. The user directory is the one worth a look: any
signed-in user may read it, because it holds nothing but names, and the order page needs "approved by
Manuel Santos" instead of "approved by 2".

```csharp
// src/Modules/Identity/Erp.Modules.Identity/Features/SearchUsers/SearchUsersHandler.cs
using Dapper;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;

namespace Erp.Modules.Identity.Features;

internal sealed record SearchUsersQuery(string? Search, int Page, int PageSize) : IQuery<PagedResult<UserDto>>;

internal sealed record UserDto(
    long Id, string Email, string FullName, string Role, bool IsDisabled, bool MustChangePassword,
    DateTime? LockedOutUntil, DateTime? LastSignIn);

internal sealed class SearchUsersValidator : AbstractValidator<SearchUsersQuery>
{
    public SearchUsersValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
    }
}

internal sealed class SearchUsersHandler(IDbConnectionFactory connections) : IQueryHandler<SearchUsersQuery, PagedResult<UserDto>>
{
    public async ValueTask<PagedResult<UserDto>> Handle(SearchUsersQuery q, CancellationToken ct)
    {
        var search = string.IsNullOrWhiteSpace(q.Search) ? null : q.Search.Trim();
        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM identity.search_users(@Search, @Page, @PageSize);
            SELECT identity.count_users(@Search);
            """, new { Search = search, q.Page, q.PageSize }, cancellationToken: ct));

        var rows = (await multi.ReadAsync<UserDto>()).ToList();
        return new PagedResult<UserDto>(rows, q.Page, q.PageSize, (int)await multi.ReadSingleAsync<long>());
    }
}

// Names for "approved by", "shipped by" and the like. Every signed-in user may read it; it holds no secrets.
internal sealed record GetUserDirectoryQuery : IQuery<IReadOnlyList<UserNameDto>>;

internal sealed record UserNameDto(long Id, string FullName);

internal sealed class GetUserDirectoryHandler(IDbConnectionFactory connections)
    : IQueryHandler<GetUserDirectoryQuery, IReadOnlyList<UserNameDto>>
{
    public async ValueTask<IReadOnlyList<UserNameDto>> Handle(GetUserDirectoryQuery q, CancellationToken ct)
    {
        await using var conn = connections.Create();
        return (await conn.QueryAsync<UserNameDto>(new CommandDefinition(
            "SELECT * FROM identity.user_directory();", cancellationToken: ct))).ToList();
    }
}
```

```csharp
// src/Modules/Identity/Erp.Modules.Identity/Features/SearchUsers/SearchUsersEndpoint.cs
using Carter;
using Erp.Shared.BuildingBlocks;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Identity.Features;

internal sealed class SearchUsersEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/users", async (string? search, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new SearchUsersQuery(search, page ?? 1, pageSize ?? 20), ct)))
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));

        app.MapGet("/users/directory", async (ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetUserDirectoryQuery(), ct)))
            .RequireAuthorization();
    }
}
```

The sign-in log slice, `Features/SearchSignInAttempts/`, is the same shape: a `SearchSignInAttemptsQuery(string?
Email, bool FailedOnly, int Page, int PageSize)`, a `SignInAttemptDto(long Id, string Email, long? UserId,
string Outcome, string? IpAddress, DateTime OccurredUtc)`, the paging validator, a handler calling
`identity.search_sign_in_attempts` and its count, and `GET /sign-in-attempts` for Admin only, with a page size
of 50 by default. Write it from the pattern; the reference build has it if you want to compare.

## 8. Email, for "I forgot my password"

Identity already has the whole flow: `POST /api/auth/forgotPassword` makes a reset code, and
`POST /api/auth/resetPassword` takes the code and a new password. All it lacks is a way to send the code. It
asks for an `IEmailSender<AppUser>`; until now, Identity used a built-in one that sends nothing, which is
the honest gap lesson 05 admitted.

**MailKit** (MIT) is the standard .NET library for sending email over SMTP. Microsoft's own docs recommend
it over the old `SmtpClient`.

```bash
dotnet add src/Modules/Identity/Erp.Modules.Identity package MailKit --version 4.18.2
```

```csharp
// src/Modules/Identity/Erp.Modules.Identity/Infrastructure/SmtpEmailSender.cs
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace Erp.Modules.Identity.Infrastructure;

internal sealed class SmtpOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string From { get; set; } = "erp@localhost";
}

// Identity calls this for "forgot password". With no SMTP host configured (development), the message is logged
// instead, so the flow can still be followed end to end.
internal sealed class SmtpEmailSender(IConfiguration config, ILogger<SmtpEmailSender> log) : IEmailSender<AppUser>
{
    private readonly SmtpOptions _smtp = config.GetSection("Smtp").Get<SmtpOptions>() ?? new();

    public Task SendConfirmationLinkAsync(AppUser user, string email, string confirmationLink) =>
        SendAsync(email, "Confirm your email", $"Confirm your email address: {confirmationLink}");

    public Task SendPasswordResetLinkAsync(AppUser user, string email, string resetLink) =>
        SendAsync(email, "Reset your password", $"Reset your password: {resetLink}");

    public Task SendPasswordResetCodeAsync(AppUser user, string email, string resetCode) =>
        SendAsync(email, "Your password reset code",
            $"Hello {user.FullName},\n\nYour password reset code is:\n\n{resetCode}\n\n" +
            "Enter it on the sign-in page under \"Forgot password\". If you didn't ask for this, ignore this email.");

    private async Task SendAsync(string to, string subject, string body)
    {
        if (string.IsNullOrWhiteSpace(_smtp.Host))
        {
            log.LogWarning("Smtp:Host is not set; email to {To} not sent. Subject: {Subject}. Body: {Body}", to, subject, body);
            return;
        }

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_smtp.From));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        await client.ConnectAsync(_smtp.Host, _smtp.Port, SecureSocketOptions.StartTlsWhenAvailable);
        if (!string.IsNullOrEmpty(_smtp.Username))
            await client.AuthenticateAsync(_smtp.Username, _smtp.Password ?? "");
        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }
}
```

Register it in `IdentityModule`: `services.AddTransient<IEmailSender<AppUser>, SmtpEmailSender>();`. And
add the settings, empty, to `appsettings.json`:

```json
"Smtp": { "Host": "", "Port": 587, "Username": "", "Password": "", "From": "erp@localhost" }
```

In development, with no host, the code is written to the log instead, so you can follow the whole flow on
your own machine. In production, the buyer puts in their mail provider's SMTP details; any provider works,
from Google Workspace to a local mail server. `StartTlsWhenAvailable` encrypts the connection whenever the
server supports it.

Notice what `forgotPassword` does with an email that matches nobody: it answers 200 and sends nothing.
Otherwise anyone could test which emails have accounts.

## 9. Six dev users, with names

`IdentityDatabase.SeedDevelopmentDataAsync` now creates one user per role:

```csharp
(string Email, string Name, string Role)[] devUsers =
[
    ("rep@erp.local", "Rita Reyes", Roles.SalesRep),
    ("manager@erp.local", "Manuel Santos", Roles.SalesManager),
    ("admin@erp.local", "Ada Mendoza", Roles.Admin),
    ("clerk@erp.local", "Carlo Bautista", Roles.InventoryClerk),
    ("buyer@erp.local", "Bea Villanueva", Roles.Purchaser),
    ("accountant@erp.local", "Ana Cruz", Roles.Accountant),
];

foreach (var (email, name, role) in devUsers)
{
    if (await users.FindByEmailAsync(email) is not null) continue;
    var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, FullName = name };
    ThrowIfFailed(await users.CreateAsync(user, DevPassword));
    ThrowIfFailed(await users.AddToRoleAsync(user, role));
}
```

The three existing users keep their empty names (the seed skips users that exist). Give them names from the
Users screen, or drop the `erp` database to start fresh.

## 10. Who reads Sales

A warehouse clerk ships orders, and an accountant bills them, so both must read orders and customers. In
`SalesRoles`:

```csharp
// Customers and orders are also read by the people who ship and bill them.
public static readonly string[] Readers =
    [Roles.SalesRep, Roles.SalesManager, Roles.Admin, Roles.InventoryClerk, Roles.Accountant];
```

and the four read endpoints (`GET /sales-orders`, `/sales-orders/{id}`, `/customers` and `/customers/{id}`,
and `/price-list`) change from `SalesRoles.Sellers` to `SalesRoles.Readers`. Writing stays with sellers.

Who sees which orders? `CreatorFilterFor` already answers "everyone but a rep sees all", so a clerk and an
accountant see every order without a line of change. Pin that down with one more line in
`A_rep_sees_only_their_own_orders_and_a_manager_sees_all`:

```csharp
order.IsVisibleTo(new UserId(5), Roles.Accountant).Should().BeTrue();
```

## 11. The screens

### Who's signed in, now with a name

In the browser's `CookieAuthenticationStateProvider`, the user record grows, the name claim shows the full
name, and a disabled account gets its own message:

```csharp
public sealed record CurrentUser(long Id, string Username, string FullName, string Role, bool MustChangePassword);
```

```csharp
private static ClaimsPrincipal Principal(CurrentUser user) => new(new ClaimsIdentity(
[
    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
    new Claim(ClaimTypes.Name, user.FullName),
    new Claim(ClaimTypes.Email, user.Username),
    new Claim(ClaimTypes.Role, user.Role),
], authenticationType: "cookie", ClaimTypes.Name, ClaimTypes.Role));
```

```csharp
// in SignInAsync
if (response.StatusCode == HttpStatusCode.TooManyRequests)
    return "Too many attempts. Wait a minute and try again.";
if (!response.IsSuccessStatusCode)
{
    var body = await response.Content.ReadAsStringAsync();
    return body.Contains("LockedOut") ? "This account is locked for 15 minutes after too many wrong passwords."
        : body.Contains("NotAllowed") ? "This account is disabled. Ask an administrator."
        : "Wrong email or password.";
}
```

The "too many attempts" line is ready for [lesson 21](21-security-hardening.md)'s rate limit on sign-in.

### A temporary password must be replaced first

```razor
@* src/Erp.Web/Pages/ChangePassword.razor *@
@page "/change-password"
@inject ApiClient Api
@inject CookieAuthenticationStateProvider Auth
@inject NavigationManager Nav

<PageTitle>Change password · ERP</PageTitle>

<MudPaper Class="pa-6" MaxWidth="480px" Elevation="1">
    <MudText Typo="Typo.h5" GutterBottom="true">Change password</MudText>
    @if (Auth.User?.MustChangePassword == true)
    {
        <MudAlert Severity="Severity.Info" Class="mb-4">An administrator set a temporary password. Choose your own to continue.</MudAlert>
    }
    <MudStack Spacing="3">
        <MudTextField @bind-Value="_current" Label="Current password" InputType="InputType.Password" />
        <MudTextField @bind-Value="_new" Label="New password (at least 12 characters)" InputType="InputType.Password" />
        <MudTextField @bind-Value="_repeat" Label="New password again" InputType="InputType.Password"
                      Error="_repeat.Length > 0 && _repeat != _new" ErrorText="The two don't match." />
        <MudButton Variant="Variant.Filled" Color="Color.Primary" OnClick="Save"
                   Disabled="_new.Length == 0 || _new != _repeat">Change password</MudButton>
    </MudStack>
</MudPaper>

@code {
    private string _current = "", _new = "", _repeat = "";

    private async Task Save()
    {
        if (await Api.Post("api/auth/change-password", new { currentPassword = _current, newPassword = _new }, "Password changed."))
        {
            Auth.Refresh();
            Nav.NavigateTo("");
        }
    }
}
```

In `MainLayout.razor`, the account menu gets a **Change password** item, the role shows in words, and every
page checks for a temporary password:

```razor
<MudText Typo="Typo.caption" Class="px-4 pb-2">@Format.Words(RoleOf(context))</MudText>
<MudDivider />
<MudMenuItem Href="change-password">Change password</MudMenuItem>
```

```csharp
// A temporary password from an admin must be replaced before anything else.
protected override async Task OnParametersSetAsync()
{
    await Auth.GetAuthenticationStateAsync();
    if (Auth.User is { MustChangePassword: true } && !Nav.Uri.EndsWith("/change-password"))
        Nav.NavigateTo("change-password");
}
```

The layout runs on every page change, so there's no page a new user can reach first. The API doesn't block
them, though: this is about getting the person to choose a password, not a security wall. The temporary
password is already one only the admin and the user know.

### Forgot password

```razor
@* src/Erp.Web/Pages/ForgotPassword.razor *@
@page "/forgot-password"
@attribute [AllowAnonymous]
@inject HttpClient Http

<PageTitle>Forgot password · ERP</PageTitle>

@* Identity's own endpoints: /forgotPassword emails a code, /resetPassword takes it back with the new password. *@
<MudContainer MaxWidth="MaxWidth.ExtraSmall" Class="mt-16">
    <MudPaper Class="pa-8" Elevation="2">
        <MudText Typo="Typo.h5" GutterBottom="true">Reset your password</MudText>
        @if (!_codeSent)
        {
            <MudStack Spacing="3">
                <MudText Typo="Typo.body2">Enter your email. If it belongs to an account, we'll send a reset code.</MudText>
                <MudTextField @bind-Value="_email" Label="Email" InputType="InputType.Email" />
                <MudButton Variant="Variant.Filled" Color="Color.Primary" OnClick="SendCode">Send code</MudButton>
            </MudStack>
        }
        else if (!_done)
        {
            <MudStack Spacing="3">
                <MudText Typo="Typo.body2">Check your email for the code, then choose a new password (at least 12 characters).</MudText>
                <MudTextField @bind-Value="_code" Label="Reset code" />
                <MudTextField @bind-Value="_password" Label="New password" InputType="InputType.Password" />
                @if (_error is not null) { <MudAlert Severity="Severity.Error" Dense="true">@_error</MudAlert> }
                <MudButton Variant="Variant.Filled" Color="Color.Primary" OnClick="Reset">Set new password</MudButton>
            </MudStack>
        }
        else
        {
            <MudAlert Severity="Severity.Success">Your password was changed.</MudAlert>
        }
        <MudLink Href="login" Typo="Typo.body2" Class="mt-4 d-block">Back to sign in</MudLink>
    </MudPaper>
</MudContainer>

@code {
    private string _email = "", _code = "", _password = "";
    private string? _error;
    private bool _codeSent, _done;

    // Always "sent", whether or not the email exists: the page must not reveal who has an account.
    private async Task SendCode()
    {
        await Http.PostAsJsonAsync("api/auth/forgotPassword", new { email = _email.Trim() });
        _codeSent = true;
    }

    private async Task Reset()
    {
        var response = await Http.PostAsJsonAsync("api/auth/resetPassword",
            new { email = _email.Trim(), resetCode = _code.Trim(), newPassword = _password });
        _done = response.IsSuccessStatusCode;
        _error = _done ? null : "That code is wrong or has expired, or the password is too short.";
    }
}
```

This page uses `HttpClient` directly, not `ApiClient`: nobody is signed in, and its errors need their own
words. On the sign-in page, under the button:

```razor
<MudLink Href="forgot-password" Typo="Typo.body2">Forgot your password?</MudLink>
```

### Names instead of numbers

```csharp
// src/Erp.Web/Services/UserDirectory.cs
namespace Erp.Web.Services;

// Turns the user ids the API returns ("approved by 2") into names, loaded once per session.
public sealed class UserDirectory(ApiClient api)
{
    private sealed record Entry(long Id, string FullName);
    private Dictionary<long, string>? _names;

    public async Task<string> NameOf(long? id)
    {
        if (id is null) return "";
        _names ??= (await api.Get<List<Entry>>("api/users/directory") ?? []).ToDictionary(e => e.Id, e => e.FullName);
        return _names.TryGetValue(id.Value, out var name) ? name : $"User {id}";
    }
}
```

Register it in `Program.cs` (`builder.Services.AddScoped<UserDirectory>();`). Remember the rule that orders
store a user's id, never a foreign key into `identity`? This is the other half: the *screen* puts the name
back, through the Identity module's own API.

On the order page, add `@inject UserDirectory Users`, a field for the names, and load them with the order:

```csharp
private (string Created, string? Approved, string? Rejected) _names = ("", null, null);

private async Task Load()
{
    _order = await Api.Get<SalesOrderDetail>($"api/sales-orders/{Id}");
    if (_order is null) { Nav.NavigateTo("sales-orders"); return; }
    _names = (await Users.NameOf(_order.CreatedBy),
              _order.ApprovedBy is null ? null : await Users.NameOf(_order.ApprovedBy),
              _order.RejectedBy is null ? null : await Users.NameOf(_order.RejectedBy));
}
```

The history now says who:

```razor
<MudText Typo="Typo.body2">Created by @_names.Created, @Format.Date(_order.CreatedUtc)</MudText>
@if (_order.SubmittedUtc is not null) { <MudText Typo="Typo.body2">Submitted @Format.Date(_order.SubmittedUtc)</MudText> }
@if (_order.ApprovedUtc is not null)
{
    <MudText Typo="Typo.body2">Approved by @(_names.Approved ?? "automatic approval (₱50,000 or less)"), @Format.Date(_order.ApprovedUtc)</MudText>
}
@if (_order.RejectionReason is not null)
{
    <MudAlert Severity="Severity.Error" Dense="true" Class="mt-2">Rejected by @_names.Rejected: @_order.RejectionReason</MudAlert>
}
```

### The administration screens

Two shapes join `Models.cs`:

```csharp
public sealed record User(long Id, string Email, string FullName, string Role, bool IsDisabled, bool MustChangePassword,
    DateTime? LockedOutUntil, DateTime? LastSignIn);

public sealed record SignInAttempt(long Id, string Email, long? UserId, string Outcome, string? IpAddress, DateTime OccurredUtc);
```

```razor
@* src/Erp.Web/Pages/Admin/Users.razor *@
@page "/users"
@attribute [Authorize(Roles = Roles.Admin)]
@inject ApiClient Api
@inject IDialogService Dialogs

<PageTitle>Users · ERP</PageTitle>

<MudStack Row="true" AlignItems="AlignItems.Center" Class="mb-4">
    <MudText Typo="Typo.h4">Users</MudText>
    <MudSpacer />
    <MudButton OnClick="() => Edit(null)" Variant="Variant.Filled" Color="Color.Primary" StartIcon="@Icons.Material.Filled.PersonAdd">New user</MudButton>
</MudStack>

<MudTextField T="string" Value="_search" ValueChanged="OnSearch" Label="Name or email" DebounceInterval="400" Clearable="true" Class="mb-4" />

<MudTable T="User" @ref="_table" ServerData="Load" Hover="true" Dense="true">
    <HeaderContent>
        <MudTh>Name</MudTh><MudTh>Email</MudTh><MudTh>Role</MudTh><MudTh>Status</MudTh><MudTh>Last sign-in</MudTh><MudTh></MudTh>
    </HeaderContent>
    <RowTemplate>
        <MudTd>@context.FullName</MudTd>
        <MudTd>@context.Email</MudTd>
        <MudTd>@Format.Words(context.Role)</MudTd>
        <MudTd>
            @if (context.IsDisabled) { <MudChip T="string" Size="Size.Small" Color="Color.Default">Disabled</MudChip> }
            else if (context.LockedOutUntil is not null) { <MudChip T="string" Size="Size.Small" Color="Color.Error">Locked out</MudChip> }
            else if (context.MustChangePassword) { <MudChip T="string" Size="Size.Small" Color="Color.Warning">Temporary password</MudChip> }
            else { <MudChip T="string" Size="Size.Small" Color="Color.Success">Active</MudChip> }
        </MudTd>
        <MudTd>@Format.Date(context.LastSignIn)</MudTd>
        <MudTd>
            <MudMenu Icon="@Icons.Material.Filled.MoreVert" Size="Size.Small" Dense="true" aria-label="Actions">
                <MudMenuItem OnClick="() => Edit(context)">Edit name and role</MudMenuItem>
                <MudMenuItem OnClick="() => ResetPassword(context)">Reset password</MudMenuItem>
                <MudMenuItem OnClick="() => ToggleDisabled(context)">@(context.IsDisabled ? "Enable" : "Disable")</MudMenuItem>
            </MudMenu>
        </MudTd>
    </RowTemplate>
    <NoRecordsContent>No users match.</NoRecordsContent>
    <PagerContent><MudTablePager /></PagerContent>
</MudTable>

@code {
    private MudTable<User> _table = default!;
    private string? _search;

    private Task<TableData<User>> Load(TableState state, CancellationToken ct) =>
        Api.Page<User>(Url.With("api/users", ("search", _search)), state);

    private Task OnSearch(string? text) { _search = text; return _table.ReloadServerData(); }

    private async Task Edit(User? user)
    {
        var dialog = await Dialogs.ShowAsync<UserDialog>(user is null ? "New user" : $"Edit {user.FullName}",
            new DialogParameters<UserDialog> { { d => d.User, user } });
        if ((await dialog.Result) is { Canceled: false }) await _table.ReloadServerData();
    }

    private async Task ResetPassword(User user)
    {
        var dialog = await Dialogs.ShowAsync<ResetPasswordDialog>($"Reset password for {user.FullName}",
            new DialogParameters<ResetPasswordDialog> { { d => d.User, user } });
        if ((await dialog.Result) is { Canceled: false }) await _table.ReloadServerData();
    }

    private async Task ToggleDisabled(User user)
    {
        if (await Api.Post($"api/users/{user.Id}/{(user.IsDisabled ? "enable" : "disable")}",
                success: user.IsDisabled ? "Enabled." : "Disabled. Their sessions end within a minute."))
            await _table.ReloadServerData();
    }
}
```

The status column answers the questions an admin is asked most: is this person locked out, still on a
temporary password, or gone? The success message tells the truth about timing: "within a minute", the
security-stamp interval from step 4.

```razor
@* src/Erp.Web/Pages/Admin/UserDialog.razor *@
@inject ApiClient Api

<MudDialog>
    <DialogContent>
        <MudStack Spacing="2" Style="min-width:400px">
            <MudTextField @bind-Value="_email" Label="Email (they sign in with it)" InputType="InputType.Email" Disabled="User is not null" />
            <MudTextField @bind-Value="_fullName" Label="Full name" MaxLength="100" />
            <MudSelect T="string" @bind-Value="_role" Label="Role">
                @foreach (var r in Roles.All)
                {
                    <MudSelectItem Value="r">@Format.Words(r)</MudSelectItem>
                }
            </MudSelect>
            @if (User is null)
            {
                <MudTextField @bind-Value="_password" Label="Temporary password (at least 12 characters)" InputType="InputType.Password"
                              HelperText="Give it to them in person. They must change it when they first sign in." />
            }
        </MudStack>
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="() => Dialog.Cancel()">Cancel</MudButton>
        <MudButton OnClick="Save" Variant="Variant.Filled" Color="Color.Primary"
                   Disabled="string.IsNullOrWhiteSpace(_fullName) || string.IsNullOrWhiteSpace(_email)">Save</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = default!;
    [Parameter] public User? User { get; set; }

    private string _email = "", _fullName = "", _role = Roles.SalesRep, _password = "";

    protected override void OnInitialized()
    {
        if (User is { } u) (_email, _fullName, _role) = (u.Email, u.FullName, u.Role);
    }

    private async Task Save()
    {
        var saved = User is null
            ? await Api.Create("api/users", new { email = _email, fullName = _fullName, role = _role, temporaryPassword = _password },
                "User created.") is not null
            : await Api.Put($"api/users/{User.Id}", new { fullName = _fullName, role = _role }, "User saved.");
        if (saved) Dialog.Close(DialogResult.Ok(true));
    }
}
```

The role list comes from `Roles.All`, the linked file from lesson 06, so a seventh role would appear here by
itself. The email can't change after creation: it's how the person signs in, and changing it quietly would
lock them out.

```razor
@* src/Erp.Web/Pages/Admin/ResetPasswordDialog.razor *@
@inject ApiClient Api

<MudDialog>
    <DialogContent>
        <MudStack Spacing="2" Style="min-width:380px">
            <MudText Typo="Typo.body2">
                Sets a temporary password and unlocks the account. They must choose their own when they next sign in.
            </MudText>
            <MudTextField @bind-Value="_password" Label="Temporary password (at least 12 characters)" InputType="InputType.Password" />
        </MudStack>
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="() => Dialog.Cancel()">Cancel</MudButton>
        <MudButton OnClick="Save" Variant="Variant.Filled" Color="Color.Primary" Disabled="_password.Length == 0">Reset</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = default!;
    [Parameter] public User User { get; set; } = default!;
    private string _password = "";

    private async Task Save()
    {
        if (await Api.Post($"api/users/{User.Id}/reset-password", new { temporaryPassword = _password }, "Password reset."))
            Dialog.Close(DialogResult.Ok(true));
    }
}
```

```razor
@* src/Erp.Web/Pages/Admin/SignInLog.razor *@
@page "/sign-in-log"
@attribute [Authorize(Roles = Roles.Admin)]
@inject ApiClient Api

<PageTitle>Sign-in log · ERP</PageTitle>

<MudText Typo="Typo.h4" Class="mb-4">Sign-in log</MudText>

<MudStack Row="true" Spacing="4" Class="mb-4">
    <MudTextField T="string" Value="_email" ValueChanged="OnEmail" Label="Email" DebounceInterval="400" Clearable="true" />
    <MudSwitch T="bool" Value="_failedOnly" ValueChanged="OnFailed" Label="Failed only" Color="Color.Error" />
</MudStack>

<MudTable T="SignInAttempt" @ref="_table" ServerData="Load" Dense="true" Hover="true" RowsPerPage="50">
    <HeaderContent>
        <MudTh>When</MudTh><MudTh>Email</MudTh><MudTh>Outcome</MudTh><MudTh>From</MudTh>
    </HeaderContent>
    <RowTemplate>
        <MudTd>@Format.Date(context.OccurredUtc)</MudTd>
        <MudTd>@context.Email</MudTd>
        <MudTd>
            <MudChip T="string" Size="Size.Small" Color="@(context.Outcome == "Succeeded" ? Color.Success : Color.Error)">@Format.Words(context.Outcome)</MudChip>
        </MudTd>
        <MudTd>@context.IpAddress</MudTd>
    </RowTemplate>
    <NoRecordsContent>No sign-in attempts match.</NoRecordsContent>
    <PagerContent><MudTablePager PageSizeOptions="new[] { 50, 100 }" /></PagerContent>
</MudTable>

@code {
    private MudTable<SignInAttempt> _table = default!;
    private string? _email;
    private bool _failedOnly;

    private Task<TableData<SignInAttempt>> Load(TableState state, CancellationToken ct) =>
        Api.Page<SignInAttempt>(Url.With("api/sign-in-attempts", ("email", _email), ("failedOnly", _failedOnly)), state);

    private Task OnEmail(string? text) { _email = text; return _table.ReloadServerData(); }
    private Task OnFailed(bool value) { _failedOnly = value; return _table.ReloadServerData(); }
}
```

"Failed only" turns twenty failures for one email in five minutes into something an admin notices.

### The menu, for six roles

In `NavMenu.razor`, the Sales group now shows to the readers too, and admins get their own group:

```razor
<AuthorizeView Roles="@Roles.Admin">
    <MudNavGroup Title="Administration" Icon="@Icons.Material.Filled.AdminPanelSettings" Expanded="true">
        <MudNavLink Href="users">Users</MudNavLink>
        <MudNavLink Href="sign-in-log">Sign-in log</MudNavLink>
    </MudNavGroup>
</AuthorizeView>
```

```csharp
private static readonly string Sales = Join(Roles.SalesRep, Roles.SalesManager, Roles.Admin, Roles.InventoryClerk, Roles.Accountant);
```

### The tests

In the UI tests, add `"/users"`, `"/sign-in-log"` and `"/change-password"` to `pages`, and one more test:

```csharp
[UiFact]
public async Task A_rep_does_not_see_administration()
{
    var rep = await SignIn("rep@erp.local");
    (await rep.GetByText("Administration").CountAsync()).Should().Be(0);

    var admin = await SignIn("admin@erp.local");
    await admin.GotoAsync("/users");
    await admin.GetByText("rep@erp.local").WaitForAsync();
    await Shot(admin, "admin-users");
}
```

And an integration test class, [UserAdministrationTests.cs](../course-validation/business/tests/Erp.Integration.Tests/UserAdministrationTests.cs),
that proves through the real API: a new user's `/api/auth/me` says `mustChangePassword: true` until they
change it, a disabled user can't sign in, and the sign-in log shows both outcomes; a rep gets a 403 on
`/api/users`, and an admin gets a 422 for disabling themselves; and a five-letter temporary password is
refused by Identity's policy with a 422.

## How a request flows

```mermaid
sequenceDiagram
  participant A as Admin
  participant API as /api/users/7/disable
  participant R as StaffAccountRules
  participant UM as UserManager
  participant U as User 7's browser
  A->>API: POST (cookie, role Admin)
  API->>UM: find user 7, their role, count active admins
  API->>R: EnsureCanDisable(me, 7, role, admins)
  API->>UM: IsDisabled = true; UpdateSecurityStamp
  U->>API: any request, within a minute
  Note over U,API: stamp no longer matches → 401
  U->>U: back to the sign-in page; "This account is disabled."
```

## Mistakes to avoid

- Deleting users who leave. Disable them; their name is on history.
- Writing users with EF directly. `UserManager` keeps hashes, stamps and normalized names right.
- An admin who knows everyone's password. Temporary passwords, changed at first sign-in.
- "No such user" on the forgot-password page. Always answer the same way.
- A foreign key from `sales_order.created_by` to `identity.users`, to show names. Use the directory.
- Forgetting `UpdateSecurityStampAsync` after a role change. Their open browser keeps the old role.

## Let's run it!

Restart the app; Identity's two new migrations run, and the seed adds the three new users. Sign in as
`admin@erp.local`:

1. **Administration → Users**. Six people, each with a role and a status. Click **New user**: "Liza Ramos",
   `liza@erp.local`, Accountant, temporary password `ChangeMe-2026!`.
2. In a private window, sign in as Liza. You land on **Change password**, whatever address you typed. Choose
   a new one, and the home page says "Good day, Liza Ramos".
3. As the admin, **Disable** Liza. Within a minute, her window's next click sends her to the sign-in page,
   and signing in says "This account is disabled."
4. Try to disable yourself: "You can't disable your own account."
5. **Sign-in log**, with **Failed only** on: Liza's refused attempt is there, with her IP address.
6. Sign out, click **Forgot your password?**, and enter `rep@erp.local`. Look at the API's console: the email
   with the reset code is in the log. Use it to set a new password for the rep.

Then the order page: open any order. The history says "Created by Rita Reyes", not a number.

```bash
dotnet test
```

The four new rules pass in the Identity unit tests, and the three new integration tests pass with the rest.

## Let's check!

1. Why are staff accounts created by an admin, and how does the admin avoid knowing anyone's password?
2. Why disable instead of delete?
3. Name the three rules that stop a company locking itself out, and where they live.
4. What are Identity's two extension points we used, and what did each add?
5. An admin disables someone. When does that person's open browser stop working, and why?
6. Why does the forgot-password page say "sent" even for an email that doesn't exist?
7. Orders store `created_by` as a number. How does the screen show a name without crossing a module wall?

And that's where this lesson ends. The company can now run its own staff list, and every role it needs
exists. Next time, the warehouse clerk gets work to do: an item catalog of their own, and stock that moves.
Good luck, developer!

---

← [Lesson 14 — Customers](14-customers.md) · Next: [Lesson 16 — Item catalog and stock](16-item-catalog.md) →
