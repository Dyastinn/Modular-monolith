using System.Data.Common;
using Erp.Shared.BuildingBlocks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Identity.Infrastructure;

internal sealed class IdentityDatabase(UserManager<AppUser> users) : IModuleDatabase
{
    public const string DevPassword = "Password123!";

    public string Module => "Identity";
    public string Schema => ErpIdentityDbContext.Schema;

    public async Task MigrateAsync(DbConnection ownerConnection, CancellationToken ct)
    {
        var options = new DbContextOptionsBuilder<ErpIdentityDbContext>();
        ErpIdentityDbContext.Configure(options, ownerConnection);
        await using var db = new ErpIdentityDbContext(options.Options);
        await db.Database.MigrateAsync(ct);
    }

    // Through UserManager rather than SQL, so passwords are hashed exactly as in real use.
    public async Task SeedDevelopmentDataAsync(DbConnection ownerConnection, CancellationToken ct)
    {
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
    }

    private static void ThrowIfFailed(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
