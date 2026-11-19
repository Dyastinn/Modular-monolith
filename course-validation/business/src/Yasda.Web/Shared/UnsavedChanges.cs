using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;

namespace Yasda.Web.Shared;

// Asks before leaving a form whose changes weren't saved, with the browser's own confirm box. A page compares a
// snapshot of its fields with the last saved one, and hands NavigationLock this check.
public static class UnsavedChanges
{
    public static async Task ConfirmLeave(IJSRuntime js, LocationChangingContext context, bool unsaved)
    {
        if (!unsaved) return;
        if (!await js.InvokeAsync<bool>("confirm", "Your changes haven't been saved. Leave anyway?"))
            context.PreventNavigation();
    }
}
