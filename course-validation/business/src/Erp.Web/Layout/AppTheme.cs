using MudBlazor;

namespace Erp.Web.Layout;

// One look for every screen: a deep-blue top bar, neutral greys, one blue for what you can act on, flat surfaces
// with thin borders. Status colours (green, amber, red) are kept for meaning, never for decoration. The dark palette
// is the same design at night: same roles, lighter blue and softer colours, so contrast stays readable.
internal static class AppTheme
{
    private static readonly string[] Font = ["system-ui", "-apple-system", "Segoe UI", "Roboto", "Helvetica Neue", "Arial", "sans-serif"];

    public static readonly MudTheme Theme = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#1d4ed8",
            PrimaryDarken = "#1e40af",
            PrimaryLighten = "#dbe6fe",
            Secondary = "#475467",
            Info = "#1d4ed8",
            Success = "#067647",
            Warning = "#b54708",
            Error = "#c4320a",
            Background = "#f6f7f9",
            Surface = "#ffffff",
            AppbarBackground = "#1e3a8a",
            AppbarText = "#ffffff",
            DrawerBackground = "#ffffff",
            DrawerText = "#344054",
            DrawerIcon = "#667085",
            TextPrimary = "#101828",
            TextSecondary = "#475467",
            TextDisabled = "#98a2b3",
            ActionDefault = "#475467",
            Divider = "#e4e7ec",
            DividerLight = "#f0f1f4",
            LinesDefault = "#e4e7ec",
            LinesInputs = "#d0d5dd",
            TableLines = "#eaecf0",
            TableHover = "#f5f7fb",
            TableStriped = "#fafbfc",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#7aa2ff",
            PrimaryDarken = "#5b88f5",
            PrimaryLighten = "#1d2b4f",
            Secondary = "#a3abb9",
            Info = "#7aa2ff",
            Success = "#4ccf8a",
            Warning = "#f5b14c",
            Error = "#ff7a66",
            Background = "#0f1218",
            Surface = "#171b23",
            AppbarBackground = "#172554",
            AppbarText = "#ffffff",
            DrawerBackground = "#171b23",
            DrawerText = "#d0d5dd",
            DrawerIcon = "#98a2b3",
            TextPrimary = "#eceef2",
            TextSecondary = "#a3abb9",
            TextDisabled = "#667085",
            ActionDefault = "#a3abb9",
            Divider = "#2a303c",
            DividerLight = "#222733",
            LinesDefault = "#2a303c",
            LinesInputs = "#3a4250",
            TableLines = "#262b36",
            TableHover = "#1e232d",
            TableStriped = "#1a1e27",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = Font, FontSize = ".875rem", LineHeight = "1.5" },
            H4 = new H4Typography { FontFamily = Font, FontSize = "1.5rem", FontWeight = "600", LineHeight = "1.3", LetterSpacing = "-.01em" },
            H5 = new H5Typography { FontFamily = Font, FontSize = "1.125rem", FontWeight = "600", LineHeight = "1.4" },
            H6 = new H6Typography { FontFamily = Font, FontSize = "1rem", FontWeight = "600", LineHeight = "1.4" },
            Subtitle1 = new Subtitle1Typography { FontFamily = Font, FontSize = ".9375rem", FontWeight = "600" },
            // Table headers use Subtitle2: a little smaller and bolder than the rows.
            Subtitle2 = new Subtitle2Typography { FontFamily = Font, FontSize = ".8125rem", FontWeight = "600" },
            Body1 = new Body1Typography { FontFamily = Font, FontSize = ".9375rem" },
            Body2 = new Body2Typography { FontFamily = Font, FontSize = ".875rem" },
            Button = new ButtonTypography { FontFamily = Font, FontSize = ".875rem", FontWeight = "500", TextTransform = "none", LetterSpacing = "0" },
            Caption = new CaptionTypography { FontFamily = Font, FontSize = ".75rem" },
            Overline = new OverlineTypography { FontFamily = Font, FontSize = ".6875rem", FontWeight = "600", LetterSpacing = ".06em" },
        },
        LayoutProperties = new LayoutProperties { DefaultBorderRadius = "8px", AppbarHeight = "56px", DrawerWidthLeft = "248px" },
        Shadows = Flat(),
    };

    // Elevations 1–5 are what sits on the page (papers, tables, buttons): flat, separated by a hairline border.
    // Menus, messages and dialogs use the higher ones and keep their shadow, because they float above the page.
    private static Shadow Flat()
    {
        var shadow = new Shadow();
        for (var level = 1; level <= 5; level++) shadow.Elevation[level] = "none";
        return shadow;
    }
}
