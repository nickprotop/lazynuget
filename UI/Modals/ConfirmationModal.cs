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
/// Async confirmation dialog (Yes/No) using ModalBase pattern
/// </summary>
public class ConfirmationModal : ModalBase<bool>
{
    private readonly string _title;
    private readonly string _message;
    private readonly string _yesText;
    private readonly string _noText;

    private ConfirmationModal(string title, string message, string yesText, string noText)
    {
        _title = title;
        _message = message;
        _yesText = yesText;
        _noText = noText;
    }

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
        var modal = new ConfirmationModal(title, message, yesText, noText);
        return modal.ShowAsync(windowSystem, parentWindow);
    }

    protected override string GetTitle() => _title;

    protected override (int width, int height) GetSize() => (50, 14);

    protected override BorderStyle GetBorderStyle() => BorderStyle.Single;

    protected override Color GetBorderColor() => Color.Grey35;

    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        // Title header
        Modal.AddControl(Controls.Markup()
            .AddLine($"[{ColorScheme.PrimaryMarkup}]{Markup.Escape(_title)}[/]")
            .WithAlignment(HorizontalAlignment.Center)
            .WithMargin(1, 1, 0, 0)
            .Build());

        // Message body
        Modal.AddControl(Controls.Markup()
            .AddLine($"[{ColorScheme.SecondaryMarkup}]{Markup.Escape(_message)}[/]")
            .WithAlignment(HorizontalAlignment.Center)
            .WithMargin(1, 1, 1, 1)
            .Build());

        // Buttons
        var yesButton = Controls.Button($"  {_yesText} (Y)  ")
            .WithAlignment(HorizontalAlignment.Center)
            .WithMargin(2, 0, 0, 0)
            .OnClick((s, e) => CloseWithResult(true))
            .Build();

        var noButton = Controls.Button($"  {_noText} (N)  ")
            .WithAlignment(HorizontalAlignment.Center)
            .WithMargin(2, 0, 0, 0)
            .OnClick((s, e) => CloseWithResult(false))
            .Build();

        var buttonGrid = Controls.HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Center)
            .Column(col => col.Add(yesButton))
            .Column(col => col.Width(2))
            .Column(col => col.Add(noButton))
            .Build();
        Modal.AddControl(buttonGrid);

        // Bottom rule + hint
        Modal.AddControl(Controls.RuleBuilder()
            .StickyBottom()
            .WithColor(ColorScheme.RuleColor)
            .Build());

        Modal.AddControl(Controls.Markup()
            .AddLine($"[{ColorScheme.MutedMarkup}]Y:Confirm  N/Esc:Cancel[/]")
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .Build());
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Y || e.KeyInfo.Key == ConsoleKey.Enter)
        {
            CloseWithResult(true);
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.N)
        {
            CloseWithResult(false);
            e.Handled = true;
        }
        else
        {
            // Let base handle Escape
            base.OnKeyPressed(sender, e);
        }
    }
}
