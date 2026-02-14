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
    private ProjectInfo? _selectedProject;
    private ViewState _currentViewState = ViewState.Projects;
    private List<PackageReference> _allInstalledPackages = new();  // Unfiltered packages for filtering
    private string _packageFilter = string.Empty;  // Current filter text
    private bool _filterMode = false;  // Whether filter mode is active
    private bool _initialLoadComplete = false;  // Track first load for welcome modal
    private PackageDetailTab _currentDetailTab = PackageDetailTab.Overview;  // Current detail tab (F1-F5)
    private PackageReference? _cachedPackageRef;  // Cached package for detail tab switching
    private NuGetPackage? _cachedNuGetData;  // Cached NuGet data for detail tab switching
    private CancellationTokenSource? _packageLoadCancellation;  // Cancel token for async package loading
    private System.Threading.Timer? _loadingAnimationTimer;  // Timer for loading spinner animation
    private int _spinnerFrame = 0;  // Current frame of loading spinner
    private MarkupControl? _loadingMessageControl;  // Loading message control for updates
    private ProgressBarControl? _loadingProgressBar;  // Loading progress bar

    // Progress tracking for background package checks
    private System.Threading.Timer? _checkProgressTimer;
    private PackageCheckProgressTracker? _checkProgressTracker;

    // Services
    private ProjectDiscoveryService? _discoveryService;
    private ProjectParserService? _parserService;
    private NuGetClientService? _nugetService;
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
        _nugetService = CreateNuGetClientService();

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
        _ = LoadProjectsAsync();
    }

    private NuGetClientService CreateNuGetClientService()
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

        return new NuGetClientService(_windowSystem.LogService, sources.Count > 0 ? sources : null);
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
            .WithColors(ColorScheme.WindowBackground, Color.Grey93)
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
            if (_currentViewState == ViewState.Packages)
            {
                SwitchToProjectsView();
            }
        };

        _contextList = Controls.List()
            .WithTitle(string.Empty)
            .WithMargin(0, 1, 0, 0)
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithColors(ColorScheme.SidebarBackground, Color.Grey93)
            .WithFocusedColors(ColorScheme.SidebarBackground, Color.Grey93)
            .WithHighlightColors(Color.Grey35, Color.White)
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
            _projects);

        _modalManager = new ModalManager(
            _windowSystem,
            _configService,
            _nugetConfigService!,
            _cliService!,
            _nugetService!,
            _currentFolderPath,
            _window);
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
                _statusBarManager?.UpdateStatusRight(_currentViewState, _selectedProject);

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
            // Up/Down arrows: Navigate list only when list is NOT focused
            // If list is focused, let it handle arrows itself
            if (e.KeyInfo.Key == ConsoleKey.UpArrow && _contextList != null && _contextList.Items.Count > 0)
            {
                // Check if list control is focused - if yes, let it handle the key
                if (_windowSystem.FocusStateService.FocusedControl == _contextList)
                {
                    return; // List will handle it
                }

                // List not focused - we handle it (global navigation)
                if (_contextList.SelectedIndex > 0)
                    _contextList.SelectedIndex--;
                e.Handled = true;
                return;
            }
            if (e.KeyInfo.Key == ConsoleKey.DownArrow && _contextList != null && _contextList.Items.Count > 0)
            {
                // Check if list control is focused - if yes, let it handle the key
                if (_windowSystem.FocusStateService.FocusedControl == _contextList)
                {
                    return; // List will handle it
                }

                // List not focused - we handle it (global navigation)
                if (_contextList.SelectedIndex < _contextList.Items.Count - 1)
                    _contextList.SelectedIndex++;
                e.Handled = true;
                return;
            }

            // === PAGE UP/DOWN FOR RIGHT PANEL SCROLLING ===
            // Page Up/Down: Scroll right panel content when it overflows
            // Only handle if not already handled by a focused control
            if (e.KeyInfo.Key == ConsoleKey.PageUp && !e.AlreadyHandled)
            {
                if (_detailsPanel != null && _detailsPanel.CanScrollUp)
                {
                    _detailsPanel.ScrollVerticalBy(-10);  // Scroll up 10 lines
                    e.Handled = true;
                }
                return;
            }
            if (e.KeyInfo.Key == ConsoleKey.PageDown && !e.AlreadyHandled)
            {
                if (_detailsPanel != null && _detailsPanel.CanScrollDown)
                {
                    _detailsPanel.ScrollVerticalBy(10);  // Scroll down 10 lines
                    e.Handled = true;
                }
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
                HandleEscapeKey();
                e.Handled = true;
                return;
            }

            // Enter: Navigate forward ONLY if not already handled by a control (e.g., button)
            if (e.KeyInfo.Key == ConsoleKey.Enter)
            {
                if (!e.AlreadyHandled)
                {
                    HandleEnterKey();
                }
                e.Handled = true;
                return;
            }

            // F1-F5: Switch package detail tabs (in packages view)
            if (_currentViewState == ViewState.Packages && HandleDetailTabShortcut(e.KeyInfo.Key))
            {
                e.Handled = true;
                return;
            }

            if (e.AlreadyHandled) { e.Handled = true; return; }

            // ? - Show help modal
            if (e.KeyInfo.KeyChar == '?')
            {
                _ = HelpModal.ShowAsync(_windowSystem, _window);
                e.Handled = true;
                return;
            }

            // Ctrl+O - Open folder
            if (e.KeyInfo.Key == ConsoleKey.O && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                _ = PromptForFolderAsync();
                e.Handled = true;
            }
            // Ctrl+S - Search packages
            else if (e.KeyInfo.Key == ConsoleKey.S && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                _ = ShowSearchModalAsync();
                e.Handled = true;
            }
            // Ctrl+R - Reload
            else if (e.KeyInfo.Key == ConsoleKey.R && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                _ = LoadProjectsAsync();
                e.Handled = true;
            }
            // Ctrl+H - Operation history
            else if (e.KeyInfo.Key == ConsoleKey.H && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                _ = ShowOperationHistoryAsync();
                e.Handled = true;
            }
            // Ctrl+U - Update package (in packages view) or update all (in projects view)
            else if (e.KeyInfo.Key == ConsoleKey.U && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                if (_currentViewState == ViewState.Packages && _contextList?.SelectedItem?.Tag is PackageReference pkg)
                {
                    _ = HandleUpdatePackageAsync(pkg);
                }
                else if (_currentViewState == ViewState.Projects && _contextList?.SelectedItem?.Tag is ProjectInfo proj)
                {
                    _ = HandleUpdateAllAsync(proj);
                }
                e.Handled = true;
            }
            // Ctrl+V - Change package version
            else if (e.KeyInfo.Key == ConsoleKey.V && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                if (_currentViewState == ViewState.Packages && _contextList?.SelectedItem?.Tag is PackageReference pkgToChange)
                {
                    _ = HandleChangeVersionAsync(pkgToChange);
                }
                e.Handled = true;
            }
            // Ctrl+X - Remove package
            else if (e.KeyInfo.Key == ConsoleKey.X && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                if (_currentViewState == ViewState.Packages && _contextList?.SelectedItem?.Tag is PackageReference pkgToRemove)
                {
                    _ = HandleRemovePackageAsync(pkgToRemove);
                }
                e.Handled = true;
            }
            // Ctrl+P - Settings
            else if (e.KeyInfo.Key == ConsoleKey.P && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                _ = ShowSettingsAsync();
                e.Handled = true;
            }
            // Ctrl+D - Dependency tree
            else if (e.KeyInfo.Key == ConsoleKey.D && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                ProjectInfo? targetProject = null;
                PackageReference? targetPackage = null;

                if (_currentViewState == ViewState.Packages && _selectedProject != null)
                {
                    targetProject = _selectedProject;
                    targetPackage = _contextList?.SelectedItem?.Tag as PackageReference;
                }
                else if (_currentViewState == ViewState.Projects && _contextList?.SelectedItem?.Tag is ProjectInfo proj)
                {
                    targetProject = proj;
                }

                if (targetProject != null)
                {
                    _ = ShowDependencyTreeAsync(targetProject, targetPackage);
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
                if (_currentViewState == ViewState.Packages)
                {
                    EnterFilterMode();
                }
                e.Handled = true;
            }
            // Handle typing in filter mode
            else if (_filterMode && e.KeyInfo.Key != ConsoleKey.Escape)
            {
                HandleFilterInput(e.KeyInfo);
                e.Handled = true;
            }
        };

        // List selection changed
        if (_contextList != null)
        {
            _contextList.SelectedIndexChanged += (s, e) =>
            {
                HandleSelectionChanged();
            };

            // List item activated (Enter key in Simple mode)
            _contextList.ItemActivated += (s, item) =>
            {
                HandleEnterKey();
            };
        }
    }

    private async Task PromptForFolderAsync()
    {
        try
        {
            var selected = await FileDialogs.ShowFolderPickerAsync(_windowSystem, _currentFolderPath, _window);
            if (!string.IsNullOrEmpty(selected))
            {
                _currentFolderPath = selected;
                _configService?.TrackFolder(_currentFolderPath);
                await LoadProjectsAsync();
            }
        }
        catch (Exception ex)
        {
            _ = _errorHandler?.HandleAsync(ex, ErrorSeverity.Critical,
                "Folder Error", "Failed to open folder.", "UI", _window);
        }
    }

    private async Task LoadProjectsAsync()
    {
        if (_discoveryService == null || _parserService == null) return;

        try
        {
            // Remember current view context to restore after reload
            var previousView = _currentViewState;
            var previousProjectPath = _selectedProject?.FilePath;

            // Persist the current folder for next launch
            _configService?.TrackFolder(_currentFolderPath);

            // Re-resolve NuGet sources for the new project directory
            _nugetService?.Dispose();
            _nugetService = CreateNuGetClientService();

            // Update orchestrators with new NuGet service
            _searchCoordinator = new SearchCoordinator(_windowSystem, _nugetService, _cliService!, _historyService!, _errorHandler!, _window);
            _operationOrchestrator = new OperationOrchestrator(_windowSystem, _cliService!, _nugetService, _historyService!, _errorHandler!, _window);
            _modalManager?.SetNuGetService(_nugetService);
            _modalManager?.SetFolderPath(_currentFolderPath);
            _statusBarManager?.SetFolderPath(_currentFolderPath);

            // Show loading feedback with progress bar
            ShowLoadingPanel("Discovering projects...", $"Scanning {Markup.Escape(_currentFolderPath)}");

            // Discover projects
            var projectFiles = await _discoveryService.DiscoverProjectsAsync(_currentFolderPath);

            // Parse each project
            _projects.Clear();
            foreach (var projectFile in projectFiles)
            {
                var project = await _parserService.ParseProjectAsync(projectFile);
                if (project != null)
                {
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
                    SwitchToPackagesView(restoredProject);
                }
                else
                {
                    // Project no longer exists, go to Projects view
                    SwitchToProjectsView();
                }
            }
            else
            {
                SwitchToProjectsView();
            }

            // Check for outdated packages in the background
            _ = CheckForOutdatedPackagesAsync();

            // Show welcome modal on first load only
            if (!_initialLoadComplete)
            {
                _initialLoadComplete = true;
                if (_configService != null)
                {
                    _ = WelcomeModal.ShowIfEnabledAsync(_windowSystem, _configService, _window);
                }
            }
        }
        catch (Exception ex)
        {
            _ = _errorHandler?.HandleAsync(ex, ErrorSeverity.Warning,
                "Project Load Error", "Failed to load projects.", "Projects", _window);
        }
    }

    private async Task CheckForOutdatedPackagesAsync()
    {
        if (_nugetService == null) return;

        try
        {
            // Collect all packages from all projects
            var allPackages = _projects.SelectMany(p => p.Packages).ToList();
            if (allPackages.Count == 0) return;

            _windowSystem.LogService.LogInfo($"Checking {allPackages.Count} packages for updates...", "NuGet");

            // START PROGRESS ANIMATION
            StartCheckProgressAnimation(allPackages.Count);

            // Use semaphore to limit concurrent API calls (max 10 at a time)
            var semaphore = new SemaphoreSlim(10, 10);

            // Check packages in parallel with throttling
            var tasks = allPackages.Select(async package =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var (isOutdated, latestVersion) = await _nugetService.CheckIfOutdatedAsync(
                        package.Id,
                        package.Version);

                    package.LatestVersion = latestVersion;

                    // UPDATE PROGRESS
                    UpdateCheckProgress();
                }
                catch (Exception ex)
                {
                    _windowSystem.LogService.LogWarning($"Failed to check {package.Id}: {ex.Message}", "NuGet");
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            // STOP PROGRESS ANIMATION
            StopCheckProgressAnimation(cancelled: false);

            _windowSystem.LogService.LogInfo($"Completed checking {allPackages.Count} packages", "NuGet");
        }
        catch (OperationCanceledException)
        {
            StopCheckProgressAnimation(cancelled: true);
            _windowSystem.LogService.LogInfo("Package check cancelled", "NuGet");
        }
        catch (Exception ex)
        {
            StopCheckProgressAnimation(cancelled: false);
            _ = _errorHandler?.HandleAsync(ex, ErrorSeverity.Info,
                "Package Check Error", "Failed to check for outdated packages.", "NuGet", _window);
        }
    }

    private void SwitchToProjectsView()
    {
        // Remember which project was selected so we can restore it
        var previousProject = _selectedProject;

        _currentViewState = ViewState.Projects;
        _selectedProject = null;

        // Update header to remove back arrow
        if (_leftPanelHeader != null)
        {
            _leftPanelHeader.SetContent(new List<string> { "[grey70]Projects[/]" });
        }

        // Exit filter mode and hide filter display
        _filterMode = false;
        _packageFilter = string.Empty;
        if (_filterDisplay != null)
        {
            _filterDisplay.Visible = false;
        }

        // Update panel titles and breadcrumb via StatusBarManager
        _statusBarManager?.UpdateHeadersForProjects();
        _statusBarManager?.UpdateBreadcrumbForProjects();

        // Populate project list
        _contextList?.ClearItems();
        foreach (var project in _projects)
        {
            var displayText = $"[cyan1]{Markup.Escape(project.Name)}[/]\n" +
                            $"[grey70]  📦 {project.Packages.Count} packages · {project.TargetFramework}[/]";

            if (project.OutdatedCount > 0)
            {
                displayText += $"\n[yellow]  ⚠ {project.OutdatedCount} outdated[/]";
            }
            else
            {
                displayText += $"\n[green]  ✓ All up-to-date[/]";
            }

            _contextList?.AddItem(new ListItem(displayText) { Tag = project });
        }

        // Update help bar via StatusBarManager
        _statusBarManager?.UpdateHelpBar(_currentViewState);

        // Restore previous selection or default to first
        if (_contextList != null && _contextList.Items.Count > 0)
        {
            var restoreIndex = 0;
            if (previousProject != null)
            {
                restoreIndex = _projects.FindIndex(p => p.FilePath == previousProject.FilePath);
                if (restoreIndex < 0) restoreIndex = 0;
            }
            var wasAtIndex = _contextList.SelectedIndex == restoreIndex;
            _contextList.SelectedIndex = restoreIndex;
            // If already at this index, event won't fire, so call manually
            if (wasAtIndex)
            {
                HandleSelectionChanged();
            }
        }
        else
        {
            UpdateDetailsContent(new List<string> { "[grey50]No projects found[/]" });
        }

        // Focus the left list by default (indicators update automatically)
        _contextList?.SetFocus(true, FocusReason.Programmatic);
    }

    private void SwitchToPackagesView(ProjectInfo project)
    {
        _currentViewState = ViewState.Packages;
        _selectedProject = project;

        // Store all packages for filtering
        _allInstalledPackages = new List<PackageReference>(project.Packages);

        // Reset filter state
        _filterMode = false;
        _packageFilter = string.Empty;
        if (_filterDisplay != null)
        {
            _filterDisplay.Visible = false;
        }

        // Update panel titles and breadcrumb via StatusBarManager
        _statusBarManager?.UpdateHeadersForPackages(project);
        _statusBarManager?.UpdateBreadcrumbForPackages(project);

        // Show installed packages from the project
        _allInstalledPackages = _selectedProject.Packages;
        PopulatePackagesList(_allInstalledPackages);

        // Update left panel header
        UpdateLeftPanelHeader($"Packages ({_allInstalledPackages.Count})");

        // Update help bar via StatusBarManager
        _statusBarManager?.UpdateHelpBar(_currentViewState);

        // Trigger initial selection to show package details
        if (_contextList != null && _contextList.Items.Count > 0)
        {
            var wasZero = _contextList.SelectedIndex == 0;
            _contextList.SelectedIndex = 0;
            // If already 0, event won't fire, so call manually
            if (wasZero)
            {
                HandleSelectionChanged();
            }
        }
        else
        {
            UpdateDetailsContent(new List<string> { "[grey50]No packages in this project[/]" });
        }

        // Focus the left list by default (indicators update automatically)
        _contextList?.SetFocus(true, FocusReason.Programmatic);
    }

    private void RefreshInstalledPackages()
    {
        if (_selectedProject == null) return;

        // Show installed packages from current project
        PopulatePackagesList(_allInstalledPackages);
        UpdateLeftPanelHeader($"Packages ({_allInstalledPackages.Count})");

        // Trigger selection update
        if (_contextList != null && _contextList.Items.Count > 0)
        {
            var wasZero = _contextList.SelectedIndex == 0;
            _contextList.SelectedIndex = 0;
            if (wasZero)
            {
                HandleSelectionChanged();
            }
        }
        else
        {
            UpdateDetailsContent(new List<string> { "[grey50]No packages to display[/]" });
        }
    }

    private void UpdateLeftPanelHeader(string content)
    {
        if (_leftPanelHeader == null) return;

        // Add back arrow for Packages view
        var displayText = _currentViewState == ViewState.Packages
            ? $"[cyan1]←[/] [grey70]{content}[/]"
            : $"[grey70]{content}[/]";

        _leftPanelHeader.SetContent(new List<string> { displayText });
    }

    private void PopulatePackagesList(IEnumerable<PackageReference> packages)
    {
        _contextList?.ClearItems();
        foreach (var package in packages)
        {
            var displayText = $"[cyan1]{Markup.Escape(package.Id)}[/]\n" +
                            $"[grey70]  {package.DisplayStatus}[/]";

            _contextList?.AddItem(new ListItem(displayText) { Tag = package });
        }
    }

    private void EnterFilterMode()
    {
        if (_currentViewState != ViewState.Packages) return;

        _filterMode = true;
        _packageFilter = string.Empty;

        // Show filter display
        if (_filterDisplay != null)
        {
            _filterDisplay.Visible = true;
            UpdateFilterDisplay();
        }

        // Update help bar to show filter instructions
        if (_bottomHelpBar != null)
        {
            _bottomHelpBar.SetContent(new List<string> {
                $"[{ColorScheme.SecondaryMarkup}]Filter Mode:[/] Type to filter | " +
                $"[{ColorScheme.MutedMarkup}]Backspace to delete | Esc to exit[/]"
            });
        }
    }

    private void ExitFilterMode()
    {
        _filterMode = false;
        _packageFilter = string.Empty;

        // Hide filter display
        if (_filterDisplay != null)
        {
            _filterDisplay.Visible = false;
        }

        // Reset to show all packages
        if (_selectedProject != null)
        {
            PopulatePackagesList(_allInstalledPackages);

            // Update left panel header
            if (_leftPanelHeader != null)
            {
                _leftPanelHeader.SetContent(new List<string> { $"[grey70]Packages ({_allInstalledPackages.Count})[/]" });
            }
        }

        // Restore normal help bar
        _statusBarManager?.UpdateHelpBar(_currentViewState);
    }

    private void HandleFilterInput(ConsoleKeyInfo keyInfo)
    {
        if (keyInfo.Key == ConsoleKey.Backspace)
        {
            if (_packageFilter.Length > 0)
            {
                _packageFilter = _packageFilter.Substring(0, _packageFilter.Length - 1);
                UpdateFilterDisplay();
                FilterInstalledPackages();
            }
        }
        else if (!char.IsControl(keyInfo.KeyChar))
        {
            _packageFilter += keyInfo.KeyChar;
            UpdateFilterDisplay();
            FilterInstalledPackages();
        }
    }

    private void UpdateFilterDisplay()
    {
        if (_filterDisplay == null) return;

        var filterText = string.IsNullOrEmpty(_packageFilter) ? "_" : Markup.Escape(_packageFilter);
        _filterDisplay.SetContent(new List<string> {
            $"[{ColorScheme.MutedMarkup}]Filter: [/][{ColorScheme.PrimaryMarkup}]{filterText}[/] " +
            $"[{ColorScheme.MutedMarkup}](Esc to clear)[/]"
        });
    }

    private void FilterInstalledPackages()
    {
        if (_selectedProject == null) return;

        var query = _packageFilter.ToLowerInvariant();

        // Filter packages by ID containing the query
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _allInstalledPackages
            : _allInstalledPackages.Where(p => p.Id.ToLowerInvariant().Contains(query)).ToList();

        // Update the list
        PopulatePackagesList(filtered);

        // Update left panel header with filter status
        if (_leftPanelHeader != null)
        {
            var headerText = string.IsNullOrWhiteSpace(query)
                ? $"[grey70]Packages ({_allInstalledPackages.Count})[/]"
                : $"[grey70]Packages ({filtered.Count} of {_allInstalledPackages.Count})[/]";
            _leftPanelHeader.SetContent(new List<string> { headerText });
        }

        // Trigger selection changed to update details panel
        if (_contextList != null && _contextList.Items.Count > 0)
        {
            var wasZero = _contextList.SelectedIndex == 0;
            _contextList.SelectedIndex = 0;
            if (wasZero)
            {
                HandleSelectionChanged();
            }
        }
        else
        {
            UpdateDetailsContent(new List<string> { "[grey50]No matching packages found[/]" });
        }
    }

    private void HandleEnterKey()
    {
        if (_contextList?.SelectedItem == null) return;

        switch (_currentViewState)
        {
            case ViewState.Projects:
                if (_contextList.SelectedItem.Tag is ProjectInfo project)
                {
                    SwitchToPackagesView(project);
                }
                break;

            case ViewState.Packages:
                // Enter on a package -> already showing details on selection change; nothing extra needed
                break;
        }
    }

    private void HandleEscapeKey()
    {
        // If in filter mode, exit filter mode first
        if (_filterMode)
        {
            ExitFilterMode();
            return;
        }

        switch (_currentViewState)
        {
            case ViewState.Packages:
                SwitchToProjectsView();
                break;

            case ViewState.Projects:
                _ = ConfirmExitAsync();
                break;
        }
    }

    private async Task ConfirmExitAsync()
    {
        var confirm = await (_modalManager?.ConfirmExitAsync() ?? Task.FromResult(false));
        if (confirm) _windowSystem.Shutdown();
    }

    private void HandleSelectionChanged()
    {
        if (_contextList?.SelectedItem == null) return;

        switch (_currentViewState)
        {
            case ViewState.Projects:
                if (_contextList.SelectedItem.Tag is ProjectInfo project)
                {
                    ShowProjectDashboard(project);
                }
                break;

            case ViewState.Packages:
                if (_contextList.SelectedItem.Tag is PackageReference package)
                {
                    ShowPackageDetails(package);
                }
                break;
        }
    }

    private void ShowProjectDashboard(ProjectInfo project)
    {
        // Get outdated packages
        var outdatedPackages = project.Packages
            .Where(p => p.IsOutdated)
            .ToList();

        // Build interactive dashboard with real buttons
        var controls = InteractiveDashboardBuilder.BuildInteractiveDashboard(
            project,
            outdatedPackages,
            onViewPackages: () => SwitchToPackagesView(project),
            onUpdateAll: () => HandleUpdateAll(project),
            onRestore: () => HandleRestore(project),
            onDeps: () => _ = ShowDependencyTreeAsync(project));

        UpdateDetailsPanel(controls);
    }

    private void HandleUpdateAll(ProjectInfo project)
    {
        _ = HandleUpdateAllAsync(project);
    }

    private void HandleRestore(ProjectInfo project)
    {
        _ = HandleRestoreAsync(project);
    }

    private bool HandleDetailTabShortcut(ConsoleKey key)
    {
        var tab = key switch
        {
            ConsoleKey.F1 => PackageDetailTab.Overview,
            ConsoleKey.F2 => PackageDetailTab.Dependencies,
            ConsoleKey.F3 => PackageDetailTab.Versions,
            ConsoleKey.F4 => PackageDetailTab.WhatsNew,
            _ => (PackageDetailTab?)null
        };

        if (tab == null) return false;
        if (tab.Value == _currentDetailTab) return true;

        _currentDetailTab = tab.Value;
        RebuildPackageDetailsForTab();
        return true;
    }

    private void RebuildPackageDetailsForTab()
    {
        if (_cachedPackageRef == null) return;

        var controls = InteractivePackageDetailsBuilder.BuildInteractiveDetails(
            _cachedPackageRef,
            _cachedNuGetData,
            _currentDetailTab,
            onUpdate: () => HandleUpdatePackage(_cachedPackageRef),
            onChangeVersion: () => HandleChangeVersion(_cachedPackageRef),
            onRemove: () => HandleRemovePackage(_cachedPackageRef),
            onDeps: () => HandleShowPackageDeps(_cachedPackageRef));
        UpdateDetailsPanel(controls);
        WireUpTabClickHandlers();
    }

    private void WireUpTabClickHandlers()
    {
        if (_detailsPanel == null) return;

        // Find the single tabBar control and wire up click handler
        var tabBar = FindControlByName<MarkupControl>(_detailsPanel.GetChildren(), "tabBar");
        if (tabBar != null)
        {
            // Remove old handler if any
            tabBar.MouseClick -= OnTabBarClick;
            // Add new handler
            tabBar.MouseClick += OnTabBarClick;
        }
    }

    private T? FindControlByName<T>(IReadOnlyList<IWindowControl> controls, string name) where T : class, IWindowControl
    {
        foreach (var control in controls)
        {
            if (control is T typed && control.Name == name)
                return typed;

            // Recursively search in container controls
            if (control is IContainerControl container)
            {
                var found = FindControlByName<T>(container.GetChildren(), name);
                if (found != null)
                    return found;
            }
        }
        return null;
    }

    private void OnTabBarClick(object? sender, MouseEventArgs e)
    {
        // Calculate which tab was clicked based on X position
        // Tab layout: "F1 Overview  F2 Deps  F3 Versions  F4 What's New  F5 Details"
        // Approximate positions (including 1-char left margin from WithMargin(1,1,1,0)):
        // F1 Overview: 1-13, F2 Deps: 15-22, F3 Versions: 24-36, F4 What's New: 38-52, F5 Details: 54-66

        var x = e.Position.X;
        PackageDetailTab? newTab = x switch
        {
            >= 1 and < 14 => PackageDetailTab.Overview,
            >= 14 and < 23 => PackageDetailTab.Dependencies,
            >= 23 and < 37 => PackageDetailTab.Versions,
            >= 37 and < 53 => PackageDetailTab.WhatsNew,
            _ => null
        };

        if (newTab.HasValue && newTab.Value != _currentDetailTab)
        {
            _currentDetailTab = newTab.Value;
            RebuildPackageDetailsForTab();
        }
    }

    private void ShowPackageDetails(PackageReference package)
    {
        // Cancel any previous package load operation to prevent race conditions
        _packageLoadCancellation?.Cancel();
        _packageLoadCancellation?.Dispose();
        _packageLoadCancellation = new CancellationTokenSource();

        // Reset to overview tab and cache state
        _currentDetailTab = PackageDetailTab.Overview;
        _cachedPackageRef = package;
        _cachedNuGetData = null;

        // Show animated loading state
        ShowLoadingState(package);

        // Fetch package details asynchronously
        _ = LoadPackageDetailsAsync(package, _packageLoadCancellation.Token);
    }

    private void ShowLoadingState(PackageReference package)
    {
        var controls = new List<IWindowControl>();

        // Package header
        var header = Controls.Markup()
            .AddLine($"[cyan1 bold]Package: {Markup.Escape(package.Id)}[/]")
            .AddLine($"[grey70]Installed: {Markup.Escape(package.Version)}[/]")
            .AddEmptyLine()
            .WithMargin(1, 1, 0, 0)
            .Build();
        controls.Add(header);

        // Loading message with spinner
        _loadingMessageControl = Controls.Markup()
            .AddLine($"[cyan1]⠋[/] [grey70]Connecting to NuGet.org...[/]")
            .WithMargin(1, 1, 1, 0)
            .Build();
        controls.Add(_loadingMessageControl);

        // Indeterminate progress bar
        _loadingProgressBar = new ProgressBarControl
        {
            Value = 0,
            MaxValue = 100,
            Width = 50,
            ShowPercentage = false,
            Margin = new Margin(1, 0, 1, 1)
        };
        controls.Add(_loadingProgressBar);

        UpdateDetailsPanel(controls);

        // Start loading animation
        StartLoadingAnimation();
    }

    private void StartLoadingAnimation()
    {
        _spinnerFrame = 0;
        _loadingAnimationTimer?.Dispose();

        var spinnerChars = new[] { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
        var messages = new[]
        {
            "Connecting to NuGet.org...",
            "Fetching package metadata...",
            "Loading dependencies...",
            "Analyzing versions..."
        };

        _loadingAnimationTimer = new System.Threading.Timer(_ =>
        {
            try
            {
                _spinnerFrame++;
                var spinnerChar = spinnerChars[_spinnerFrame % spinnerChars.Length];
                var messageIndex = (_spinnerFrame / 5) % messages.Length;
                var message = messages[messageIndex];

                _loadingMessageControl?.SetContent(new List<string>
                {
                    $"[cyan1]{spinnerChar}[/] [grey70]{message}[/]"
                });

                // Animate progress bar (fake progress for visual feedback)
                if (_loadingProgressBar != null)
                {
                    _loadingProgressBar.Value = (_spinnerFrame * 3) % 100;
                }

                _window?.Invalidate(true);
            }
            catch
            {
                // Timer might fire after disposal, ignore
            }
        }, null, 0, 100); // Update every 100ms
    }

    private void StopLoadingAnimation()
    {
        _loadingAnimationTimer?.Dispose();
        _loadingAnimationTimer = null;
        _loadingMessageControl = null;
        _loadingProgressBar = null;
    }

    private void StartCheckProgressAnimation(int totalPackages)
    {
        _checkProgressTracker = new PackageCheckProgressTracker();
        _checkProgressTracker.Start(totalPackages);

        _checkProgressTimer?.Dispose();

        var spinnerFrame = 0;
        _checkProgressTimer = new System.Threading.Timer(_ =>
        {
            try
            {
                if (_checkProgressTracker == null || !_checkProgressTracker.IsActive)
                    return;

                spinnerFrame++;
                var message = _checkProgressTracker.GetProgressMessage(spinnerFrame);

                _bottomHelpBar?.SetContent(new List<string> { message });
                _window?.Invalidate(true);
            }
            catch
            {
                // Timer might fire after disposal, ignore
            }
        }, null, 0, 100); // Update every 100ms
    }

    private void StopCheckProgressAnimation(bool cancelled = false)
    {
        _checkProgressTimer?.Dispose();
        _checkProgressTimer = null;

        if (_checkProgressTracker == null) return;

        var (completed, total) = _checkProgressTracker.GetProgress();
        _checkProgressTracker.Stop();

        string completionMessage;
        int displayDurationMs;

        if (cancelled)
        {
            completionMessage = $"[yellow]⚠[/] [grey70]Update check cancelled[/]";
            displayDurationMs = 1500;
        }
        else
        {
            var outdatedCount = _checkProgressTracker.GetOutdatedCount(_projects);
            completionMessage = $"[green]✓[/] [grey70]Checked {total} packages - {outdatedCount} outdated[/]";
            displayDurationMs = 2000;
        }

        _bottomHelpBar?.SetContent(new List<string> { completionMessage });
        _window?.Invalidate(true);

        // Restore help text after delay
        _ = Task.Run(async () =>
        {
            await Task.Delay(displayDurationMs);

            if (!_filterMode && _checkProgressTracker != null && !_checkProgressTracker.IsActive)
            {
                _statusBarManager?.UpdateHelpBar(_currentViewState);
                _window?.Invalidate(true);
            }

            _checkProgressTracker = null;
        });
    }

    private void UpdateCheckProgress()
    {
        _checkProgressTracker?.IncrementCompleted();
    }

    private async Task LoadPackageDetailsAsync(PackageReference package, CancellationToken cancellationToken)
    {
        if (_nugetService == null)
        {
            StopLoadingAnimation();
            return;
        }

        try
        {
            // Fetch package details from NuGet.org
            var nugetData = await _nugetService.GetPackageDetailsAsync(package.Id);

            // Check if this operation was cancelled (user selected a different package)
            if (cancellationToken.IsCancellationRequested)
            {
                StopLoadingAnimation();
                return;
            }

            // Update the package reference with latest version info
            if (nugetData != null && !string.IsNullOrEmpty(nugetData.Version))
            {
                package.LatestVersion = nugetData.Version;
            }

            // Cache the fetched data for tab switching
            _cachedNuGetData = nugetData;

            // Check again before updating UI (user might have switched packages)
            if (cancellationToken.IsCancellationRequested)
            {
                StopLoadingAnimation();
                return;
            }

            // Stop the loading animation
            StopLoadingAnimation();

            // Rebuild the details view with the fetched data and interactive buttons
            var controls = InteractivePackageDetailsBuilder.BuildInteractiveDetails(
                package,
                nugetData,
                _currentDetailTab,
                onUpdate: () => HandleUpdatePackage(package),
                onChangeVersion: () => HandleChangeVersion(package),
                onRemove: () => HandleRemovePackage(package),
                onDeps: () => HandleShowPackageDeps(package));
            UpdateDetailsPanel(controls);
            WireUpTabClickHandlers();
        }
        catch (OperationCanceledException)
        {
            // Operation was cancelled, this is expected - do nothing
            StopLoadingAnimation();
        }
        catch (Exception ex)
        {
            StopLoadingAnimation();

            // Only show error if not cancelled
            if (!cancellationToken.IsCancellationRequested)
            {
                _ = _errorHandler?.HandleAsync(ex, ErrorSeverity.Info,
                    "Package Details Error", "Failed to load package details.", "NuGet", _window);
            }
        }
    }

    private void HandleShowPackageDeps(PackageReference package)
    {
        if (_selectedProject != null)
        {
            _ = ShowDependencyTreeAsync(_selectedProject, package);
        }
    }

    private void HandleUpdatePackage(PackageReference package)
    {
        _ = HandleUpdatePackageAsync(package);
    }

    private void HandleChangeVersion(PackageReference package)
    {
        _ = HandleChangeVersionAsync(package);
    }

    private void HandleRemovePackage(PackageReference package)
    {
        _ = HandleRemovePackageAsync(package);
    }

    // ──────────────────────────────────────────────────────────
    // Phase 4: Package Operations
    // ──────────────────────────────────────────────────────────

    private async Task ShowSearchModalAsync()
    {
        // Remember where search was initiated from
        var originView = _currentViewState;
        var originProject = _selectedProject;

        var selectedPackage = await _searchCoordinator?.ShowSearchModalAsync()!;
        if (selectedPackage == null) return;

        // Show install planning modal to select target projects
        // Pass current project context: if in Packages view, pre-select only that project; if in Projects view, select all
        var selectedProjects = await (_searchCoordinator?.ShowInstallPlanningModalAsync(selectedPackage, _projects, _selectedProject)
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
                    _selectedProject = refreshedProject;
                    SwitchToPackagesView(refreshedProject);
                }
                else
                {
                    SwitchToProjectsView();
                }
            }
            else
            {
                SwitchToProjectsView();
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
            _nugetService = CreateNuGetClientService();

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
        if (_selectedProject != null)
        {
            await ReloadProjectAsync(_selectedProject);
        }
    }

    private async Task HandleUpdatePackageAsync(PackageReference package)
    {
        if (_selectedProject == null) return;

        var result = await (_operationOrchestrator?.HandleUpdatePackageAsync(package, _selectedProject) ?? Task.FromResult(new OperationResult { Success = false }));

        // Refresh project after successful update
        if (result.Success)
        {
            await ReloadProjectAndRefreshView(_selectedProject);
        }
    }

    private async Task HandleRemovePackageAsync(PackageReference package)
    {
        if (_selectedProject == null) return;

        var result = await (_operationOrchestrator?.HandleRemovePackageAsync(package, _selectedProject) ?? Task.FromResult(new OperationResult { Success = false }));

        // Refresh project after successful removal
        if (result.Success)
        {
            await ReloadProjectAndRefreshView(_selectedProject);
        }
    }

    private async Task HandleChangeVersionAsync(PackageReference package)
    {
        if (_selectedProject == null) return;

        var result = await (_operationOrchestrator?.HandleChangeVersionAsync(package, _selectedProject) ?? Task.FromResult(new OperationResult { Success = false }));

        if (result.Success)
        {
            await ReloadProjectAndRefreshView(_selectedProject);
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
            if (_currentViewState == ViewState.Projects)
            {
                SwitchToProjectsView();
            }
            else if (_currentViewState == ViewState.Packages)
            {
                SwitchToPackagesView(project);
            }
        }
    }

    private async Task HandleRestoreAsync(ProjectInfo project)
    {
        var result = await (_operationOrchestrator?.HandleRestoreAsync(project) ?? Task.FromResult(new OperationResult { Success = false }));

        // Refresh project after successful restore
        if (result.Success && _selectedProject?.FilePath == project.FilePath)
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
        if (refreshed != null && _currentViewState == ViewState.Packages)
        {
            // Already in packages view - just update data and refresh list
            _selectedProject = refreshed;
            _allInstalledPackages = new List<PackageReference>(refreshed.Packages);

            // Update headers/breadcrumb
            _statusBarManager?.UpdateHeadersForPackages(refreshed);
            _statusBarManager?.UpdateBreadcrumbForPackages(refreshed);

            // Refresh the package list
            RefreshInstalledPackages();
        }
        else
        {
            SwitchToProjectsView();
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
        _statusBarManager?.UpdateHelpBar(_currentViewState);
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
        _statusBarManager?.UpdateHelpBar(_currentViewState);
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

        string title = _currentViewState == ViewState.Projects ? "Dashboard" : "Details";
        bool scrollable = IsRightPanelScrollable();

        if (scrollable)
        {
            _rightPanelHeader.SetContent(new List<string> { $"[grey70]{title}[/] [grey50](PgUp/PgDn to scroll)[/]" });
        }
        else
        {
            _rightPanelHeader.SetContent(new List<string> { $"[grey70]{title}[/]" });
        }
    }

    private void ShowLogViewer()
    {
        // Create fresh log viewer on demand (constructor builds UI and shows it)
        _logViewer = new LogViewerWindow(_windowSystem);
    }

    // UpdatePanelFocusIndicators method removed - panel title highlighting feature disabled

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Dispose check progress timer
        _checkProgressTimer?.Dispose();
        _checkProgressTimer = null;
        _checkProgressTracker = null;

        // Dispose loading animation timer
        _loadingAnimationTimer?.Dispose();
        _loadingAnimationTimer = null;

        // Dispose package load cancellation
        _packageLoadCancellation?.Cancel();
        _packageLoadCancellation?.Dispose();
        _packageLoadCancellation = null;

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
