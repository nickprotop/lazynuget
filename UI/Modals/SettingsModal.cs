using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;
using SharpConsoleUI.Drawing;
using SharpConsoleUI.Layout;
using Spectre.Console;
using LazyNuGet.Models;
using LazyNuGet.Services;
using LazyNuGet.UI.Utilities;
using HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment;
using VerticalAlignment = SharpConsoleUI.Layout.VerticalAlignment;

namespace LazyNuGet.UI.Modals;

/// <summary>
/// Settings modal with Sources and General sections
/// </summary>
public class SettingsModal : ModalBase<bool>
{
    private enum Section
    {
        Sources,
        General
    }

    // Dependencies
    private readonly ConfigurationService _configService;
    private readonly NuGetConfigService _nugetConfigService;
    private readonly NuGetClientService _nugetClientService;
    private readonly string _projectDirectory;

    // State
    private Section _currentSection = Section.Sources;
    private bool _settingsChanged = false;

    // Controls
    private ListControl? _contentList;
    private ButtonControl? _sourcesTab;
    private ButtonControl? _generalTab;
    private ButtonControl? _addBtn;
    private ButtonControl? _toggleBtn;
    private ButtonControl? _deleteBtn;
    private ButtonControl? _clearBtn;

    // Event handlers for cleanup
    private EventHandler<ButtonControl>? _addClickHandler;
    private EventHandler<ButtonControl>? _toggleClickHandler;
    private EventHandler<ButtonControl>? _deleteClickHandler;
    private EventHandler<ButtonControl>? _clearClickHandler;
    private EventHandler<ButtonControl>? _closeClickHandler;
    private EventHandler<ButtonControl>? _sourcesTabClickHandler;
    private EventHandler<ButtonControl>? _generalTabClickHandler;

    private SettingsModal(
        ConfigurationService configService,
        NuGetConfigService nugetConfigService,
        NuGetClientService nugetClientService,
        string projectDirectory)
    {
        _configService = configService;
        _nugetConfigService = nugetConfigService;
        _nugetClientService = nugetClientService;
        _projectDirectory = projectDirectory;
    }

    public static Task<bool> ShowAsync(
        ConsoleWindowSystem windowSystem,
        ConfigurationService configService,
        NuGetConfigService nugetConfigService,
        NuGetClientService nugetClientService,
        string projectDirectory,
        Window? parentWindow = null)
    {
        var instance = new SettingsModal(configService, nugetConfigService, nugetClientService, projectDirectory);
        return ((ModalBase<bool>)instance).ShowAsync(windowSystem, parentWindow);
    }

    protected override string GetTitle() => "Settings";
    protected override (int width, int height) GetSize() => (100, 30);
    protected override BorderStyle GetBorderStyle() => BorderStyle.DoubleLine;
    protected override Color GetBorderColor() => ColorScheme.BorderColor;
    protected override bool GetDefaultResult() => _settingsChanged;

    // Update tab button styling to reflect active section
    private void UpdateTabs()
    {
        if (_sourcesTab != null)
        {
            var isActive = _currentSection == Section.Sources;
            _sourcesTab.Text = isActive
                ? $"[white bold]► Sources[/] [grey78](F1)[/]"
                : $"[grey78]Sources[/] [grey62](F1)[/]";
            _sourcesTab.BackgroundColor = isActive ? Color.DarkGreen : Color.Grey30;
            _sourcesTab.FocusedBackgroundColor = isActive ? Color.Green : Color.Grey50;
        }

        if (_generalTab != null)
        {
            var isActive = _currentSection == Section.General;
            _generalTab.Text = isActive
                ? $"[white bold]► General[/] [grey78](F2)[/]"
                : $"[grey78]General[/] [grey62](F2)[/]";
            _generalTab.BackgroundColor = isActive ? Color.DarkGreen : Color.Grey30;
            _generalTab.FocusedBackgroundColor = isActive ? Color.Green : Color.Grey50;
        }
    }

    // Show/hide action buttons based on current section
    private void UpdateActionButtons()
    {
        var isSources = _currentSection == Section.Sources;
        if (_addBtn != null) _addBtn.Visible = isSources;
        if (_toggleBtn != null) _toggleBtn.Visible = isSources;
        if (_deleteBtn != null) _deleteBtn.Visible = isSources;
        if (_clearBtn != null) _clearBtn.Visible = !isSources;
    }

    private void SwitchSection(Section section)
    {
        _currentSection = section;
        UpdateTabs();
        UpdateActionButtons();
        RefreshContent();
    }

    // Helper to refresh content based on current section
    private void RefreshContent()
    {
        _contentList?.ClearItems();

        if (_currentSection == Section.Sources)
        {
            RefreshSourcesSection();
        }
        else
        {
            RefreshGeneralSection();
        }
    }

    private void RefreshSourcesSection()
    {
        var settings = _configService.Load();

        // Show NuGet.config sources
        foreach (var source in _nugetClientService.Sources)
        {
            var enabledBadge = source.IsEnabled
                ? "[green]Enabled[/]"
                : "[grey50]Disabled[/]";
            var originLabel = source.Origin == NuGetSourceOrigin.NuGetConfig
                ? "[grey50]NuGet.config[/]"
                : $"[{ColorScheme.PrimaryMarkup}]Custom[/]";
            var authBadge = source.RequiresAuth ? " [yellow]Auth[/]" : "";

            var text = $"[cyan1]{Markup.Escape(source.Name)}[/]  {enabledBadge}  {originLabel}{authBadge}\n" +
                      $"[grey50]  {Markup.Escape(source.Url)}[/]";

            var item = new ListItem(text);
            item.Tag = source;
            _contentList?.AddItem(item);
        }

        // Show custom sources from settings that might not be in the client yet
        if (settings.CustomSources.Any(cs => !_nugetClientService.Sources.Any(s =>
            string.Equals(s.Name, cs.Name, StringComparison.OrdinalIgnoreCase))))
        {
            foreach (var custom in settings.CustomSources.Where(cs =>
                !_nugetClientService.Sources.Any(s =>
                    string.Equals(s.Name, cs.Name, StringComparison.OrdinalIgnoreCase))))
            {
                var enabledBadge = custom.IsEnabled ? "[green]Enabled[/]" : "[grey50]Disabled[/]";
                var text = $"[cyan1]{Markup.Escape(custom.Name)}[/]  {enabledBadge}  [{ColorScheme.PrimaryMarkup}]Custom[/]\n" +
                          $"[grey50]  {Markup.Escape(custom.Url)}[/]";
                var item = new ListItem(text);
                item.Tag = custom;
                _contentList?.AddItem(item);
            }
        }

        if (_contentList?.Items.Count == 0)
        {
            _contentList.AddItem(new ListItem($"[{ColorScheme.MutedMarkup}]No NuGet sources configured[/]"));
        }
    }

    private void RefreshGeneralSection()
    {
        var settings = _configService.Load();

        // App info
        _contentList?.AddItem(new ListItem($"[{ColorScheme.PrimaryMarkup} bold]Application Info[/]"));
        _contentList?.AddItem(new ListItem($"[grey70]  Version: 1.0.0[/]"));

        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LazyNuGet");
        _contentList?.AddItem(new ListItem($"[grey70]  Config: {Markup.Escape(configDir)}[/]"));
        _contentList?.AddItem(new ListItem(""));

        // NuGet.config file locations
        var configPaths = _nugetConfigService.GetConfigFilePaths(_projectDirectory);
        _contentList?.AddItem(new ListItem($"[{ColorScheme.PrimaryMarkup} bold]Detected NuGet.config Files[/]"));
        if (configPaths.Any())
        {
            foreach (var path in configPaths)
            {
                _contentList?.AddItem(new ListItem($"[grey70]  {Markup.Escape(path)}[/]"));
            }
        }
        else
        {
            _contentList?.AddItem(new ListItem($"[{ColorScheme.MutedMarkup}]  None found (using defaults)[/]"));
        }
        _contentList?.AddItem(new ListItem(""));

        // Recent folders
        _contentList?.AddItem(new ListItem($"[{ColorScheme.PrimaryMarkup} bold]Recent Folders[/] [{ColorScheme.MutedMarkup}]({settings.RecentFolders.Count})[/]"));
        if (settings.RecentFolders.Any())
        {
            foreach (var folder in settings.RecentFolders)
            {
                _contentList?.AddItem(new ListItem($"[grey70]  {Markup.Escape(folder)}[/]"));
            }
        }
        else
        {
            _contentList?.AddItem(new ListItem($"[{ColorScheme.MutedMarkup}]  No recent folders[/]"));
        }
    }

    protected override void BuildContent()
    {
        // Header
        var header = Controls.Markup()
            .AddLine($"[{ColorScheme.PrimaryMarkup} bold]Settings[/]")
            .AddLine($"[{ColorScheme.SecondaryMarkup}]Configure NuGet sources and preferences[/]")
            .WithMargin(2, 2, 2, 0)
            .Build();

        // ── Tab buttons ──────────────────────────────────────
        _sourcesTabClickHandler = (s, e) => SwitchSection(Section.Sources);
        _sourcesTab = Controls.Button($"[white bold]► Sources[/] [grey78](F1)[/]")
            .OnClick(_sourcesTabClickHandler)
            .WithMargin(1, 0, 0, 0)
            .WithBackgroundColor(Color.DarkGreen)
            .WithForegroundColor(Color.White)
            .WithFocusedBackgroundColor(Color.Green)
            .WithFocusedForegroundColor(Color.White)
            .Build();

        _generalTabClickHandler = (s, e) => SwitchSection(Section.General);
        _generalTab = Controls.Button($"[grey78]General[/] [grey62](F2)[/]")
            .OnClick(_generalTabClickHandler)
            .WithMargin(1, 0, 0, 0)
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .Build();

        var tabBarTop = Controls.Markup()
            .AddEmptyLine()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithBackgroundColor(Color.Grey15)
            .WithMargin(2, 1, 2, 0)
            .Build();

        var tabBar = Controls.Toolbar()
            .AddButton(_sourcesTab)
            .AddButton(_generalTab)
            .WithSpacing(2)
            .WithBackgroundColor(Color.Grey15)
            .WithMargin(2, 0, 2, 0)
            .Build();

        var tabBarBottom = Controls.Markup()
            .AddEmptyLine()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithBackgroundColor(Color.Grey15)
            .WithMargin(2, 0, 2, 0)
            .Build();

        // Separator between tabs and actions
        var separator1 = Controls.RuleBuilder()
            .WithColor(ColorScheme.RuleColor)
            .Build();
        separator1.Margin = new Margin(2, 0, 2, 0);

        // ── Action toolbar ───────────────────────────────────
        _addBtn = Controls.Button($"[cyan1]Add Source[/] [grey78](A)[/]")
            .WithMargin(1, 0, 0, 0)
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .Build();

        _toggleBtn = Controls.Button($"[cyan1]Toggle[/] [grey78](E)[/]")
            .WithMargin(1, 0, 0, 0)
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .Build();

        _deleteBtn = Controls.Button($"[cyan1]Delete[/] [grey78](D)[/]")
            .WithMargin(1, 0, 0, 0)
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .Build();

        _clearBtn = Controls.Button($"[cyan1]Clear Recent[/] [grey78](C)[/]")
            .WithMargin(1, 0, 0, 0)
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .Build();

        var actionBarTop = Controls.Markup()
            .AddEmptyLine()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithBackgroundColor(Color.Grey15)
            .WithMargin(2, 0, 2, 0)
            .Build();

        var actionToolbar = Controls.Toolbar()
            .AddButton(_addBtn)
            .AddButton(_toggleBtn)
            .AddButton(_deleteBtn)
            .AddButton(_clearBtn)
            .WithSpacing(2)
            .WithBackgroundColor(Color.Grey15)
            .WithMargin(2, 0, 2, 0)
            .Build();

        var actionBarBottom = Controls.Markup()
            .AddEmptyLine()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithBackgroundColor(Color.Grey15)
            .WithMargin(2, 0, 2, 0)
            .Build();

        // Content list
        _contentList = Controls.List()
            .SimpleMode()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithColors(ColorScheme.StatusBarBackground, Color.Grey93)
            .WithFocusedColors(ColorScheme.StatusBarBackground, Color.Grey93)
            .WithHighlightColors(Color.Grey35, Color.White)
            .WithMargin(2, 1, 2, 1)
            .Build();

        // Bottom separator
        var separator2 = Controls.RuleBuilder()
            .WithColor(ColorScheme.RuleColor)
            .StickyBottom()
            .Build();
        separator2.Margin = new Margin(2, 0, 2, 0);

        // Close button
        _closeClickHandler = (s, e) => CloseWithResult(_settingsChanged);
        var closeButton = Controls.Button("[grey93]Close (Esc)[/]")
            .WithMargin(1, 0, 0, 0)
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .Build();

        closeButton.Click += _closeClickHandler;

        var buttonGrid = Controls.HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .Column(col => col.Add(closeButton))
            .Build();
        buttonGrid.Margin = new Margin(0, 0, 0, 0);

        // ── Assemble modal ───────────────────────────────────
        Modal.AddControl(header);
        Modal.AddControl(tabBarTop);
        Modal.AddControl(tabBar);
        Modal.AddControl(tabBarBottom);
        Modal.AddControl(separator1);
        Modal.AddControl(actionBarTop);
        Modal.AddControl(actionToolbar);
        Modal.AddControl(actionBarBottom);
        Modal.AddControl(_contentList);
        Modal.AddControl(separator2);
        Modal.AddControl(buttonGrid);

        // ── Action handlers ──────────────────────────────────
        _addClickHandler = (s, e) => _ = HandleAddSource();
        _toggleClickHandler = (s, e) => HandleToggleSource();
        _deleteClickHandler = (s, e) => _ = HandleDeleteSource();
        _clearClickHandler = (s, e) => _ = HandleClearRecent();

        _addBtn.Click += _addClickHandler;
        _toggleBtn.Click += _toggleClickHandler;
        _deleteBtn.Click += _deleteClickHandler;
        _clearBtn.Click += _clearClickHandler;

        // Initial setup
        UpdateActionButtons();
        RefreshContent();
    }

    // Action handlers
    private async Task HandleAddSource()
    {
        if (WindowSystem == null || Modal == null) return;

        var newSource = await AddSourceModal.ShowAsync(WindowSystem, Modal);
        if (newSource != null)
        {
            var settings = _configService.Load();
            settings.CustomSources.Add(newSource);
            _configService.Save(settings);
            _settingsChanged = true;
            RefreshContent();
        }
    }

    private void HandleToggleSource()
    {
        var selected = _contentList?.SelectedItem?.Tag;
        if (selected is NuGetSource source)
        {
            var settings = _configService.Load();
            var newEnabled = !source.IsEnabled;
            settings.SourceOverrides[source.Name] = newEnabled;
            _configService.Save(settings);
            source.IsEnabled = newEnabled;
            _settingsChanged = true;
            RefreshContent();
        }
        else if (selected is CustomNuGetSource custom)
        {
            var settings = _configService.Load();
            var existing = settings.CustomSources.FirstOrDefault(cs =>
                string.Equals(cs.Name, custom.Name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.IsEnabled = !existing.IsEnabled;
                _configService.Save(settings);
                _settingsChanged = true;
                RefreshContent();
            }
        }
    }

    private async Task HandleDeleteSource()
    {
        if (WindowSystem == null || Modal == null) return;

        var selected = _contentList?.SelectedItem?.Tag;

        if (selected is NuGetSource source && source.Origin == NuGetSourceOrigin.NuGetConfig)
        {
            WindowSystem.NotificationStateService.ShowNotification(
                "Cannot Remove",
                "NuGet.config sources cannot be removed from LazyNuGet. Edit NuGet.config directly.",
                NotificationSeverity.Warning,
                timeout: 4000,
                parentWindow: Modal);
            return;
        }

        string? nameToDelete = null;
        if (selected is NuGetSource lazySource && lazySource.Origin == NuGetSourceOrigin.LazyNuGetSettings)
        {
            nameToDelete = lazySource.Name;
        }
        else if (selected is CustomNuGetSource custom)
        {
            nameToDelete = custom.Name;
        }

        if (nameToDelete == null) return;

        var confirm = await ConfirmationModal.ShowAsync(WindowSystem,
            "Remove Source",
            $"Remove custom source '{nameToDelete}'?",
            "Remove", "Cancel", Modal);

        if (confirm)
        {
            var settings = _configService.Load();
            settings.CustomSources.RemoveAll(cs =>
                string.Equals(cs.Name, nameToDelete, StringComparison.OrdinalIgnoreCase));
            settings.SourceOverrides.Remove(nameToDelete);
            _configService.Save(settings);
            _settingsChanged = true;
            RefreshContent();
        }
    }

    private async Task HandleClearRecent()
    {
        if (WindowSystem == null || Modal == null) return;

        var confirm = await ConfirmationModal.ShowAsync(WindowSystem,
            "Clear Recent Folders",
            "Clear all recent folder history?",
            "Clear", "Cancel", Modal);

        if (confirm)
        {
            var settings = _configService.Load();
            settings.RecentFolders.Clear();
            _configService.Save(settings);
            RefreshContent();
        }
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.F1)
        {
            SwitchSection(Section.Sources);
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.F2)
        {
            SwitchSection(Section.General);
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.A && _currentSection == Section.Sources)
        {
            _ = HandleAddSource();
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.E && _currentSection == Section.Sources)
        {
            HandleToggleSource();
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.D && _currentSection == Section.Sources)
        {
            _ = HandleDeleteSource();
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.C && _currentSection == Section.General)
        {
            _ = HandleClearRecent();
            e.Handled = true;
        }
        else
        {
            base.OnKeyPressed(sender, e); // Handle Escape
        }
    }

    protected override void OnCleanup()
    {
        // Unsubscribe event handlers
        if (_addBtn != null && _addClickHandler != null)
            _addBtn.Click -= _addClickHandler;

        if (_toggleBtn != null && _toggleClickHandler != null)
            _toggleBtn.Click -= _toggleClickHandler;

        if (_deleteBtn != null && _deleteClickHandler != null)
            _deleteBtn.Click -= _deleteClickHandler;

        if (_clearBtn != null && _clearClickHandler != null)
            _clearBtn.Click -= _clearClickHandler;

        if (_sourcesTab != null && _sourcesTabClickHandler != null)
            _sourcesTab.Click -= _sourcesTabClickHandler;

        if (_generalTab != null && _generalTabClickHandler != null)
            _generalTab.Click -= _generalTabClickHandler;
    }
}
