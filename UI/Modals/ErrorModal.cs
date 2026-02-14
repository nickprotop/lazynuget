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
/// Error display modal with scrollable details using ModalBase pattern
/// </summary>
public class ErrorModal : ModalBase<bool>
{
    private readonly string _title;
    private readonly string _message;
    private readonly string? _details;
    private readonly IReadOnlyList<string>? _suggestions;

    private ErrorModal(string title, string message, string? details, IReadOnlyList<string>? suggestions)
    {
        _title = title;
        _message = message;
        _details = details;
        _suggestions = suggestions;
    }

    /// <summary>
    /// Show an error dialog and wait for dismissal
    /// </summary>
    public static Task ShowAsync(
        ConsoleWindowSystem windowSystem,
        string title,
        string message,
        string? details = null,
        Window? parentWindow = null,
        IReadOnlyList<string>? suggestions = null)
    {
        var modal = new ErrorModal(title, message, details, suggestions);
        return modal.ShowAsync(windowSystem, parentWindow);
    }

    protected override string GetTitle() => _title;

    protected override (int width, int height) GetSize()
    {
        var height = 18;
        if (_suggestions is { Count: > 0 })
            height += 2 + _suggestions.Count;
        return (60, height);
    }

    protected override BorderStyle GetBorderStyle() => BorderStyle.Single;

    protected override Color GetBorderColor() => Color.Grey35;

    protected override bool GetDefaultResult() => true;

    protected override void BuildContent()
    {
        // Error icon + title
        Modal.AddControl(Controls.Markup()
            .AddLine($"[{ColorScheme.ErrorMarkup} bold]⚠ {Markup.Escape(_title)}[/]")
            .WithAlignment(HorizontalAlignment.Center)
            .WithMargin(1, 1, 0, 0)
            .Build());

        // Message
        Modal.AddControl(Controls.Markup()
            .AddLine($"[{ColorScheme.SecondaryMarkup}]{Markup.Escape(_message)}[/]")
            .WithMargin(2, 1, 0, 0)
            .Build());

        // Details section (scrollable if long)
        if (!string.IsNullOrEmpty(_details))
        {
            Modal.AddControl(Controls.RuleBuilder()
                .WithColor(ColorScheme.RuleColor)
                .Build());

            var detailsBuilder = Controls.Markup()
                .AddLine($"[{ColorScheme.MutedMarkup} bold]Details:[/]");

            // Split long details into lines
            foreach (var line in _details.Split('\n'))
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
            Modal.AddControl(detailsPanel);
        }

        // Suggestions section
        if (_suggestions is { Count: > 0 })
        {
            Modal.AddControl(Controls.RuleBuilder()
                .WithColor(ColorScheme.RuleColor)
                .Build());

            var suggestionsBuilder = Controls.Markup()
                .AddLine($"[{ColorScheme.PrimaryMarkup} bold]Suggestions:[/]");

            foreach (var suggestion in _suggestions)
            {
                suggestionsBuilder.AddLine($"[{ColorScheme.SecondaryMarkup}]  - {Markup.Escape(suggestion)}[/]");
            }

            Modal.AddControl(suggestionsBuilder.WithMargin(2, 0, 0, 0).Build());
        }

        // Bottom rule + dismiss hint
        Modal.AddControl(Controls.RuleBuilder()
            .StickyBottom()
            .WithColor(ColorScheme.RuleColor)
            .Build());

        Modal.AddControl(Controls.Markup()
            .AddLine($"[{ColorScheme.MutedMarkup}]Press Enter or Esc to close[/]")
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .Build());
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Enter)
        {
            CloseWithResult(true);
            e.Handled = true;
        }
        else
        {
            // Let base handle Escape
            base.OnKeyPressed(sender, e);
        }
    }
}
