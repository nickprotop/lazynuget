using SharpConsoleUI;

namespace LazyNuGet.UI.Utilities;

/// <summary>
/// Centralized color definitions matching AgentStudio aesthetic
/// </summary>
public static class ColorScheme
{
    // Background colors.
    //
    // These used to be four steps of one grey ramp with the sidebar and the details pane on
    // the SAME value (Grey19), so the two panels never read as separate surfaces and the whole
    // app looked like one flat sheet. Now the surfaces step: the chrome recedes, the sidebar
    // sits back, and the details pane comes forward as the surface you work on.
    //
    // The lift is a cool one rather than plain grey, so the panes carry a trace of the accent
    // hue instead of reading as dead neutral.
    public static readonly Color WindowBackground = new Color(16, 17, 21);
    public static readonly Color StatusBarBackground = new Color(13, 14, 18);
    public static readonly Color SidebarBackground = new Color(21, 23, 28);
    public static readonly Color DetailsPanelBackground = new Color(27, 30, 36);

    /// <summary>A panel inset on the details surface: a table, a card, a well.</summary>
    public static readonly Color InsetBackground = new Color(33, 37, 44);

    /// <summary>A table header, one more step forward than the inset it heads.</summary>
    public static readonly Color HeaderBackground = new Color(42, 47, 56);

    // Border and separator colors. A rule is quieter than a border: a rule divides bands
    // inside one surface, a border draws the edge of an object, so they should not be the
    // same weight. They were both Grey23.
    public static readonly Color RuleColor = new Color(42, 46, 54);
    public static readonly Color BorderColor = new Color(58, 64, 75);
    public static readonly Color AccentBorderColor = new Color(70, 92, 112);

    // Scrollbars. The stock theme uses Cyan1, which outshouts every piece of real content on
    // a dark surface. A thumb should be findable when you look for it, not before.
    public static readonly Color ScrollbarThumbColor = new Color(92, 104, 122);
    public static readonly Color ScrollbarThumbUnfocusedColor = new Color(58, 64, 75);
    public static readonly Color ScrollbarTrackColor = new Color(31, 34, 41);

    // Text colors
    public static readonly Color PrimaryText = Color.Cyan1;
    public static readonly Color SecondaryText = Color.Grey70;
    public static readonly Color MutedText = Color.Grey50;

    // Status colors
    public static readonly Color SuccessColor = Color.Green;
    public static readonly Color WarningColor = Color.Yellow;
    public static readonly Color ErrorColor = Color.Red;
    public static readonly Color InfoColor = Color.Cyan1;

    // Markup strings for convenience
    public const string PrimaryMarkup = "cyan1 bold";
    public const string SecondaryMarkup = "grey70";
    public const string MutedMarkup = "grey50";
    public const string SuccessMarkup = "green";
    public const string WarningMarkup = "yellow";
    public const string ErrorMarkup = "red";
    public const string InfoMarkup = "cyan1";
}
