using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Erp.Web.Shared;

// The app's input fields: MudBlazor's own, with one look decided here, once. Every page uses these instead of the
// Mud* originals, so a form looks the same everywhere and changing the look is a change to this file.
// They take every parameter the original takes; a page can still override the defaults when it must.
// (MudBlazor 9 has no global setting for an input's variant, so the defaults live here.)

public class AppTextField<T> : MudTextField<T>
{
    public AppTextField() { Variant = Variant.Outlined; Margin = Margin.Normal; }
}

public class AppNumericField<T> : MudNumericField<T>
{
    public AppNumericField() { Variant = Variant.Outlined; Margin = Margin.Normal; }
}

public class AppSelect<T> : MudSelect<T>
{
    public AppSelect() { Variant = Variant.Outlined; Margin = Margin.Normal; }
}

public class AppAutocomplete<T> : MudAutocomplete<T>
{
    public AppAutocomplete() { Variant = Variant.Outlined; Margin = Margin.Normal; }
}

public class AppDatePicker : MudDatePicker
{
    public AppDatePicker() { Variant = Variant.Outlined; Margin = Margin.Normal; }
}

// A password field with an eye button: people can check what they typed, because typos in a hidden field are the
// commonest reason a sign-in fails.
public class AppPasswordField : AppTextField<string>
{
    public AppPasswordField()
    {
        InputType = InputType.Password;
        Adornment = Adornment.End;
        AdornmentIcon = Icons.Material.Outlined.Visibility;
        AdornmentAriaLabel = "Show password";
        OnAdornmentClick = EventCallback.Factory.Create<Microsoft.AspNetCore.Components.Web.MouseEventArgs>(this, Toggle);
    }

    private void Toggle()
    {
        var shown = InputType == InputType.Text;
        InputType = shown ? InputType.Password : InputType.Text;
        AdornmentIcon = shown ? Icons.Material.Outlined.Visibility : Icons.Material.Outlined.VisibilityOff;
        AdornmentAriaLabel = shown ? "Show password" : "Hide password";
    }
}
