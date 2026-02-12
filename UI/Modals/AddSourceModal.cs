using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;
using SharpConsoleUI.Drawing;
using SharpConsoleUI.Layout;
using Spectre.Console;
using LazyNuGet.Services;
using LazyNuGet.UI.Utilities;
using HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment;

namespace LazyNuGet.UI.Modals;

/// <summary>
/// Sub-modal for adding a custom NuGet source
/// </summary>
public static class AddSourceModal
{
    public static Task<CustomNuGetSource?> ShowAsync(
        ConsoleWindowSystem windowSystem,
        Window? parentWindow = null)
    {
        var tcs = new TaskCompletionSource<CustomNuGetSource?>();

        PromptControl? nameInput = null;
        PromptControl? urlInput = null;

        var modal = new WindowBuilder(windowSystem)
            .WithTitle("Add NuGet Source")
            .Centered()
            .WithSize(70, 16)
            .AsModal()
            .Resizable(false)
            .Movable(true)
            .Minimizable(false)
            .WithColors(ColorScheme.WindowBackground, Color.Grey93)
            .WithBorderStyle(BorderStyle.DoubleLine)
            .WithBorderColor(ColorScheme.BorderColor)
            .Build();

        // Header
        var header = Controls.Markup()
            .AddLine($"[{ColorScheme.PrimaryMarkup} bold]Add Custom NuGet Source[/]")
            .AddLine($"[{ColorScheme.SecondaryMarkup}]Enter a name and URL for the package source[/]")
            .WithMargin(2, 2, 2, 1)
            .Build();

        // Name input
        var nameLabel = Controls.Markup()
            .AddLine($"[{ColorScheme.SecondaryMarkup}]Name:[/]")
            .WithMargin(2, 0, 2, 0)
            .Build();

        nameInput = Controls.Prompt("Name: ")
            .WithInput("")
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithMargin(2, 0, 2, 1)
            .Build();

        // URL input
        var urlLabel = Controls.Markup()
            .AddLine($"[{ColorScheme.SecondaryMarkup}]URL:[/]")
            .WithMargin(2, 0, 2, 0)
            .Build();

        urlInput = Controls.Prompt("URL: ")
            .WithInput("")
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithMargin(2, 0, 2, 1)
            .Build();

        // Separator
        var separator = Controls.RuleBuilder()
            .WithColor(ColorScheme.RuleColor)
            .StickyBottom()
            .Build();
        separator.Margin = new Margin(2, 0, 2, 0);

        // Buttons
        var okButton = Controls.Button($"[{ColorScheme.PrimaryMarkup}]Add (Enter)[/]")
            .WithMargin(1, 0, 0, 0)
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.DarkGreen)
            .WithFocusedForegroundColor(Color.White)
            .Build();

        var cancelButton = Controls.Button("[grey93]Cancel (Esc)[/]")
            .WithMargin(1, 0, 0, 0)
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .Build();

        void TrySubmit()
        {
            var name = nameInput?.Input?.Trim() ?? "";
            var url = urlInput?.Input?.Trim() ?? "";

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url))
            {
                windowSystem.NotificationStateService.ShowNotification(
                    "Validation Error",
                    "Both name and URL are required.",
                    NotificationSeverity.Warning,
                    timeout: 3000,
                    parentWindow: modal);
                return;
            }

            modal.Close();
            tcs.TrySetResult(new CustomNuGetSource
            {
                Name = name,
                Url = url,
                IsEnabled = true
            });
        }

        okButton.Click += (s, e) => TrySubmit();
        cancelButton.Click += (s, e) =>
        {
            modal.Close();
            tcs.TrySetResult(null);
        };

        var buttonGrid = Controls.HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .Column(col => col.Add(okButton))
            .Column(col => col.Width(2))
            .Column(col => col.Add(cancelButton))
            .Build();
        buttonGrid.Margin = new Margin(0, 0, 0, 0);

        // Assemble modal
        modal.AddControl(header);
        modal.AddControl(nameLabel);
        modal.AddControl(nameInput);
        modal.AddControl(urlLabel);
        modal.AddControl(urlInput);
        modal.AddControl(separator);
        modal.AddControl(buttonGrid);

        // Keyboard handling
        modal.KeyPressed += (s, e) =>
        {
            if (e.KeyInfo.Key == ConsoleKey.Escape)
            {
                modal.Close();
                tcs.TrySetResult(null);
                e.Handled = true;
            }
        };

        modal.OnClosed += (s, e) => tcs.TrySetResult(null);

        // Show modal
        windowSystem.AddWindow(modal);
        windowSystem.SetActiveWindow(modal);
        nameInput.SetFocus(true, FocusReason.Programmatic);

        return tcs.Task;
    }
}
