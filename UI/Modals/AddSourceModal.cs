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
/// Sub-modal for adding or editing a custom NuGet source.
/// </summary>
public class AddSourceModal : ModalBase<CustomNuGetSource?>
{
    private const int ModalWidth        = 70;
    private const int HeightWithAuth    = 26;
    private const int HeightWithoutAuth = 20;

    private PromptControl?   _nameInput;
    private PromptControl?   _urlInput;
    private CheckboxControl? _requiresAuthCheckbox;
    private MarkupControl?   _usernameLabel;
    private PromptControl?   _usernameInput;
    private MarkupControl?   _passwordLabel;
    private PromptControl?   _passwordInput;
    private MarkupControl?   _warningLabel;

    private readonly bool               _editMode;
    private readonly CustomNuGetSource? _existingSource;

    private AddSourceModal() { }

    private AddSourceModal(CustomNuGetSource existing)
    {
        _editMode       = true;
        _existingSource = existing;
    }

    public new static Task<CustomNuGetSource?> ShowAsync(
        ConsoleWindowSystem windowSystem,
        Window? parentWindow = null)
    {
        var instance = new AddSourceModal();
        return ((ModalBase<CustomNuGetSource?>)instance).ShowAsync(windowSystem, parentWindow);
    }

    public static Task<CustomNuGetSource?> ShowEditAsync(
        ConsoleWindowSystem windowSystem,
        CustomNuGetSource existing,
        Window? parentWindow = null)
    {
        var instance = new AddSourceModal(existing);
        return ((ModalBase<CustomNuGetSource?>)instance).ShowAsync(windowSystem, parentWindow);
    }

    protected override string GetTitle() => _editMode ? "Edit NuGet Source" : "Add NuGet Source";

    protected override (int width, int height) GetSize()
    {
        bool initialAuth = _editMode && (_existingSource?.RequiresAuth ?? false);
        return (ModalWidth, initialAuth ? HeightWithAuth : HeightWithoutAuth);
    }

    protected override bool GetResizable() => false;
    protected override CustomNuGetSource? GetDefaultResult() => null;

    protected override void BuildContent()
    {
        var headerText = _editMode
            ? $"[{ColorScheme.PrimaryMarkup} bold]Edit Custom NuGet Source[/]"
            : $"[{ColorScheme.PrimaryMarkup} bold]Add Custom NuGet Source[/]";
        var headerSub = _editMode
            ? $"[{ColorScheme.SecondaryMarkup}]Update URL or credentials for this source[/]"
            : $"[{ColorScheme.SecondaryMarkup}]Enter a name and URL for the package source[/]";

        var header = Controls.Markup()
            .AddLine(headerText)
            .AddLine(headerSub)
            .WithMargin(2, 2, 2, 1)
            .Build();

        // Name
        var nameLabel = Controls.Markup()
            .AddLine(_editMode
                ? $"[{ColorScheme.SecondaryMarkup}]Name (cannot be changed):[/]"
                : $"[{ColorScheme.SecondaryMarkup}]Name:[/]")
            .WithMargin(2, 0, 2, 0)
            .Build();

        _nameInput = Controls.Prompt("Name: ")
            .WithInput(_existingSource?.Name ?? "")
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithMargin(2, 0, 2, 1)
            .Build();

        if (_editMode)
            _nameInput.IsEnabled = false;

        // URL
        var urlLabel = Controls.Markup()
            .AddLine($"[{ColorScheme.SecondaryMarkup}]URL:[/]")
            .WithMargin(2, 0, 2, 0)
            .Build();

        _urlInput = Controls.Prompt("URL: ")
            .WithInput(_existingSource?.Url ?? "")
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithMargin(2, 0, 2, 1)
            .Build();

        // Requires Authentication checkbox
        bool initialAuth = _editMode
            ? (_existingSource?.RequiresAuth ?? false)
            : false;

        _requiresAuthCheckbox = Controls.Checkbox("Requires Authentication")
            .Checked(initialAuth)
            .WithMargin(2, 0, 2, 1)
            .Build();

        // Credentials (visible only when checkbox is checked)
        _usernameLabel = Controls.Markup()
            .AddLine($"[{ColorScheme.SecondaryMarkup}]Username:[/]")
            .WithMargin(2, 0, 2, 0)
            .Build();

        _usernameInput = Controls.Prompt("Username: ")
            .WithInput(_existingSource?.Username ?? "")
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithMargin(2, 0, 2, 1)
            .Build();

        var passwordLabelText = _editMode
            ? $"[{ColorScheme.SecondaryMarkup}]New Password (leave blank to keep existing):[/]"
            : $"[{ColorScheme.SecondaryMarkup}]Password:[/]";

        _passwordLabel = Controls.Markup()
            .AddLine(passwordLabelText)
            .WithMargin(2, 0, 2, 0)
            .Build();

        _passwordInput = Controls.Prompt("Password: ")
            .WithInput("")
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithMargin(2, 0, 2, 1)
            .Build();
        _passwordInput.MaskCharacter = '*';

        // Initialise visibility based on checkbox state
        _usernameLabel.Visible = initialAuth;
        _usernameInput.Visible = initialAuth;
        _passwordLabel.Visible = initialAuth;
        _passwordInput.Visible = initialAuth;

        // Keep credential section and dialog height in sync with checkbox
        _requiresAuthCheckbox.CheckedChanged += (_, isChecked) =>
        {
            if (_usernameLabel != null) _usernameLabel.Visible = isChecked;
            if (_usernameInput != null) _usernameInput.Visible = isChecked;
            if (_passwordLabel != null) _passwordLabel.Visible = isChecked;
            if (_passwordInput != null) _passwordInput.Visible = isChecked;

            int newHeight = isChecked ? HeightWithAuth : HeightWithoutAuth;
            Modal.SetSize(ModalWidth, newHeight);

            var desktop = WindowSystem.DesktopDimensions;
            Modal.Left = Math.Max(0, (desktop.Width  - ModalWidth) / 2);
            Modal.Top  = Math.Max(0, (desktop.Height - newHeight)  / 2);
        };

        // HTTP warning (shown on submit, non-blocking)
        _warningLabel = Controls.Markup()
            .WithMargin(2, 0, 2, 0)
            .Build();
        _warningLabel.Visible = false;

        // Separator + buttons
        var separator = Controls.RuleBuilder()
            .WithColor(ColorScheme.RuleColor)
            .StickyBottom()
            .Build();
        separator.Margin = new Margin(2, 0, 2, 0);

        var okButton = Controls.Button(_editMode
                ? $"[{ColorScheme.PrimaryMarkup}]Save (Enter)[/]"
                : $"[{ColorScheme.PrimaryMarkup}]Add (Enter)[/]")
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

        okButton.Click     += (_, _) => TrySubmit();
        cancelButton.Click += (_, _) => CloseWithResult(null);

        var buttonGrid = Controls.HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .Column(col => col.Add(okButton))
            .Column(col => col.Width(2))
            .Column(col => col.Add(cancelButton))
            .Build();
        buttonGrid.Margin = new Margin(0, 0, 0, 0);

        // Assemble
        Modal.AddControl(header);
        Modal.AddControl(nameLabel);
        Modal.AddControl(_nameInput);
        Modal.AddControl(urlLabel);
        Modal.AddControl(_urlInput);
        Modal.AddControl(_requiresAuthCheckbox);
        Modal.AddControl(_usernameLabel);
        Modal.AddControl(_usernameInput);
        Modal.AddControl(_passwordLabel);
        Modal.AddControl(_passwordInput);
        Modal.AddControl(_warningLabel);
        Modal.AddControl(separator);
        Modal.AddControl(buttonGrid);
    }

    protected override void SetInitialFocus()
    {
        if (_editMode)
            _urlInput?.SetFocus(true, FocusReason.Programmatic);
        else
            _nameInput?.SetFocus(true, FocusReason.Programmatic);
    }

    private void TrySubmit()
    {
        var name = _nameInput?.Input?.Trim() ?? "";
        var url  = _urlInput?.Input?.Trim()  ?? "";

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url))
        {
            WindowSystem.NotificationStateService.ShowNotification(
                "Validation Error", "Both name and URL are required.",
                NotificationSeverity.Warning, timeout: 3000, parentWindow: Modal);
            return;
        }

        var requiresAuth = _requiresAuthCheckbox?.Checked ?? false;

        // No credentials path
        if (!requiresAuth)
        {
            CloseWithResult(new CustomNuGetSource
            {
                Name              = name,
                Url               = url,
                IsEnabled         = _editMode ? (_existingSource?.IsEnabled ?? true) : true,
                RequiresAuth      = false,
                Username          = null,
                ClearTextPassword = null
            });
            return;
        }

        // Credential validation
        var username = _usernameInput?.Input?.Trim() ?? "";
        var password = _passwordInput?.Input ?? "";

        // In edit mode keep existing username when left blank
        var effectiveUsername = _editMode && string.IsNullOrEmpty(username)
            ? _existingSource?.Username
            : (string.IsNullOrEmpty(username) ? null : username);

        var effectivePassword = string.IsNullOrEmpty(password) ? null : password;

        if (string.IsNullOrEmpty(effectiveUsername))
        {
            WindowSystem.NotificationStateService.ShowNotification(
                "Validation Error", "Username is required when authentication is enabled.",
                NotificationSeverity.Warning, timeout: 3000, parentWindow: Modal);
            return;
        }

        // In add mode both fields are required (edit mode allows blank password = keep existing)
        if (!_editMode && effectivePassword == null)
        {
            WindowSystem.NotificationStateService.ShowNotification(
                "Validation Error", "Password is required when adding an authenticated source.",
                NotificationSeverity.Warning, timeout: 3000, parentWindow: Modal);
            return;
        }

        // HTTP warning (non-blocking, shown inline)
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && _warningLabel != null)
        {
            _warningLabel.SetContent(new List<string>
                { "[yellow]⚠ Credentials over http:// will be transmitted unencrypted.[/]" });
            _warningLabel.Visible = true;
        }

        CloseWithResult(new CustomNuGetSource
        {
            Name              = name,
            Url               = url,
            IsEnabled         = _editMode ? (_existingSource?.IsEnabled ?? true) : true,
            RequiresAuth      = true,
            Username          = effectiveUsername,
            ClearTextPassword = effectivePassword   // null in edit mode = "keep existing"
        });
    }
}
