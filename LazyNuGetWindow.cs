using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drawing;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Core;
using SharpConsoleUI.Dialogs;
using SharpConsoleUI.Events;
using Spectre.Console;
using LazyNuGet.Models;
using LazyNuGet.UI;
using LazyNuGet.UI.Utilities;
using LazyNuGet.Services;
using LazyNuGet.UI.Components;
using LazyNuGet.UI.Modals;
using LazyNuGet.Orchestrators;
using HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment;
using VerticalAlignment = SharpConsoleUI.Layout.VerticalAlignment;

namespace LazyNuGet;

/// <summary>
/// Main LazyNuGet window with 2-panel layout and context-switching navigation
/// </summary>
public class LazyNuGetWindow : IDisposable
{
    private readonly ConsoleWindowSystem _windowSystem;
    private Window? _window;
    private LogViewerWindow? _logViewer;
    private volatile bool _disposed = false;

    // Named controls
    private MarkupControl? _topStatusLeft;
    private MarkupControl? _topStatusRight;
    private MarkupControl? _bottomHelpBar;
    private MarkupControl? _leftPanelHeader;
    private MarkupControl? _rightPanelHeader;
    private ListControl? _contextList;
    private ScrollablePanelControl? _detailsPanel;
    private MarkupControl? _detailsContent;  // Content inside details panel
    private MarkupControl? _filterDisplay;  // Display filter text for installed packages

    // State
    private string _currentFolderPath;
    private List<ProjectInfo> _projects = new();
    private bool _initialLoadComplete = false;  // Track first load for welcome modal


    // Services
    private ProjectDiscoveryService? _discoveryService;
    private ProjectParserService? _parserService;
    private NuGetCacheService? _nugetService;
    private DotNetCliService? _cliService;
    private ConfigurationService? _configService;
    private OperationHistoryService? _historyService;
    private NuGetConfigService? _nugetConfigService;
    private ErrorHandler? _errorHandler;

    // Orchestrators
    private SearchCoordinator? _searchCoordinator;
    private OperationOrchestrator? _operationOrchestrator;
    private StatusBarManager? _statusBarManager;
    private ModalManager? _modalManager;
    private PackageDetailsController? _packageDetailsController;
    private PackageFilterController? _filterController;
    private PackageCheckController? _packageCheckController;
    private NavigationController? _navigationController;

    public LazyNuGetWindow(ConsoleWindowSystem windowSystem, string folderPath, ConfigurationService? configService = null)
    {
        _windowSystem = windowSystem ?? throw new ArgumentNullException(nameof(windowSystem));
        _currentFolderPath = folderPath;
        _configService = configService;

        // Initialize services
        _discoveryService = new ProjectDiscoveryService();
        _parserService = new ProjectParserService(_windowSystem.LogService);
        _nugetConfigService = new NuGetConfigService(_windowSystem.LogService);
        _cliService = new DotNetCliService(_windowSystem.LogService);
        _errorHandler = new ErrorHandler(_windowSystem, _windowSystem.LogService);

        // Resolve NuGet sources from config hierarchy and create client
        _nugetService = CreateNuGetCacheService();

        // Initialize operation history service
        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LazyNuGet");
        Directory.CreateDirectory(configDir);
        _historyService = new OperationHistoryService(configDir);

        BuildUI();
        InitializeOrchestrators();
        SetupEventHandlers();

        // Load projects asynchronously
        AsyncHelper.FireAndForget(
            () => LoadProjectsAsync(),
            ex => _errorHandler?.HandleAsync(ex, ErrorSeverity.Warning, "Load Error", "Failed to load projects.", "Init", _window));
    }

    private NuGetCacheService CreateNuGetCacheService()
    {
        var sources = _nugetConfigService?.GetEffectiveSources(_currentFolderPath) ?? new List<NuGetSource>();

        // Merge custom sources from LazyNuGet settings
        var settings = _configService?.Load();
        if (settings?.CustomSources != null)
        {
            foreach (var custom in settings.CustomSources)
            {
                // Check for override (enable/disable)
                var isEnabled = custom.IsEnabled;
                if (settings.SourceOverrides.TryGetValue(custom.Name, out var overrideEnabled))
                    isEnabled = overrideEnabled;

                sources.Add(new NuGetSource
                {
                    Name = custom.Name,
                    Url = custom.Url,
                    IsEnabled = isEnabled,
                    Origin = NuGetSourceOrigin.LazyNuGetSettings
                });
            }
        }

        // Apply source overrides from settings to NuGet.config sources
        if (settings?.SourceOverrides != null)
        {
            foreach (var source in sources)
            {
                if (settings.SourceOverrides.TryGetValue(source.Name, out var enabled))
                    source.IsEnabled = enabled;
            }
        }

        return new NuGetCacheService(_windowSystem.LogService, sources.Count > 0 ? sources : null);
    }

    public void Show()
    {
        if (_window != null)
        {
            _windowSystem.AddWindow(_window);
            // Set initial focus to the left panel list
            _contextList?.SetFocus(true, FocusReason.Programmatic);
        }
    }

    private void BuildUI()
    {
        _window = new WindowBuilder(_windowSystem)
            .WithTitle("LazyNuGet")
            .WithColors(Color.Grey93, ColorScheme.WindowBackground)
            .AtPosition(0, 0)
            .WithSize(80, 24)
            .WithAsyncWindowThread(RefreshThreadAsync)
            .Borderless()
            .Resizable(false)
            .Movable(false)
            .Closable(false)
            .Minimizable(false)
            .Maximizable(false)
            .Maximized()
            .Build();

        // Top status bar
        var initFolderName = Path.GetFileName(_currentFolderPath.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrEmpty(initFolderName)) initFolderName = _currentFolderPath;
        _topStatusLeft = Controls.Markup($"[cyan1]{Markup.Escape(initFolderName)}[/] [grey50]({Markup.Escape(_currentFolderPath)})[/]")
            .WithAlignment(HorizontalAlignment.Left)
            .WithMargin(1, 0, 0, 0)
            .Build();

        _topStatusRight = Controls.Markup("[grey70]--:--:--[/]")
            .WithAlignment(HorizontalAlignment.Right)
            .WithMargin(0, 0, 1, 0)
            .Build();

        var topStatusBar = Controls.HorizontalGrid()
            .StickyTop()
            .WithAlignment(HorizontalAlignment.Stretch)
            .Column(col => col.Add(_topStatusLeft))
            .Column(col => col.Add(_topStatusRight))
            .Build();
        topStatusBar.BackgroundColor = ColorScheme.StatusBarBackground;
        topStatusBar.ForegroundColor = Color.Grey93;

        _window.AddControl(topStatusBar);

        _window.AddControl(Controls.RuleBuilder()
            .StickyTop()
            .WithColor(ColorScheme.RuleColor)
            .Build());

        // Main grid - 2 panels
        _leftPanelHeader = Controls.Markup("[grey70]Projects[/]")
            .WithMargin(1, 0, 0, 0)
            .Build();

        // Make header clickable to go back when in Packages view (shows ← arrow)
        _leftPanelHeader.MouseClick += (s, e) =>
        {
            if (_navigationController?.CurrentViewState == ViewState.Packages)
            {
                _navigationController?.SwitchToProjectsView();
            }
        };

        _contextList = Controls.List()
            .WithTitle(string.Empty)
            .WithMargin(0, 1, 0, 0)
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithColors(Color.Grey93, ColorScheme.SidebarBackground)
            .WithFocusedColors(Color.Grey93, ColorScheme.SidebarBackground)
            .WithHighlightColors(Color.White, Color.Grey35)
            .SimpleMode()
            .Build();

        _rightPanelHeader = Controls.Markup("[grey70]Dashboard[/]")
            .WithMargin(1, 0, 0, 0)
            .Build();

        _detailsPanel = Controls.ScrollablePanel()
            .WithVerticalScroll(ScrollMode.Scroll)
            .WithScrollbar(true)
            .WithMouseWheel(true)
            .WithAutoScroll(false)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithColors(Color.Grey93, ColorScheme.DetailsPanelBackground)
            .Build();

        // Create initial empty details content
        _detailsContent = Controls.Markup()
            .AddLine("[grey50]Select a project to view details[/]")
            .WithMargin(1, 1, 1, 1)
            .Build();
        _detailsPanel.AddControl(_detailsContent);

        // Create filter display (initially hidden, will be shown in packages view)
        _filterDisplay = Controls.Markup($"[{ColorScheme.MutedMarkup}]Filter: [/][{ColorScheme.PrimaryMarkup}][/] [{ColorScheme.MutedMarkup}](Ctrl+F to filter, Esc to clear)[/]")
            .WithMargin(1, 1, 0, 0)
            .Build();
        _filterDisplay.Visible = false; // Hidden by default

        var mainGrid = Controls.HorizontalGrid()
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithAlignment(HorizontalAlignment.Stretch)
            .Column(col => col
                .Width(40)
                .Add(_leftPanelHeader)
                .Add(_filterDisplay)
                .Add(_contextList))
            .Column(col => col.Width(1)) // Spacing
            .Column(col => col
                .Add(_rightPanelHeader)
                .Add(_detailsPanel))
            .Build();

        // Set panel backgrounds
        if (mainGrid.Columns.Count > 0)
            mainGrid.Columns[0].BackgroundColor = ColorScheme.SidebarBackground;
        if (mainGrid.Columns.Count > 2)
            mainGrid.Columns[2].BackgroundColor = ColorScheme.DetailsPanelBackground;

        _window.AddControl(mainGrid);

        _window.AddControl(Controls.RuleBuilder()
            .StickyBottom()
            .WithColor(ColorScheme.RuleColor)
            .Build());

        // Bottom help bar (will be updated by StatusBarManager after initialization)
        _bottomHelpBar = Controls.Markup("[grey70]Loading...[/]")
            .WithAlignment(HorizontalAlignment.Left)
            .WithMargin(1, 0, 1, 0)
            .Build();

        var bottomBar = Controls.HorizontalGrid()
            .StickyBottom()
            .WithAlignment(HorizontalAlignment.Stretch)
            .Column(col => col.Add(_bottomHelpBar))
            .Build();
        bottomBar.BackgroundColor = ColorScheme.StatusBarBackground;
        bottomBar.ForegroundColor = ColorScheme.SecondaryText;

        _window.AddControl(bottomBar);
    }

    private void InitializeOrchestrators()
    {
        // Initialize orchestrators with required dependencies
        _searchCoordinator = new SearchCoordinator(
            _windowSystem,
            _nugetService!,
            _cliService!,
            _historyService!,
            _errorHandler!,
            _window);

        _operationOrchestrator = new OperationOrchestrator(
            _windowSystem,
            _cliService!,
            _nugetService!,
            _historyService!,
            _errorHandler!,
            _window);

        _statusBarManager = new StatusBarManager(
            _topStatusLeft,
            _topStatusRight,
            _bottomHelpBar,
            _leftPanelHeader,
            _rightPanelHeader,
            _detailsPanel,
            _currentFolderPath,
            _projects,
            onAction:    action => HandleHelpBarAction(action),
            isCacheWarm: () => _nugetService?.IsAnyCacheWarm ?? false,
            onRefresh:   TriggerRefresh);

        _modalManager = new ModalManager(
            _windowSystem,
            _configService,
            _nugetConfigService!,
            _cliService!,
            _nugetService!,
            _currentFolderPath,
            _window);

        _packageDetailsController = new PackageDetailsController(
            _nugetService!,
            _errorHandler,
            _window,
            _detailsPanel,
            controls => UpdateDetailsPanel(controls),
            () => _navigationController?.SelectedProject,
            pkg => HandleUpdatePackageAsync(pkg),
            pkg => HandleChangeVersionAsync(pkg),
            pkg => HandleRemovePackageAsync(pkg),
            (proj, pkg) => ShowDependencyTreeAsync(proj, pkg));

        _filterController = new PackageFilterController(
            _contextList,
            _filterDisplay,
            _leftPanelHeader,
            _bottomHelpBar,
            _statusBarManager,
            () => _navigationController?.CurrentViewState ?? ViewState.Projects,
            packages => _navigationController?.PopulatePackagesList(packages),
            lines => UpdateDetailsContent(lines),
            () => _navigationController?.HandleSelectionChanged());

        _packageCheckController = new PackageCheckController(
            _windowSystem,
            _nugetService!,
            _bottomHelpBar,
            _window,
            _statusBarManager,
            _errorHandler,
            () => _navigationController?.CurrentViewState ?? ViewState.Projects,
            () => _filterController?.IsFilterMode == true,
            () => _navigationController?.RefreshCurrentView(),
            () => _projects);

        _navigationController = new NavigationController(
            _contextList,
            _leftPanelHeader,
            _statusBarManager,
            _filterController,
            _packageDetailsController,
            _errorHandler,
            _window,
            () => _projects,
            lines => UpdateDetailsContent(lines),
            controls => UpdateDetailsPanel(controls),
            proj => HandleUpdateAllAsync(proj),
            proj => HandleRestoreAsync(proj),
            (proj, pkg) => ShowDependencyTreeAsync(proj, pkg),
            () => ConfirmExitAsync(),
            _operationOrchestrator);
    }

    private async Task RefreshThreadAsync(Window window, CancellationToken ct)
    {
        while (!window.GetIsActive() && !ct.IsCancellationRequested && !_disposed)
        {
            await Task.Delay(100, ct);
        }

        while (!ct.IsCancellationRequested && !_disposed)
        {
            try
            {
                // Update clock and context-aware stats via StatusBarManager
                _statusBarManager?.UpdateStatusRight(
                    _navigationController?.CurrentViewState ?? ViewState.Projects,
                    _navigationController?.SelectedProject);

                // Panel focus indicators update automatically via FocusStateService.StateChanged event

                await Task.Delay(1000, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                break;
            }
        }
    }

    private void SetupEventHandlers()
    {
        if (_window == null) return;

        // Subscribe to FocusStateService for automatic panel indicator updates
        _windowSystem.FocusStateService.StateChanged += (sender, e) =>
        {
            // Panel title focus indicators disabled
        };

        _window.KeyPressed += (sender, e) =>
        {
            // === CTRL+UP/DOWN FOR RIGHT PANEL SCROLLING ===
            // Must be checked before plain Up/Down (both match ConsoleKey.UpArrow)
            if (e.KeyInfo.Key == ConsoleKey.UpArrow && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                if (_detailsPanel != null && _detailsPanel.CanScrollUp)
                {
                    _detailsPanel.ScrollVerticalBy(-10);
                    e.Handled = true;
                }
                return;
            }
            if (e.KeyInfo.Key == ConsoleKey.DownArrow && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                if (_detailsPanel != null && _detailsPanel.CanScrollDown)
                {
                    _detailsPanel.ScrollVerticalBy(10);
                    e.Handled = true;
                }
                return;
            }

            // Up/Down arrows (no modifiers): Navigate list
            // If list is focused, let it handle arrows itself
            if (e.KeyInfo.Key == ConsoleKey.UpArrow && e.KeyInfo.Modifiers == 0 && _contextList != null && _contextList.Items.Count > 0)
            {
                var fc = _windowSystem.FocusStateService.FocusedControl;
                if (fc is ListControl)
                {
                    return; // Any focused ListControl handles its own arrows
                }

                // Nothing else focused - global navigation on left panel
                if (_contextList.SelectedIndex > 0)
                    _contextList.SelectedIndex--;
                e.Handled = true;
                return;
            }
            if (e.KeyInfo.Key == ConsoleKey.DownArrow && e.KeyInfo.Modifiers == 0 && _contextList != null && _contextList.Items.Count > 0)
            {
                var fc = _windowSystem.FocusStateService.FocusedControl;
                if (fc is ListControl)
                {
                    return; // Any focused ListControl handles its own arrows
                }

                // Nothing else focused - global navigation on left panel
                if (_contextList.SelectedIndex < _contextList.Items.Count - 1)
                    _contextList.SelectedIndex++;
                e.Handled = true;
                return;
            }

            // Note: Left/Right arrows and Tab are NOT intercepted - they work naturally
            // Tab cycles through all focusable elements (panels, buttons, controls)
            // Left/Right can be used by controls as needed

            // Escape must run BEFORE AlreadyHandled check — the dispatcher
            // consumes the first Escape to unfocus controls, setting AlreadyHandled.
            // We want Escape to always mean "navigate back / exit".
            if (e.KeyInfo.Key == ConsoleKey.Escape)
            {
                _navigationController?.HandleEscapeKey();
                e.Handled = true;
                return;
            }

            // Enter: Navigate forward ONLY if not already handled by a control (e.g., button)
            if (e.KeyInfo.Key == ConsoleKey.Enter)
            {
                if (!e.AlreadyHandled)
                {
                    _navigationController?.HandleEnterKey();
                }
                e.Handled = true;
                return;
            }

            // F1-F5: Switch package detail tabs (in packages view)
            if (_navigationController?.CurrentViewState == ViewState.Packages && (_packageDetailsController?.HandleDetailTabShortcut(e.KeyInfo.Key) ?? false))
            {
                e.Handled = true;
                return;
            }

            if (e.AlreadyHandled) { e.Handled = true; return; }

            // ? - Show help modal
            if (e.KeyInfo.KeyChar == '?')
            {
                AsyncHelper.FireAndForget(
                    () => HelpModal.ShowAsync(_windowSystem, _window),
                    ex => _errorHandler?.HandleAsync(ex, ErrorSeverity.Info, "Help Error", "Failed to show help.", "UI", _window));
                e.Handled = true;
                return;
            }

            // Ctrl+O - Open folder
            if (e.KeyInfo.Key == ConsoleKey.O && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                AsyncHelper.FireAndForget(
                    () => PromptForFolderAsync(),
                    ex => _errorHandler?.HandleAsync(ex, ErrorSeverity.Critical, "Folder Error", "Failed to open folder.", "UI", _window));
                e.Handled = true;
            }
            // Ctrl+S - Search packages
            else if (e.KeyInfo.Key == ConsoleKey.S && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                AsyncHelper.FireAndForget(
                    () => ShowSearchModalAsync(),
                    ex => _errorHandler?.HandleAsync(ex, ErrorSeverity.Warning, "Search Error", "Failed to show search.", "UI", _window));
                e.Handled = true;
            }
            // Ctrl+R - Reload (clear cache first for a fully fresh NuGet check)
            else if (e.KeyInfo.Key == ConsoleKey.R && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                TriggerRefresh();
                e.Handled = true;
            }
            // Ctrl+H - Operation history
            else if (e.KeyInfo.Key == ConsoleKey.H && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                AsyncHelper.FireAndForget(
                    () => ShowOperationHistoryAsync(),
                    ex => _errorHandler?.HandleAsync(ex, ErrorSeverity.Info, "History Error", "Failed to show history.", "UI", _window));
                e.Handled = true;
            }
            // Ctrl+U - Update package (in packages view) or update all (in projects view)
            else if (e.KeyInfo.Key == ConsoleKey.U && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                if (_navigationController?.CurrentViewState == ViewState.Packages && _contextList?.SelectedItem?.Tag is PackageReference pkg)
                {
                    AsyncHelper.FireAndForget(
                        () => HandleUpdatePackageAsync(pkg),
                        ex => _errorHandler?.HandleAsync(ex, ErrorSeverity.Warning, "Update Error", "Failed to update package.", "NuGet", _window));
                }
                else if (_navigationController?.CurrentViewState == ViewState.Projects && _contextList?.SelectedItem?.Tag is ProjectInfo proj)
                {
                    AsyncHelper.FireAndForget(
                        () => HandleUpdateAllAsync(proj),
                        ex => _errorHandler?.HandleAsync(ex, ErrorSeverity.Warning, "Update All Error", "Failed to update packages.", "NuGet", _window));
                }
                e.Handled = true;
            }
            // Ctrl+V - Change package version
            else if (e.KeyInfo.Key == ConsoleKey.V && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                if (_navigationController?.CurrentViewState == ViewState.Packages && _contextList?.SelectedItem?.Tag is PackageReference pkgToChange)
                {
                    AsyncHelper.FireAndForget(
                        () => HandleChangeVersionAsync(pkgToChange),
                        ex => _errorHandler?.HandleAsync(ex, ErrorSeverity.Warning, "Version Error", "Failed to change version.", "NuGet", _window));
                }
                e.Handled = true;
            }
            // Ctrl+X - Remove package
            else if (e.KeyInfo.Key == ConsoleKey.X && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                if (_navigationController?.CurrentViewState == ViewState.Packages && _contextList?.SelectedItem?.Tag is PackageReference pkgToRemove)
                {
                    AsyncHelper.FireAndForget(
                        () => HandleRemovePackageAsync(pkgToRemove),
                        ex => _errorHandler?.HandleAsync(ex, ErrorSeverity.Warning, "Remove Error", "Failed to remove package.", "NuGet", _window));
                }
                e.Handled = true;
            }
            // Ctrl+P - Settings
            else if (e.KeyInfo.Key == ConsoleKey.P && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                AsyncHelper.FireAndForget(
                    () => ShowSettingsAsync(),
                    ex => _errorHandler?.HandleAsync(ex, ErrorSeverity.Info, "Settings Error", "Failed to show settings.", "UI", _window));
                e.Handled = true;
            }
            // Ctrl+D - Dependency tree
            else if (e.KeyInfo.Key == ConsoleKey.D && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                ProjectInfo? targetProject = null;
                PackageReference? targetPackage = null;

                if (_navigationController?.CurrentViewState == ViewState.Packages && _navigationController?.SelectedProject != null)
                {
                    targetProject = _navigationController.SelectedProject;
                    targetPackage = _contextList?.SelectedItem?.Tag as PackageReference;
                }
                else if (_navigationController?.CurrentViewState == ViewState.Projects && _contextList?.SelectedItem?.Tag is ProjectInfo proj)
                {
                    targetProject = proj;
                }

                if (targetProject != null)
                {
                    AsyncHelper.FireAndForget(
                        () => ShowDependencyTreeAsync(targetProject, targetPackage),
                        ex => _errorHandler?.HandleAsync(ex, ErrorSeverity.Info, "Dependency Error", "Failed to show dependencies.", "UI", _window));
                }
                e.Handled = true;
            }
            // Ctrl+L - Show log viewer
            else if (e.KeyInfo.Key == ConsoleKey.L && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                ShowLogViewer();
                e.Handled = true;
            }
            // Ctrl+F - Enter filter mode (in packages view)
            else if (e.KeyInfo.Key == ConsoleKey.F && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                if (_navigationController?.CurrentViewState == ViewState.Packages)
                {
                    _filterController?.EnterFilterMode();
                }
                e.Handled = true;
            }
            // Handle typing in filter mode
            else if (_filterController?.IsFilterMode == true && e.KeyInfo.Key != ConsoleKey.Escape)
            {
                _filterController?.HandleFilterInput(e.KeyInfo);
                e.Handled = true;
            }
        };

        // Top-right status bar click → delegate to StatusBarManager (e.g. ^R:Refresh hint).
        // e.Position.X is control-relative (not screen-absolute), so pass ActualWidth
        // so StatusBar can compute the right-aligned content's left edge correctly.
        if (_topStatusRight != null)
        {
            _topStatusRight.MouseClick += (s, e) =>
            {
                _statusBarManager?.HandleStatusBarClick(e.Position.X, _topStatusRight.ActualWidth);
            };
        }

        // Help bar mouse click → delegate to StatusBarManager for hit-testing
        if (_bottomHelpBar != null)
        {
            _bottomHelpBar.MouseClick += (s, e) =>
            {
                _statusBarManager?.HandleHelpBarClick(e.Position.X);
            };
        }

        // List selection changed
        if (_contextList != null)
        {
            _contextList.SelectedIndexChanged += (s, e) =>
            {
                _navigationController?.HandleSelectionChanged();
            };

            // List item activated (Enter key in Simple mode)
            _contextList.ItemActivated += (s, item) =>
            {
                _navigationController?.HandleEnterKey();
            };
        }
    }

    private async Task PromptForFolderAsync()
    {
        try
        {
            string? selected = null;

            // Show recent folders modal if there are any
            var recentFolders = _configService?.Load().RecentFolders ?? new List<string>();
            if (recentFolders.Count > 0)
            {
                var result = await RecentFoldersModal.ShowAsync(
                    _windowSystem, recentFolders, _currentFolderPath, _window);

                if (result == null)
                    return; // User cancelled

                if (result == RecentFoldersModal.BrowseSentinel)
                    selected = await FileDialogs.ShowFolderPickerAsync(_windowSystem, _currentFolderPath, _window);
                else
                    selected = result;
            }
            else
            {
                // No recent folders — go straight to folder picker
                selected = await FileDialogs.ShowFolderPickerAsync(_windowSystem, _currentFolderPath, _window);
            }

            if (!string.IsNullOrEmpty(selected))
            {
                _currentFolderPath = selected;
                _configService?.TrackFolder(_currentFolderPath);
                await LoadProjectsAsync();
            }
        }
        catch (Exception ex)
        {
            AsyncHelper.FireAndForget(
                () => _errorHandler?.HandleAsync(ex, ErrorSeverity.Critical,
                    "Folder Error", "Failed to open folder.", "UI", _window) ?? Task.CompletedTask);
        }
    }

    private void TriggerRefresh()
    {
        _nugetService?.ClearAll();
        AsyncHelper.FireAndForget(
            () => LoadProjectsAsync(),
            ex => _errorHandler?.HandleAsync(ex, ErrorSeverity.Warning, "Reload Error", "Failed to reload projects.", "UI", _window));
    }

    private async Task LoadProjectsAsync()
    {
        if (_discoveryService == null || _parserService == null) return;

        try
        {
            // Remember current view context to restore after reload
            var previousView = _navigationController?.CurrentViewState ?? ViewState.Projects;
            var previousProjectPath = _navigationController?.SelectedProject?.FilePath;

            // Persist the current folder for next launch
            _configService?.TrackFolder(_currentFolderPath);

            // Re-resolve NuGet sources for the new project directory
            _nugetService?.Dispose();
            _nugetService = CreateNuGetCacheService();

            // Update orchestrators with new NuGet service
            _searchCoordinator = new SearchCoordinator(_windowSystem, _nugetService, _cliService!, _historyService!, _errorHandler!, _window);
            _operationOrchestrator = new OperationOrchestrator(_windowSystem, _cliService!, _nugetService, _historyService!, _errorHandler!, _window);
            _modalManager?.SetNuGetService(_nugetService);
            _packageDetailsController?.SetNuGetService(_nugetService);
            _packageCheckController?.SetNuGetService(_nugetService);
            _modalManager?.SetFolderPath(_currentFolderPath);
            _statusBarManager?.SetFolderPath(_currentFolderPath);

            // Show loading feedback with progress bar
            ShowLoadingPanel("Discovering projects...", $"Scanning {Markup.Escape(_currentFolderPath)}");

            // Discover projects
            var projectFiles = await _discoveryService.DiscoverProjectsAsync(_currentFolderPath);

            // Snapshot LatestVersion data before clearing (not stored in .csproj)
            var previousLatestVersions = _projects
                .ToDictionary(
                    p => p.FilePath,
                    p => p.Packages.ToDictionary(pkg => pkg.Id, pkg => pkg.LatestVersion));

            // Parse each project
            _projects.Clear();
            foreach (var projectFile in projectFiles)
            {
                var project = await _parserService.ParseProjectAsync(projectFile);
                if (project != null)
                {
                    // Restore LatestVersion from previous data
                    if (previousLatestVersions.TryGetValue(project.FilePath, out var versions))
                    {
                        foreach (var pkg in project.Packages)
                        {
                            if (versions.TryGetValue(pkg.Id, out var latestVersion))
                            {
                                pkg.LatestVersion = latestVersion;
                            }
                        }
                    }

                    _projects.Add(project);
                }
            }

            // Update StatusBarManager with new projects list
            _statusBarManager?.SetProjects(_projects);

            // Restore previous view if possible, otherwise go to Projects view
            if (previousView == ViewState.Packages && !string.IsNullOrEmpty(previousProjectPath))
            {
                var restoredProject = _projects.FirstOrDefault(p => p.FilePath == previousProjectPath);
                if (restoredProject != null)
                {
                    _navigationController?.SwitchToPackagesView(restoredProject);
                }
                else
                {
                    // Project no longer exists, go to Projects view
                    _navigationController?.SwitchToProjectsView();
                }
            }
            else
            {
                _navigationController?.SwitchToProjectsView();
            }

            // Check for outdated packages in the background
            AsyncHelper.FireAndForget(
                () => _packageCheckController?.CheckForOutdatedPackagesAsync() ?? Task.CompletedTask,
                ex => _errorHandler?.HandleAsync(ex, ErrorSeverity.Info, "Package Check Error", "Failed to check for outdated packages.", "NuGet", _window));

            // Show welcome modal on first load only
            if (!_initialLoadComplete)
            {
                _initialLoadComplete = true;
                if (_configService != null)
                {
                    AsyncHelper.FireAndForget(
                        () => WelcomeModal.ShowIfEnabledAsync(_windowSystem, _configService, _window),
                        ex => _errorHandler?.HandleAsync(ex, ErrorSeverity.Info, "Welcome Error", "Failed to show welcome.", "UI", _window));
                }
            }
        }
        catch (Exception ex)
        {
            AsyncHelper.FireAndForget(
                () => _errorHandler?.HandleAsync(ex, ErrorSeverity.Warning,
                    "Project Load Error", "Failed to load projects.", "Projects", _window) ?? Task.CompletedTask);
        }
    }

    private async Task ConfirmExitAsync()
    {
        var confirm = await (_modalManager?.ConfirmExitAsync() ?? Task.FromResult(false));
        if (confirm) _windowSystem.Shutdown();
    }

    // ──────────────────────────────────────────────────────────
    // Package Operations
    // ──────────────────────────────────────────────────────────

    private async Task ShowSearchModalAsync()
    {
        // Remember where search was initiated from
        var originView = _navigationController?.CurrentViewState ?? ViewState.Projects;
        var originProject = _navigationController?.SelectedProject;

        var selectedPackage = await _searchCoordinator?.ShowSearchModalAsync()!;
        if (selectedPackage == null) return;

        // Show install planning modal to select target projects
        // Pass current project context: if in Packages view, pre-select only that project; if in Projects view, select all
        var selectedProjects = await (_searchCoordinator?.ShowInstallPlanningModalAsync(selectedPackage, _projects, _navigationController?.SelectedProject)
            ?? Task.FromResult<List<ProjectInfo>?>(null));
        if (selectedProjects == null || selectedProjects.Count == 0) return;

        // Batch install to all selected projects
        var result = await (_searchCoordinator?.BatchInstallAsync(selectedPackage, selectedProjects)
            ?? Task.FromResult(new OperationResult { Success = false }));

        // Refresh projects after successful installation
        if (result.Success)
        {
            foreach (var project in selectedProjects)
            {
                await ReloadProjectAsync(project);
            }

            // Return to the view where search was initiated
            if (originView == ViewState.Packages && originProject != null)
            {
                // Find the refreshed project instance
                var refreshedProject = _projects.FirstOrDefault(p => p.FilePath == originProject.FilePath);
                if (refreshedProject != null)
                {
                    _navigationController?.SwitchToPackagesView(refreshedProject);
                }
                else
                {
                    _navigationController?.SwitchToProjectsView();
                }
            }
            else
            {
                _navigationController?.SwitchToProjectsView();
            }
        }
    }

    private async Task ShowSettingsAsync()
    {
        var changed = await (_modalManager?.ShowSettingsAsync() ?? Task.FromResult(false));

        if (changed)
        {
            // Reinitialize NuGet client with updated sources
            _nugetService?.Dispose();
            _nugetService = CreateNuGetCacheService();

            // Reload projects to refresh package status
            await LoadProjectsAsync();
        }
    }

    private async Task ShowDependencyTreeAsync(ProjectInfo project, PackageReference? selectedPackage = null)
    {
        await (_modalManager?.ShowDependencyTreeAsync(project, selectedPackage) ?? Task.CompletedTask);
    }

    private async Task ShowOperationHistoryAsync()
    {
        await (_operationOrchestrator?.ShowOperationHistoryAsync() ?? Task.CompletedTask);

        // Refresh current view after history modal closes (in case user retried operations)
        var selectedProject = _navigationController?.SelectedProject;
        if (selectedProject != null)
        {
            await ReloadProjectAsync(selectedProject);
        }
    }

    private async Task HandleUpdatePackageAsync(PackageReference package)
    {
        var selectedProject = _navigationController?.SelectedProject;
        if (selectedProject == null) return;

        var result = await (_operationOrchestrator?.HandleUpdatePackageAsync(package, selectedProject) ?? Task.FromResult(new OperationResult { Success = false }));

        // Refresh project after successful update
        if (result.Success)
        {
            await ReloadProjectAndRefreshView(selectedProject);
        }
    }

    private async Task HandleRemovePackageAsync(PackageReference package)
    {
        var selectedProject = _navigationController?.SelectedProject;
        if (selectedProject == null) return;

        var result = await (_operationOrchestrator?.HandleRemovePackageAsync(package, selectedProject) ?? Task.FromResult(new OperationResult { Success = false }));

        // Refresh project after successful removal
        if (result.Success)
        {
            await ReloadProjectAndRefreshView(selectedProject);
        }
    }

    private async Task HandleChangeVersionAsync(PackageReference package)
    {
        var selectedProject = _navigationController?.SelectedProject;
        if (selectedProject == null) return;

        var result = await (_operationOrchestrator?.HandleChangeVersionAsync(package, selectedProject) ?? Task.FromResult(new OperationResult { Success = false }));

        if (result.Success)
        {
            await ReloadProjectAndRefreshView(selectedProject);
        }
    }

    private async Task HandleUpdateAllAsync(ProjectInfo project)
    {
        var result = await (_operationOrchestrator?.HandleUpdateAllAsync(project) ?? Task.FromResult(new OperationResult { Success = false }));

        // Reload project after updates (even if partial success)
        if (result.Success || result.Message?.Contains("Updated") == true)
        {
            await ReloadProjectAsync(project);

            // Refresh the current view
            if (_navigationController?.CurrentViewState == ViewState.Projects)
            {
                _navigationController?.SwitchToProjectsView();
            }
            else if (_navigationController?.CurrentViewState == ViewState.Packages)
            {
                _navigationController?.SwitchToPackagesView(project);
            }
        }
    }

    private async Task HandleRestoreAsync(ProjectInfo project)
    {
        var result = await (_operationOrchestrator?.HandleRestoreAsync(project) ?? Task.FromResult(new OperationResult { Success = false }));

        // Refresh project after successful restore
        if (result.Success && _navigationController?.SelectedProject?.FilePath == project.FilePath)
        {
            await ReloadProjectAsync(project);
        }
    }

    private async Task ReloadProjectAsync(ProjectInfo project)
    {
        if (_parserService == null) return;

        var updated = await _parserService.ParseProjectAsync(project.FilePath);
        if (updated == null) return;

        // Preserve LatestVersion from old packages (not stored in .csproj)
        foreach (var newPkg in updated.Packages)
        {
            var oldPkg = project.Packages.FirstOrDefault(p => p.Id == newPkg.Id);
            if (oldPkg != null)
            {
                newPkg.LatestVersion = oldPkg.LatestVersion;
            }
        }

        // Replace in the projects list
        var index = _projects.FindIndex(p => p.FilePath == project.FilePath);
        if (index >= 0)
        {
            _projects[index] = updated;
        }
    }

    private async Task ReloadProjectAndRefreshView(ProjectInfo project)
    {
        await ReloadProjectAsync(project);

        // Get the refreshed project
        var refreshed = _projects.FirstOrDefault(p => p.FilePath == project.FilePath);
        if (refreshed != null && _navigationController?.CurrentViewState == ViewState.Packages)
        {
            // Already in packages view - just update data and refresh list
            if (_navigationController != null) _navigationController.SelectedProject = refreshed;
            _filterController?.SetPackages(new List<PackageReference>(refreshed.Packages));

            // Update headers/breadcrumb
            _statusBarManager?.UpdateHeadersForPackages(refreshed);
            _statusBarManager?.UpdateBreadcrumbForPackages(refreshed);

            // Refresh the package list
            _navigationController?.RefreshInstalledPackages();
        }
        else
        {
            _navigationController?.SwitchToProjectsView();
        }
    }

    // ──────────────────────────────────────────────────────────

    private void ShowLoadingPanel(string title, string? subtitle = null)
    {
        var controls = new List<IWindowControl>();

        var textBuilder = Controls.Markup()
            .AddLine($"[{ColorScheme.PrimaryMarkup}]{Markup.Escape(title)}[/]");
        if (subtitle != null)
            textBuilder.AddLine($"[grey70]{subtitle}[/]");
        controls.Add(textBuilder.WithMargin(1, 1, 1, 0).Build());

        var progress = Controls.ProgressBar()
            .Indeterminate(true)
            .WithMargin(1, 0, 1, 1)
            .Build();
        controls.Add(progress);

        UpdateDetailsPanel(controls);
    }

    private void UpdateDetailsContent(List<string> lines)
    {
        if (_detailsPanel == null) return;

        // Clear all previous content
        _detailsPanel.ClearContents();

        // Build and add new content
        var builder = Controls.Markup();
        foreach (var line in lines) builder.AddLine(line);
        _detailsContent = builder.WithMargin(1, 1, 1, 1).Build();
        _detailsPanel.AddControl(_detailsContent);
        _detailsPanel.ScrollToTop();

        // Update header and help bar to reflect new content state via StatusBarManager
        UpdateRightPanelHeader();
        _statusBarManager?.UpdateHelpBar(_navigationController?.CurrentViewState ?? ViewState.Projects);
    }

    private void UpdateDetailsPanel(List<IWindowControl> controls)
    {
        if (_detailsPanel == null) return;

        // Clear all previous content
        _detailsPanel.ClearContents();

        // Add all new controls
        foreach (var control in controls)
        {
            _detailsPanel.AddControl(control);
        }

        // Keep track of the first control as _detailsContent for backward compatibility
        _detailsContent = controls.FirstOrDefault() as MarkupControl;
        _detailsPanel.ScrollToTop();

        // Update header and help bar to reflect new content state via StatusBarManager
        UpdateRightPanelHeader();
        _statusBarManager?.UpdateHelpBar(_navigationController?.CurrentViewState ?? ViewState.Projects);
    }

    private bool IsRightPanelScrollable()
    {
        if (_detailsPanel == null) return false;

        // Check if content overflows viewport - indicates scrolling is possible
        return _detailsPanel.CanScrollDown || _detailsPanel.CanScrollUp;
    }

    private void UpdateRightPanelHeader()
    {
        if (_rightPanelHeader == null) return;

        string title = _navigationController?.CurrentViewState == ViewState.Projects ? "Dashboard" : "Details";
        bool scrollable = IsRightPanelScrollable();

        // Per-package cache indicator: shown in right panel header when details came from cache
        string cacheTag = (_navigationController?.CurrentViewState == ViewState.Packages
                           && _packageDetailsController?.LastLoadWasFromCache == true)
            ? " [grey50]cached[/]"
            : string.Empty;

        if (scrollable)
            _rightPanelHeader.SetContent(new List<string> { $"[grey70]{title}[/]{cacheTag} [grey50](Ctrl+↑↓ to scroll)[/]" });
        else
            _rightPanelHeader.SetContent(new List<string> { $"[grey70]{title}[/]{cacheTag}" });
    }

    private void ShowLogViewer()
    {
        // Create fresh log viewer on demand (constructor builds UI and shows it)
        _logViewer = new LogViewerWindow(_windowSystem);
    }

    private void HandleHelpBarAction(string action)
    {
        switch (action)
        {
            case "open":
                AsyncHelper.FireAndForget(
                    () => PromptForFolderAsync(),
                    ex => _errorHandler?.HandleAsync(ex, ErrorSeverity.Critical, "Folder Error", "Failed to open folder.", "UI", _window));
                break;
            case "search":
                AsyncHelper.FireAndForget(
                    () => ShowSearchModalAsync(),
                    ex => _errorHandler?.HandleAsync(ex, ErrorSeverity.Warning, "Search Error", "Failed to show search.", "UI", _window));
                break;
            case "history":
                AsyncHelper.FireAndForget(
                    () => ShowOperationHistoryAsync(),
                    ex => _errorHandler?.HandleAsync(ex, ErrorSeverity.Info, "History Error", "Failed to show history.", "UI", _window));
                break;
            case "settings":
                AsyncHelper.FireAndForget(
                    () => ShowSettingsAsync(),
                    ex => _errorHandler?.HandleAsync(ex, ErrorSeverity.Info, "Settings Error", "Failed to show settings.", "UI", _window));
                break;
            case "help":
                AsyncHelper.FireAndForget(
                    () => HelpModal.ShowAsync(_windowSystem, _window),
                    ex => _errorHandler?.HandleAsync(ex, ErrorSeverity.Info, "Help Error", "Failed to show help.", "UI", _window));
                break;
            case "exit":
                AsyncHelper.FireAndForget(
                    () => ConfirmExitAsync(),
                    ex => _errorHandler?.HandleAsync(ex, ErrorSeverity.Info, "Exit Error", "Failed to exit.", "UI", _window));
                break;
            case "filter":
                if (_navigationController?.CurrentViewState == ViewState.Packages)
                    _filterController?.EnterFilterMode();
                break;
            case "back":
                _navigationController?.HandleEscapeKey();
                break;
        }
    }

    // UpdatePanelFocusIndicators method removed - panel title highlighting feature disabled

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Dispose package check controller
        _packageCheckController?.Dispose();
        _packageCheckController = null;

        // Dispose package details controller
        _packageDetailsController?.Dispose();
        _packageDetailsController = null;

        _nugetService?.Dispose();
        // Window cleanup is handled by WindowSystem
    }
}

/// <summary>
/// View states for context-switching navigation
/// </summary>
public enum ViewState
{
    Projects,
    Packages
}

/// <summary>
/// Package detail tabs for the right panel (F1-F5)
/// </summary>
public enum PackageDetailTab
{
    Overview,
    Dependencies,
    Versions,
    WhatsNew
}
