using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drawing;
using SharpConsoleUI.Layout;
using Spectre.Console;
using LazyNuGet.Services;
using LazyNuGet.UI.Utilities;
using HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment;

namespace LazyNuGet.UI.Modals;

/// <summary>
/// First-run welcome modal with quick start tips and "Don't show again" toggle
/// </summary>
public class WelcomeModal : ModalBase<bool>
{
    private readonly ConfigurationService _configService;
    private bool _dontShowAgain;

    private ButtonControl? _dontShowButton;
    private ButtonControl? _getStartedButton;
    private EventHandler<ButtonControl>? _dontShowClickHandler;
    private EventHandler<ButtonControl>? _getStartedClickHandler;

    private WelcomeModal(ConfigurationService configService)
    {
        _configService = configService;
    }

    /// <summary>
    /// Show the welcome modal if ShowWelcomeOnStartup is enabled.
    /// Returns true if the modal was shown.
    /// </summary>
    public static async Task<bool> ShowIfEnabledAsync(
        ConsoleWindowSystem windowSystem,
        ConfigurationService configService,
        Window? parentWindow = null)
    {
        var settings = configService.Load();
        if (!settings.ShowWelcomeOnStartup)
            return false;

        var modal = new WelcomeModal(configService);
        await ((ModalBase<bool>)modal).ShowAsync(windowSystem, parentWindow);
        return true;
    }

    protected override string GetTitle() => "Welcome to LazyNuGet";
    protected override (int width, int height) GetSize() => (62, 24);
    protected override BorderStyle GetBorderStyle() => BorderStyle.DoubleLine;
    protected override Color GetBorderColor() => ColorScheme.InfoColor;
    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        // Title
        Modal.AddControl(Controls.Markup()
            .AddLine($"[{ColorScheme.PrimaryMarkup}]Welcome to LazyNuGet[/]")
            .AddLine($"[{ColorScheme.SecondaryMarkup}]A terminal UI for managing NuGet packages[/]")
            .WithAlignment(HorizontalAlignment.Center)
            .WithMargin(2, 2, 2, 0)
            .Build());

        // Separator
        var sep1 = Controls.RuleBuilder()
            .WithColor(ColorScheme.RuleColor)
            .Build();
        sep1.Margin = new Margin(2, 0, 2, 0);
        Modal.AddControl(sep1);

        // Quick start tips
        Modal.AddControl(Controls.Markup()
            .AddLine($"[{ColorScheme.PrimaryMarkup}]Quick Start[/]")
            .AddLine("")
            .AddLine($"[{ColorScheme.SecondaryMarkup}]  Ctrl+O[/]  [{ColorScheme.MutedMarkup}]Open a project folder[/]")
            .AddLine($"[{ColorScheme.SecondaryMarkup}]  Ctrl+S[/]  [{ColorScheme.MutedMarkup}]Search for packages[/]")
            .AddLine($"[{ColorScheme.SecondaryMarkup}]  Ctrl+R[/]  [{ColorScheme.MutedMarkup}]Reload projects[/]")
            .AddLine($"[{ColorScheme.SecondaryMarkup}]  Ctrl+P[/]  [{ColorScheme.MutedMarkup}]Open settings[/]")
            .AddLine($"[{ColorScheme.SecondaryMarkup}]  Ctrl+D[/]  [{ColorScheme.MutedMarkup}]View dependency tree[/]")
            .AddLine($"[{ColorScheme.SecondaryMarkup}]  Enter [/]  [{ColorScheme.MutedMarkup}]Select / drill into project[/]")
            .AddLine($"[{ColorScheme.SecondaryMarkup}]  Esc   [/]  [{ColorScheme.MutedMarkup}]Go back / exit[/]")
            .WithMargin(2, 1, 2, 0)
            .Build());

        // Bottom separator
        var sep2 = Controls.RuleBuilder()
            .WithColor(ColorScheme.RuleColor)
            .StickyBottom()
            .Build();
        sep2.Margin = new Margin(2, 0, 2, 0);
        Modal.AddControl(sep2);

        // Don't show again toggle button
        _dontShowClickHandler = (s, e) => ToggleDontShowAgain();
        _dontShowButton = Controls.Button($"[grey70][ ] Don't show this again[/]")
            .WithMargin(1, 0, 0, 0)
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .StickyBottom()
            .Build();
        _dontShowButton.Click += _dontShowClickHandler;

        // Get Started button
        _getStartedClickHandler = (s, e) => CloseWithResult(true);
        _getStartedButton = Controls.Button("[white]  Get Started  [/]")
            .WithMargin(1, 0, 0, 0)
            .WithBackgroundColor(Color.DarkGreen)
            .WithForegroundColor(Color.White)
            .WithFocusedBackgroundColor(Color.Green)
            .WithFocusedForegroundColor(Color.White)
            .StickyBottom()
            .Build();
        _getStartedButton.Click += _getStartedClickHandler;

        var buttonGrid = Controls.HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .Column(col => col.Add(_dontShowButton))
            .Column(col => col.Width(2))
            .Column(col => col.Add(_getStartedButton))
            .Build();
        Modal.AddControl(buttonGrid);

        // Bottom hint
        Modal.AddControl(Controls.Markup()
            .AddLine($"[{ColorScheme.MutedMarkup}]Enter/G:Get Started  Esc:Close[/]")
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .Build());
    }

    private void ToggleDontShowAgain()
    {
        _dontShowAgain = !_dontShowAgain;

        if (_dontShowButton != null)
        {
            var checkmark = _dontShowAgain ? "X" : " ";
            _dontShowButton.Text = $"[grey70][{checkmark}] Don't show this again[/]";
        }
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Enter || e.KeyInfo.Key == ConsoleKey.G)
        {
            CloseWithResult(true);
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.Spacebar)
        {
            ToggleDontShowAgain();
            e.Handled = true;
        }
        else
        {
            base.OnKeyPressed(sender, e);
        }
    }

    protected override void OnEscapePressed()
    {
        SaveSettings();
        CloseWithResult(false);
    }

    protected override void OnCleanup()
    {
        SaveSettings();

        if (_dontShowButton != null && _dontShowClickHandler != null)
            _dontShowButton.Click -= _dontShowClickHandler;
        if (_getStartedButton != null && _getStartedClickHandler != null)
            _getStartedButton.Click -= _getStartedClickHandler;
    }

    private void SaveSettings()
    {
        if (_dontShowAgain)
        {
            var settings = _configService.Load();
            settings.ShowWelcomeOnStartup = false;
            _configService.Save(settings);
        }
    }
}
