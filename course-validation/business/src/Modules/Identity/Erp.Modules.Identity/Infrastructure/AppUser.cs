using Microsoft.AspNetCore.Identity;

namespace Erp.Modules.Identity.Infrastructure;

// Identity's user, plus the three things an ERP's staff list needs.
internal sealed class AppUser : IdentityUser<long>
{
    public string FullName { get; set; } = "";
    public bool IsDisabled { get; set; }            // left the company: can't sign in, history stays
    public bool MustChangePassword { get; set; }    // set by an admin-chosen temporary password
}
