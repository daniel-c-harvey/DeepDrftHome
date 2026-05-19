using MudBlazor;

namespace DeepDrftShared.Client.Common;

/// <summary>
/// Canonical MudBlazor theme for DeepDrft apps. Holds the wireframe light palette
/// (navy / green / warm off-white), the dark palette (navy ground / green-accent
/// / off-white), and the shared typography (Cormorant Garamond / Geist Mono / DM Sans).
///
/// <see cref="Default"/> is used by the public site (light+dark toggle).
/// <see cref="Cms"/> is used by the CMS host (light-only, solid navy AppBar for visual distinction).
/// Hosts decide light vs dark by toggling <c>IsDarkMode</c> on <c>MudThemeProvider</c>.
/// </summary>
// MudColor accepts string via implicit conversion, and MudTheme palette/typography
// properties are typed as nullable in MudBlazor. These assignments are valid but
// the nullable annotations trigger CS8601 as false positives.
#pragma warning disable CS8601
public static class DeepDrftPalettes
{
    /// <summary>
    /// The single MudTheme exposing both light and dark palettes plus typography.
    /// Hosts pick a mode via <c>MudThemeProvider IsDarkMode</c>.
    /// </summary>
    public static MudTheme Default { get; } = new()
    {
        PaletteLight = Light,
        PaletteDark = Dark,
        Typography = Typography,
    };

    /// <summary>
    /// CMS-specific MudTheme. Same as <see cref="Default"/> but with a solid navy AppBar
    /// on the light palette to visually distinguish the admin surface from the public site.
    /// </summary>
    public static MudTheme Cms { get; } = new()
    {
        PaletteLight = CmsLight,
        PaletteDark = Dark,
        Typography = Typography,
    };

    // Wireframe light palette - navy / green / warm off-white
    public static PaletteLight Light { get; } = new()
    {
        Primary = "#0D1B2A",                              // Navy - text, buttons
        PrimaryDarken = "#162437",                        // Navy-mid - hover, elevated surfaces
        Secondary = "#1A3C34",                            // Deep green - italic emphasis
        Tertiary = "#3D7A68",                             // Green-accent - interactive hovers/active/icons
        Background = "#FAFAF8",                           // Warm off-white
        BackgroundGray = "#F0F2F0",                       // Slight warm gray for contrast
        Surface = "#FAFAF8",
        AppbarBackground = "rgba(250,250,248,0.88)",
        AppbarText = "#0D1B2A",
        TextPrimary = "#0D1B2A",
        TextSecondary = "#8A9BB0",                        // Muted - secondary text, nav at rest
        TextDisabled = "rgba(13,27,42,0.38)",
        ActionDefault = "#8A9BB0",
        ActionDisabled = "rgba(13,27,42,0.26)",
        ActionDisabledBackground = "rgba(13,27,42,0.12)",
        Divider = "rgba(13,27,42,0.10)",
        DividerLight = "rgba(13,27,42,0.06)",
        TableLines = "rgba(13,27,42,0.10)",
        LinesDefault = "rgba(13,27,42,0.10)",
        LinesInputs = "rgba(13,27,42,0.30)",
        OverlayLight = "rgba(250,250,248,0.5)",
        OverlayDark = "rgba(13,27,42,0.5)",
        // Semantic (Info/Success/Warning/Error) intentionally left at MudBlazor defaults
    };

    // CMS light palette - same as Light but with a solid navy AppBar to distinguish
    // the admin surface from the public site's semi-transparent off-white AppBar.
    public static PaletteLight CmsLight { get; } = new()
    {
        Primary          = Light.Primary,
        PrimaryDarken    = Light.PrimaryDarken,
        Secondary        = Light.Secondary,
        Tertiary         = Light.Tertiary,
        Background       = Light.Background,
        BackgroundGray   = Light.BackgroundGray,
        Surface          = Light.Surface,
        AppbarBackground = "#0D1B2A",                    // Solid navy - CMS visual distinction
        AppbarText       = "#FAFAF8",                    // Off-white text on navy
        TextPrimary      = Light.TextPrimary,
        TextSecondary    = Light.TextSecondary,
        TextDisabled     = Light.TextDisabled,
        ActionDefault    = Light.ActionDefault,
        ActionDisabled   = Light.ActionDisabled,
        ActionDisabledBackground = Light.ActionDisabledBackground,
        Divider          = Light.Divider,
        DividerLight     = Light.DividerLight,
        TableLines       = Light.TableLines,
        LinesDefault     = Light.LinesDefault,
        LinesInputs      = Light.LinesInputs,
        OverlayLight     = Light.OverlayLight,
        OverlayDark      = Light.OverlayDark,
        // Semantic (Info/Success/Warning/Error) intentionally left at MudBlazor defaults
    };

    // Wireframe dark palette - navy ground / green-accent / off-white.
    // Mirrors the light palette's vocabulary on a dark ground; the coral/lowcountry
    // identity has been retired. On dark, green-accent (#3D7A68) becomes the primary
    // interactive colour and off-white (#FAFAF8) becomes the secondary emphasis.
    public static PaletteDark Dark { get; } = new()
    {
        Primary = "#3D7A68",                              // Green-accent - interactive colour on dark
        PrimaryDarken = "#2A5C4F",                        // Green-light - hover/pressed
        Secondary = "#FAFAF8",                            // Off-white - secondary emphasis
        Tertiary = "#1A3C34",                             // Deep green - tertiary accent
        Background = "#0D1B2A",                           // Navy - the light palette's primary as the dark ground
        Surface = "#162437",                              // Navy-mid - elevated cards/panels
        AppbarBackground = "rgba(13,27,42,0.92)",         // Semi-opaque navy
        AppbarText = "#FAFAF8",
        DrawerBackground = "#162437",                     // Navy-mid
        DrawerText = "#FAFAF8",
        DrawerIcon = "#8A9BB0",                           // Muted
        // TextPrimary (#FAFAF8) on Background (#0D1B2A): contrast ratio ~16.5:1 - passes WCAG AA (>=4.5:1)
        TextPrimary = "#FAFAF8",
        // TextSecondary (#8A9BB0) on Background (#0D1B2A): contrast ratio ~6.1:1 - passes WCAG AA for normal text
        TextSecondary = "#8A9BB0",
        TextDisabled = "rgba(250,250,248,0.38)",
        ActionDefault = "#8A9BB0",
        ActionDisabled = "rgba(250,250,248,0.26)",
        ActionDisabledBackground = "rgba(250,250,248,0.12)",
        Divider = "rgba(250,250,248,0.10)",
        DividerLight = "rgba(250,250,248,0.06)",
        TableLines = "rgba(250,250,248,0.10)",
        LinesDefault = "rgba(250,250,248,0.10)",
        LinesInputs = "rgba(250,250,248,0.30)",
        OverlayLight = "rgba(13,27,42,0.5)",
        OverlayDark = "rgba(0,0,0,0.7)",
        // Semantic (Info/Success/Warning/Error) intentionally left at MudBlazor defaults
    };

    // Shared typography - Cormorant Garamond for display, Geist Mono for mono, DM Sans for body.
    public static Typography Typography { get; } = new()
    {
        Default    = new DefaultTypography  { FontFamily = new[] { "DM Sans",             "sans-serif" } },
        H1         = new H1Typography       { FontFamily = new[] { "Cormorant Garamond",  "Georgia", "serif" } },
        H2         = new H2Typography       { FontFamily = new[] { "Cormorant Garamond",  "Georgia", "serif" } },
        H3         = new H3Typography       { FontFamily = new[] { "Cormorant Garamond",  "Georgia", "serif" } },
        H4         = new H4Typography       { FontFamily = new[] { "Cormorant Garamond",  "Georgia", "serif" } },
        // Extended to H5/H6 beyond spec - keeps heading hierarchy consistent in Cormorant Garamond
        H5         = new H5Typography       { FontFamily = new[] { "Cormorant Garamond",  "Georgia", "serif" } },
        H6         = new H6Typography       { FontFamily = new[] { "Cormorant Garamond",  "Georgia", "serif" } },
        Subtitle1  = new Subtitle1Typography{ FontFamily = new[] { "Geist Mono",          "monospace" } },
        Body1      = new Body1Typography    { FontFamily = new[] { "DM Sans",             "sans-serif" } },
        Body2      = new Body2Typography    { FontFamily = new[] { "DM Sans",             "sans-serif" } },
        Caption    = new CaptionTypography  { FontFamily = new[] { "Geist Mono",          "monospace" } },
        Button     = new ButtonTypography   { FontFamily = new[] { "Geist Mono",          "monospace" } },
    };
}
#pragma warning restore CS8601
