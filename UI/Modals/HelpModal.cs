using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drawing;
using SharpConsoleUI.Layout;
using Spectre.Console;
using LazyNuGet.UI.Utilities;
using HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment;
using VerticalAlignment = SharpConsoleUI.Layout.VerticalAlignment;

namespace LazyNuGet.UI.Modals;

/// <summary>
/// Help modal displaying categorized keyboard shortcuts.
/// Any key press closes the modal.
/// </summary>
public class HelpModal : ModalBase<bool>
{
    private HelpModal() { }

    /// <summary>
    /// Show the help modal and wait for dismissal
    /// </summary>
    public static new Task ShowAsync(
        ConsoleWindowSystem windowSystem,
        Window? parentWindow = null)
    {
        var modal = new HelpModal();
        return ((ModalBase<bool>)modal).ShowAsync(windowSystem, parentWindow);
    }

    protected override string GetTitle() => "Keyboard Shortcuts";

    protected override (int width, int height) GetSize() => (60, 30);

    protected override bool GetResizable() => false;

    protected override bool GetMovable() => false;

    protected override BorderStyle GetBorderStyle() => BorderStyle.DoubleLine;

    protected override Color GetBorderColor() => ColorScheme.BorderColor;

    protected override bool GetDefaultResult() => true;

    protected override void BuildContent()
    {
        var scrollPanel = Controls.ScrollablePanel()
            .WithVerticalScroll(ScrollMode.Scroll)
            .WithScrollbar(true)
            .WithMouseWheel(true)
            .WithAutoScroll(false)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithColors(Color.Grey93, ColorScheme.WindowBackground)
            .Build();

        var content = Controls.Markup()
            .WithMargin(2, 1, 2, 1);

        // Navigation
        content.AddLine($"[{ColorScheme.PrimaryMarkup}]Navigation[/]");
        content.AddLine($"  [{ColorScheme.InfoMarkup}]Up/Down[/]      [{ColorScheme.SecondaryMarkup}]Navigate list items[/]");
        content.AddLine($"  [{ColorScheme.InfoMarkup}]Enter[/]        [{ColorScheme.SecondaryMarkup}]Select / drill into[/]");
        content.AddLine($"  [{ColorScheme.InfoMarkup}]Escape[/]       [{ColorScheme.SecondaryMarkup}]Go back / exit[/]");
        content.AddLine($"  [{ColorScheme.InfoMarkup}]Ctrl+↑/↓[/]     [{ColorScheme.SecondaryMarkup}]Scroll details panel[/]");
        content.AddLine($"  [{ColorScheme.InfoMarkup}]Tab[/]          [{ColorScheme.SecondaryMarkup}]Cycle focus[/]");
        content.AddLine("");

        // Search & Management
        content.AddLine($"[{ColorScheme.PrimaryMarkup}]Search & Management[/]");
        content.AddLine($"  [{ColorScheme.InfoMarkup}]Ctrl+S[/]       [{ColorScheme.SecondaryMarkup}]Search NuGet packages[/]");
        content.AddLine($"  [{ColorScheme.InfoMarkup}]Ctrl+F[/]       [{ColorScheme.SecondaryMarkup}]Filter installed packages[/]");
        content.AddLine($"  [{ColorScheme.InfoMarkup}]Ctrl+R[/]       [{ColorScheme.SecondaryMarkup}]Reload projects[/]");
        content.AddLine($"  [{ColorScheme.InfoMarkup}]Ctrl+O[/]       [{ColorScheme.SecondaryMarkup}]Open folder[/]");
        content.AddLine("");

        // Package Actions
        content.AddLine($"[{ColorScheme.PrimaryMarkup}]Package Actions[/]");
        content.AddLine($"  [{ColorScheme.InfoMarkup}]Ctrl+U[/]       [{ColorScheme.SecondaryMarkup}]Update package / update all[/]");
        content.AddLine($"  [{ColorScheme.InfoMarkup}]Ctrl+V[/]       [{ColorScheme.SecondaryMarkup}]Change package version[/]");
        content.AddLine($"  [{ColorScheme.InfoMarkup}]Ctrl+X[/]       [{ColorScheme.SecondaryMarkup}]Remove package[/]");
        content.AddLine("");

        // Views & Tools
        content.AddLine($"[{ColorScheme.PrimaryMarkup}]Views & Tools[/]");
        content.AddLine($"  [{ColorScheme.InfoMarkup}]Ctrl+D[/]       [{ColorScheme.SecondaryMarkup}]Dependency tree[/]");
        content.AddLine($"  [{ColorScheme.InfoMarkup}]Ctrl+H[/]       [{ColorScheme.SecondaryMarkup}]Operation history[/]");
        content.AddLine($"  [{ColorScheme.InfoMarkup}]Ctrl+P[/]       [{ColorScheme.SecondaryMarkup}]Settings[/]");
        content.AddLine($"  [{ColorScheme.InfoMarkup}]Ctrl+L[/]       [{ColorScheme.SecondaryMarkup}]Log viewer[/]");
        content.AddLine($"  [{ColorScheme.InfoMarkup}]?[/]            [{ColorScheme.SecondaryMarkup}]This help screen[/]");

        scrollPanel.AddControl(content.Build());
        Modal.AddControl(scrollPanel);

        // Bottom rule + dismiss hint
        Modal.AddControl(Controls.RuleBuilder()
            .StickyBottom()
            .WithColor(ColorScheme.RuleColor)
            .Build());

        Modal.AddControl(Controls.Markup()
            .AddLine($"[{ColorScheme.MutedMarkup}]Press any key to close[/]")
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .Build());
    }

    /// <summary>
    /// Any key closes the modal
    /// </summary>
    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        CloseWithResult(true);
        e.Handled = true;
    }
}
