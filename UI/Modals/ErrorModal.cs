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
/// Error display modal with scrollable details
/// </summary>
public static class ErrorModal
{
    /// <summary>
    /// Show an error dialog and wait for dismissal
    /// </summary>
    public static Task ShowAsync(
        ConsoleWindowSystem windowSystem,
        string title,
        string message,
        string? details = null,
        Window? parentWindow = null)
    {
        var tcs = new TaskCompletionSource<bool>();

        var modal = new WindowBuilder(windowSystem)
            .WithTitle(title)
            .Centered()
            .WithSize(60, 18)
            .AsModal()
            .WithBorderStyle(BorderStyle.Single)
            .WithBorderColor(Color.Grey35)
            .Resizable(true)
            .Movable(true)
            .Minimizable(false)
            .WithColors(ColorScheme.StatusBarBackground, Color.Grey93)
            .Build();

        // Error icon + title
        modal.AddControl(Controls.Markup()
            .AddLine($"[{ColorScheme.ErrorMarkup} bold]⚠ {Markup.Escape(title)}[/]")
            .WithAlignment(HorizontalAlignment.Center)
            .WithMargin(1, 1, 0, 0)
            .Build());

        // Message
        modal.AddControl(Controls.Markup()
            .AddLine($"[{ColorScheme.SecondaryMarkup}]{Markup.Escape(message)}[/]")
            .WithMargin(2, 1, 0, 0)
            .Build());

        // Details section (scrollable if long)
        if (!string.IsNullOrEmpty(details))
        {
            modal.AddControl(Controls.RuleBuilder()
                .WithColor(ColorScheme.RuleColor)
                .Build());

            var detailsBuilder = Controls.Markup()
                .AddLine($"[{ColorScheme.MutedMarkup} bold]Details:[/]");

            // Split long details into lines
            foreach (var line in details.Split('\n'))
            {
                detailsBuilder.AddLine($"[{ColorScheme.MutedMarkup}]{Markup.Escape(line.TrimEnd())}[/]");
            }

            var detailsPanel = Controls.ScrollablePanel()
                .WithVerticalScroll(ScrollMode.Scroll)
                .WithScrollbar(true)
                .WithMouseWheel(true)
                .WithAutoScroll(false)
                .WithVerticalAlignment(VerticalAlignment.Fill)
                .WithColors(Color.Grey93, ColorScheme.StatusBarBackground)
                .Build();

            detailsPanel.AddControl(detailsBuilder.WithMargin(2, 0, 0, 0).Build());
            modal.AddControl(detailsPanel);
        }

        // Bottom rule + dismiss hint
        modal.AddControl(Controls.RuleBuilder()
            .StickyBottom()
            .WithColor(ColorScheme.RuleColor)
            .Build());

        modal.AddControl(Controls.Markup()
            .AddLine($"[{ColorScheme.MutedMarkup}]Press Enter or Esc to close[/]")
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .Build());

        // Keyboard handling
        modal.KeyPressed += (sender, e) =>
        {
            if (e.KeyInfo.Key == ConsoleKey.Enter || e.KeyInfo.Key == ConsoleKey.Escape)
            {
                modal.Close();
                e.Handled = true;
            }
        };

        modal.OnClosed += (s, e) => tcs.TrySetResult(true);

        windowSystem.AddWindow(modal);
        windowSystem.SetActiveWindow(modal);

        return tcs.Task;
    }
}
