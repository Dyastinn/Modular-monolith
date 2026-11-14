using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;

namespace Erp.Web.Shared;

// Asks before leaving a form whose changes weren't saved. A page compares a snapshot of its fields with the last
// saved one, and hands NavigationLock this check.
public static class UnsavedChanges
{
    public static async Task ConfirmLeave(IDialogService dialogs, LocationChangingContext context, bool unsaved)
    {
        if (!unsaved) return;
        var leave = await dialogs.ShowMessageBoxAsync("Leave without saving?",
            "Your changes haven't been saved. If you leave now, they're lost.", yesText: "Leave", cancelText: "Stay");
        if (leave != true) context.PreventNavigation();
    }
}
