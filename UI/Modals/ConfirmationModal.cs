using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drawing;
using SharpConsoleUI.Layout;
using Spectre.Console;
using LazyNuGet.UI.Utilities;
using HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment;

namespace LazyNuGet.UI.Modals;

/// <summary>
/// Async confirmation dialog (Yes/No) using TaskCompletionSource pattern
/// </summary>
public static class ConfirmationModal
{
    /// <summary>
    /// Show a confirmation dialog and await the user's choice
    /// </summary>
    public static Task<bool> ShowAsync(
        ConsoleWindowSystem windowSystem,
        string title,
        string message,
        string yesText = "Yes",
        string noText = "No",
        Window? parentWindow = null)
    {
        var tcs = new TaskCompletionSource<bool>();
        bool confirmed = false;

        var modal = new WindowBuilder(windowSystem)
            .WithTitle(title)
            .Centered()
            .WithSize(50, 14)
            .AsModal()
            .WithBorderStyle(BorderStyle.Single)
            .WithBorderColor(Color.Grey35)
            .Resizable(true)
            .Movable(true)
            .Minimizable(false)
            .WithColors(ColorScheme.StatusBarBackground, Color.Grey93)
            .Build();

        // Title header
        modal.AddControl(Controls.Markup()
            .AddLine($"[{ColorScheme.PrimaryMarkup}]{Markup.Escape(title)}[/]")
            .WithAlignment(HorizontalAlignment.Center)
            .WithMargin(1, 1, 0, 0)
            .Build());

        // Message body
        modal.AddControl(Controls.Markup()
            .AddLine($"[{ColorScheme.SecondaryMarkup}]{Markup.Escape(message)}[/]")
            .WithAlignment(HorizontalAlignment.Center)
            .WithMargin(1, 1, 1, 1)
            .Build());

        // Buttons
        var yesButton = Controls.Button($"  {yesText} (Y)  ")
            .WithAlignment(HorizontalAlignment.Center)
            .WithMargin(2, 0, 0, 0)
            .OnClick((s, e) =>
            {
                confirmed = true;
                modal.Close();
            })
            .Build();

        var noButton = Controls.Button($"  {noText} (N)  ")
            .WithAlignment(HorizontalAlignment.Center)
            .WithMargin(2, 0, 0, 0)
            .OnClick((s, e) =>
            {
                confirmed = false;
                modal.Close();
            })
            .Build();

        var buttonGrid = Controls.HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Center)
            .Column(col => col.Add(yesButton))
            .Column(col => col.Width(2))
            .Column(col => col.Add(noButton))
            .Build();
        modal.AddControl(buttonGrid);

        // Bottom rule + hint
        modal.AddControl(Controls.RuleBuilder()
            .StickyBottom()
            .WithColor(ColorScheme.RuleColor)
            .Build());

        modal.AddControl(Controls.Markup()
            .AddLine($"[{ColorScheme.MutedMarkup}]Y:Confirm  N/Esc:Cancel[/]")
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .Build());

        // Keyboard handling
        modal.KeyPressed += (sender, e) =>
        {
            if (e.KeyInfo.Key == ConsoleKey.Y || e.KeyInfo.Key == ConsoleKey.Enter)
            {
                confirmed = true;
                modal.Close();
                e.Handled = true;
            }
            else if (e.KeyInfo.Key == ConsoleKey.N || e.KeyInfo.Key == ConsoleKey.Escape)
            {
                confirmed = false;
                modal.Close();
                e.Handled = true;
            }
        };

        modal.OnClosed += (s, e) => tcs.TrySetResult(confirmed);

        windowSystem.AddWindow(modal);
        windowSystem.SetActiveWindow(modal);

        return tcs.Task;
    }
}
