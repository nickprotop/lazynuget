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
/// Sub-modal for adding a custom NuGet source using ModalBase pattern
/// </summary>
public class AddSourceModal : ModalBase<CustomNuGetSource?>
{
    private PromptControl? _nameInput;
    private PromptControl? _urlInput;
    private PromptControl? _usernameInput;
    private PromptControl? _passwordInput;

    private AddSourceModal()
    {
    }

    public new static Task<CustomNuGetSource?> ShowAsync(
        ConsoleWindowSystem windowSystem,
        Window? parentWindow = null)
    {
        var instance = new AddSourceModal();
        return ((ModalBase<CustomNuGetSource?>)instance).ShowAsync(windowSystem, parentWindow);
    }

    protected override string GetTitle() => "Add NuGet Source";

    protected override (int width, int height) GetSize() => (70, 22);

    protected override bool GetResizable() => false;

    protected override CustomNuGetSource? GetDefaultResult() => null;

    protected override void BuildContent()
    {
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

        _nameInput = Controls.Prompt("Name: ")
            .WithInput("")
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithMargin(2, 0, 2, 1)
            .Build();

        // URL input
        var urlLabel = Controls.Markup()
            .AddLine($"[{ColorScheme.SecondaryMarkup}]URL:[/]")
            .WithMargin(2, 0, 2, 0)
            .Build();

        _urlInput = Controls.Prompt("URL: ")
            .WithInput("")
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithMargin(2, 0, 2, 1)
            .Build();

        // Optional credentials section
        var authLabel = Controls.Markup()
            .AddLine($"[{ColorScheme.SecondaryMarkup}]Username (optional):[/]")
            .WithMargin(2, 0, 2, 0)
            .Build();

        _usernameInput = Controls.Prompt("Username: ")
            .WithInput("")
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithMargin(2, 0, 2, 1)
            .Build();

        var passwordLabel = Controls.Markup()
            .AddLine($"[{ColorScheme.SecondaryMarkup}]Password (optional):[/]")
            .WithMargin(2, 0, 2, 0)
            .Build();

        _passwordInput = Controls.Prompt("Password: ")
            .WithInput("")
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithMargin(2, 0, 2, 1)
            .Build();
        _passwordInput.MaskCharacter = '*';

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

        okButton.Click += (s, e) => TrySubmit();
        cancelButton.Click += (s, e) => CloseWithResult(null);

        var buttonGrid = Controls.HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .Column(col => col.Add(okButton))
            .Column(col => col.Width(2))
            .Column(col => col.Add(cancelButton))
            .Build();
        buttonGrid.Margin = new Margin(0, 0, 0, 0);

        // Assemble modal
        Modal.AddControl(header);
        Modal.AddControl(nameLabel);
        Modal.AddControl(_nameInput);
        Modal.AddControl(urlLabel);
        Modal.AddControl(_urlInput);
        Modal.AddControl(authLabel);
        Modal.AddControl(_usernameInput);
        Modal.AddControl(passwordLabel);
        Modal.AddControl(_passwordInput);
        Modal.AddControl(separator);
        Modal.AddControl(buttonGrid);
    }

    protected override void SetInitialFocus()
    {
        _nameInput?.SetFocus(true, FocusReason.Programmatic);
    }

    private void TrySubmit()
    {
        var name = _nameInput?.Input?.Trim() ?? "";
        var url = _urlInput?.Input?.Trim() ?? "";

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url))
        {
            WindowSystem.NotificationStateService.ShowNotification(
                "Validation Error",
                "Both name and URL are required.",
                NotificationSeverity.Warning,
                timeout: 3000,
                parentWindow: Modal);
            return;
        }

        var username = _usernameInput?.Input?.Trim() ?? "";
        var password = _passwordInput?.Input ?? "";
        var hasCredentials = !string.IsNullOrEmpty(username) || !string.IsNullOrEmpty(password);

        CloseWithResult(new CustomNuGetSource
        {
            Name = name,
            Url = url,
            IsEnabled = true,
            RequiresAuth = hasCredentials,
            Username = hasCredentials ? username : null,
            ClearTextPassword = hasCredentials ? password : null
        });
    }
}
