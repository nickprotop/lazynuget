using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drawing;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Core;
using SharpConsoleUI.Dialogs;
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
    private ButtonControl? _searchInstallButton;  // Install button in search view
    private MarkupControl? _searchInstallLabel;  // Label showing selected project name


    // State
    private string _currentFolderPath;
    private List<ProjectInfo> _projects = new();
    private ProjectInfo? _selectedProject;
    private ViewState _currentViewState = ViewState.Projects;
    private List<PackageReference> _allInstalledPackages = new();  // Unfiltered packages for filtering
    private string _packageFilter = string.Empty;  // Current filter text
    private bool _filterMode = false;  // Whether filter mode is active
    private PackageTabView _currentPackageTab = PackageTabView.Installed;  // Current tab in packages view

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

        // Create install label and button for search view (initially hidden)
        _searchInstallLabel = Controls.Markup("[grey70]Select a project[/]")
            .WithMargin(1, 1, 0, 0)
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .Build();
        _searchInstallLabel.Visible = false; // Hidden by default

        _searchInstallButton = Controls.Button("[grey93]Install Package (I)[/]")
            .WithMargin(1, 1, 0, 0)
            .WithAlignment(HorizontalAlignment.Center)
            .WithBackgroundColor(Color.DarkGreen)
            .WithForegroundColor(Color.White)
            .WithFocusedBackgroundColor(Color.Green)
            .WithFocusedForegroundColor(Color.White)
            .Build();
        _searchInstallButton.Visible = false; // Hidden by default
        _searchInstallButton.Click += (s, e) => {
            var searchResults = _searchCoordinator?.GetSearchResults() ?? new List<NuGetPackage>();
            if (searchResults.Count > 0)
            {
                _ = HandleInstallFromSearchAsync(searchResults[0]);
            }
        };

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
                _statusBarManager?.UpdateStatusRight(_currentViewState, _selectedProject, _searchCoordinator?.GetSearchResults() ?? new List<NuGetPackage>());

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
            if (e.KeyInfo.Key == ConsoleKey.PageUp)
            {
                if (_detailsPanel != null && _detailsPanel.CanScrollUp)
                {
                    _detailsPanel.ScrollVerticalBy(-10);  // Scroll up 10 lines
                }
                e.Handled = true;
                return;
            }
            if (e.KeyInfo.Key == ConsoleKey.PageDown)
            {
                if (_detailsPanel != null && _detailsPanel.CanScrollDown)
                {
                    _detailsPanel.ScrollVerticalBy(10);  // Scroll down 10 lines
                }
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

            // 'I' key for Install in Search view
            if (e.KeyInfo.Key == ConsoleKey.I && _currentViewState == ViewState.Search)
            {
                var searchResults = _searchCoordinator?.GetSearchResults() ?? new List<NuGetPackage>();
                if (searchResults.Count > 0 && _contextList != null && _contextList.Items.Count > 0)
                {
                    // Ensure we have a valid selection
                    if (_contextList.SelectedIndex < 0 || _contextList.SelectedIndex >= _contextList.Items.Count)
                    {
                        _contextList.SelectedIndex = 0;
                    }
                    _ = HandleInstallFromSearchAsync(searchResults[0]);
                }
                e.Handled = true;
                return;
            }

            if (e.AlreadyHandled) { e.Handled = true; return; }

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
            // Ctrl+T - Cycle tabs (in packages view)
            else if (e.KeyInfo.Key == ConsoleKey.T && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                if (_currentViewState == ViewState.Packages)
                {
                    CyclePackageTab();
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

            // Update view
            SwitchToProjectsView();

            // Check for outdated packages in the background
            _ = CheckForOutdatedPackagesAsync();
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

            // Use semaphore to limit concurrent API calls (max 10 at a time)
            var semaphore = new SemaphoreSlim(10, 10);
            var completedCount = 0;
            var lastUiUpdate = DateTime.MinValue;
            var uiUpdateLock = new object();

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

                    // Increment completed count and update UI periodically (not on every package)
                    lock (uiUpdateLock)
                    {
                        completedCount++;
                        var now = DateTime.Now;
                        var shouldUpdate = (now - lastUiUpdate).TotalMilliseconds >= 500 || // Every 500ms
                                          completedCount % 5 == 0 || // Every 5 packages
                                          completedCount == allPackages.Count; // Final update

                        if (shouldUpdate)
                        {
                            lastUiUpdate = now;
                            // Trigger UI refresh based on current view
                            var currentSelection = _contextList?.SelectedIndex ?? 0;

                            if (_currentViewState == ViewState.Projects)
                            {
                                SwitchToProjectsView();
                                if (_contextList != null && currentSelection >= 0 && currentSelection < _projects.Count)
                                {
                                    _contextList.SelectedIndex = currentSelection;
                                }
                            }
                            else if (_currentViewState == ViewState.Packages && _selectedProject != null)
                            {
                                // Refresh the current project view - just update data, don't rebuild the entire view
                                var refreshed = _projects.FirstOrDefault(p => p.FilePath == _selectedProject.FilePath);
                                if (refreshed != null)
                                {
                                    _selectedProject = refreshed;
                                    _allInstalledPackages = new List<PackageReference>(refreshed.Packages);

                                    // Refresh the package list without switching views
                                    RefreshPackageListForCurrentTab();

                                    // Restore selection
                                    if (_contextList != null && currentSelection >= 0 && currentSelection < refreshed.Packages.Count)
                                    {
                                        _contextList.SelectedIndex = currentSelection;
                                    }
                                }
                            }
                        }
                    }
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

            _windowSystem.LogService.LogInfo($"Completed checking {allPackages.Count} packages", "NuGet");
        }
        catch (Exception ex)
        {
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

        // Exit filter mode and hide filter display
        _filterMode = false;
        _packageFilter = string.Empty;
        if (_filterDisplay != null)
        {
            _filterDisplay.Visible = false;
        }

        // Hide search install button
        if (_searchInstallButton != null)
        {
            _searchInstallButton.Visible = false;
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

        // Hide search install button
        if (_searchInstallButton != null)
        {
            _searchInstallButton.Visible = false;
        }

        // Reset to Installed tab by default
        _currentPackageTab = PackageTabView.Installed;

        // Update panel titles and breadcrumb via StatusBarManager
        _statusBarManager?.UpdateHeadersForPackages(project);
        _statusBarManager?.UpdateBreadcrumbForPackages(project);

        // Populate package list based on current tab
        RefreshPackageListForCurrentTab();

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

    private void CyclePackageTab()
    {
        if (_currentViewState != ViewState.Packages) return;

        // Cycle between tabs
        _currentPackageTab = _currentPackageTab == PackageTabView.Installed
            ? PackageTabView.Recent
            : PackageTabView.Installed;

        // Exit filter mode when switching tabs
        if (_filterMode)
        {
            ExitFilterMode();
        }

        // Refresh the package list
        RefreshPackageListForCurrentTab();
    }

    private void RefreshPackageListForCurrentTab()
    {
        if (_selectedProject == null) return;

        switch (_currentPackageTab)
        {
            case PackageTabView.Installed:
                // Show installed packages from current project
                PopulatePackagesList(_allInstalledPackages);
                UpdateLeftPanelHeaderForTab($"Installed ({_allInstalledPackages.Count})");
                break;

            case PackageTabView.Recent:
                // Show recent packages from history
                _ = LoadRecentPackagesAsync();
                break;
        }

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

    private async Task LoadRecentPackagesAsync()
    {
        if (_historyService == null) return;

        try
        {
            var recentPackages = await _historyService.GetRecentInstallsAsync();

            _contextList?.ClearItems();

            if (recentPackages.Count == 0)
            {
                UpdateLeftPanelHeaderForTab("Recent (0)");
                UpdateDetailsContent(new List<string> { "[grey50]No recently installed packages[/]" });
                return;
            }

            UpdateLeftPanelHeaderForTab($"Recent ({recentPackages.Count})");

            foreach (var pkg in recentPackages)
            {
                var timeSince = DateTime.Now - pkg.LastInstalled;
                var timeText = timeSince.TotalHours < 1
                    ? $"{(int)timeSince.TotalMinutes}m ago"
                    : timeSince.TotalDays < 1
                        ? $"{(int)timeSince.TotalHours}h ago"
                        : $"{(int)timeSince.TotalDays}d ago";

                var displayText = $"[cyan1]{Markup.Escape(pkg.PackageId)}[/]\n" +
                                $"[grey70]  v{Markup.Escape(pkg.Version)} · {timeText}[/]\n" +
                                $"[grey50]  Last used in: {Markup.Escape(pkg.ProjectName)}[/]";

                _contextList?.AddItem(new ListItem(displayText) { Tag = pkg });
            }

            // Trigger initial selection to show package details
            if (_contextList != null && _contextList.Items.Count > 0)
            {
                var wasZero = _contextList.SelectedIndex == 0;
                _contextList.SelectedIndex = 0;
                if (wasZero)
                {
                    HandleSelectionChanged();
                }
            }
        }
        catch (Exception ex)
        {
            _ = _errorHandler?.HandleAsync(ex, ErrorSeverity.Info,
                "Recent Packages Error", "Failed to load recent packages.", "History", _window);
        }
    }

    private void UpdateLeftPanelHeaderForTab(string content)
    {
        if (_leftPanelHeader == null) return;

        var tabIndicator = _currentPackageTab == PackageTabView.Installed
            ? "[white on grey35] Installed [/] [grey50]Recent[/]"
            : "[grey50]Installed[/] [white on grey35] Recent [/]";

        _leftPanelHeader.SetContent(new List<string> {
            $"[grey70]{content}[/]",
            $"{tabIndicator} [grey50](Ctrl+T)[/]"
        });
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
                // In Recent tab, allow installing the selected package
                if (_currentPackageTab == PackageTabView.Recent && _contextList.SelectedItem.Tag is RecentPackageInfo recentPkg)
                {
                    _ = HandleInstallRecentPackageAsync(recentPkg);
                }
                // In Installed tab, already showing details on selection change; nothing extra on Enter
                break;

            case ViewState.Search:
                // In Search view, Enter does nothing - use 'I' key to install
                break;
        }
    }

    private async Task HandleInstallRecentPackageAsync(RecentPackageInfo recentPkg)
    {
        if (_selectedProject == null) return;

        // Create a NuGetPackage object for the orchestrator
        var package = new NuGetPackage
        {
            Id = recentPkg.PackageId,
            Version = recentPkg.Version == "latest" ? "" : recentPkg.Version,
            Description = $"Recently installed package"
        };

        var result = await (_searchCoordinator?.HandleInstallFromSearchAsync(package, _selectedProject) ?? Task.FromResult(new OperationResult { Success = false }));

        // Refresh project after successful installation
        if (result.Success)
        {
            await ReloadProjectAsync(_selectedProject);

            // Refresh the current tab to show updated state
            RefreshPackageListForCurrentTab();
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

            case ViewState.Search:
                RestorePreSearchView();
                break;

            case ViewState.Projects:
                _ = ConfirmExitAsync();
                break;
        }
    }

    private void RestorePreSearchView()
    {
        var (viewState, selectedIndex) = _searchCoordinator?.GetPreSearchState() ?? (ViewState.Projects, 0);

        if (viewState == ViewState.Packages && _selectedProject != null)
        {
            // Return to the exact project's package list
            var project = _projects.FirstOrDefault(p => p.FilePath == _selectedProject.FilePath);
            if (project != null)
            {
                SwitchToPackagesView(project);
                // Restore the previously selected package index
                if (_contextList != null && selectedIndex >= 0
                    && selectedIndex < _contextList.Items.Count)
                {
                    _contextList.SelectedIndex = selectedIndex;
                }
                return;
            }
        }

        // Default: return to Projects view (preserves selected project via _selectedProject)
        SwitchToProjectsView();
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
                else if (_contextList.SelectedItem.Tag is RecentPackageInfo recentPkg)
                {
                    ShowRecentPackageDetails(recentPkg);
                }
                break;

            case ViewState.Search:
                // In search view, list shows projects; keep showing the searched package details
                var searchResults = _searchCoordinator?.GetSearchResults() ?? new List<NuGetPackage>();
                if (searchResults.Count > 0)
                {
                    ShowSearchPackageDetails(searchResults[0]);
                }
                // Update the install button text with the selected project name
                UpdateSearchInstallButtonText();
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

    private void ShowPackageDetails(PackageReference package)
    {
        // Show loading state with interactive buttons
        var loadingControls = InteractivePackageDetailsBuilder.BuildInteractiveDetails(
            package,
            nugetData: null,
            onUpdate: () => HandleUpdatePackage(package),
            onChangeVersion: () => HandleChangeVersion(package),
            onRemove: () => HandleRemovePackage(package),
            onDeps: () => HandleShowPackageDeps(package));
        UpdateDetailsPanel(loadingControls);

        // Fetch package details asynchronously
        _ = LoadPackageDetailsAsync(package);
    }

    private async Task LoadPackageDetailsAsync(PackageReference package)
    {
        if (_nugetService == null) return;

        try
        {
            // Fetch package details from NuGet.org
            var nugetData = await _nugetService.GetPackageDetailsAsync(package.Id);

            // Update the package reference with latest version info
            if (nugetData != null && !string.IsNullOrEmpty(nugetData.Version))
            {
                package.LatestVersion = nugetData.Version;
            }

            // Rebuild the details view with the fetched data and interactive buttons
            var controls = InteractivePackageDetailsBuilder.BuildInteractiveDetails(
                package,
                nugetData,
                onUpdate: () => HandleUpdatePackage(package),
                onChangeVersion: () => HandleChangeVersion(package),
                onRemove: () => HandleRemovePackage(package),
                onDeps: () => HandleShowPackageDeps(package));
            UpdateDetailsPanel(controls);
        }
        catch (Exception ex)
        {
            _ = _errorHandler?.HandleAsync(ex, ErrorSeverity.Info,
                "Package Details Error", "Failed to load package details.", "NuGet", _window);
        }
    }

    private void ShowRecentPackageDetails(RecentPackageInfo recentPkg)
    {
        // Show loading state
        var lines = new List<string>
        {
            $"[{ColorScheme.PrimaryMarkup}]{Markup.Escape(recentPkg.PackageId)}[/]",
            "",
            $"[grey70]Last Installed:[/] {recentPkg.LastInstalled:yyyy-MM-dd HH:mm}",
            $"[grey70]Version:[/] {Markup.Escape(recentPkg.Version)}",
            $"[grey70]Last Used In:[/] {Markup.Escape(recentPkg.ProjectName)}",
            "",
            "[grey50]Loading package details...[/]"
        };
        UpdateDetailsContent(lines);

        // Fetch full package details asynchronously
        _ = LoadRecentPackageDetailsAsync(recentPkg);
    }

    private async Task LoadRecentPackageDetailsAsync(RecentPackageInfo recentPkg)
    {
        if (_nugetService == null || _selectedProject == null) return;

        try
        {
            // Fetch package details from NuGet.org
            var nugetData = await _nugetService.GetPackageDetailsAsync(recentPkg.PackageId);

            // Build details view with install button
            var lines = new List<string>
            {
                $"[{ColorScheme.PrimaryMarkup}]{Markup.Escape(recentPkg.PackageId)}[/]",
                "",
                $"[grey70]Last Installed:[/] {recentPkg.LastInstalled:yyyy-MM-dd HH:mm}",
                $"[grey70]Last Version:[/] {Markup.Escape(recentPkg.Version)}",
                $"[grey70]Last Used In:[/] {Markup.Escape(recentPkg.ProjectName)}",
                ""
            };

            if (nugetData != null)
            {
                lines.Add($"[{ColorScheme.SecondaryMarkup}]Latest Version:[/] {Markup.Escape(nugetData.Version ?? "N/A")}");
                if (!string.IsNullOrEmpty(nugetData.Description))
                {
                    lines.Add("");
                    lines.Add($"[grey70]{Markup.Escape(nugetData.Description)}[/]");
                }
                if (nugetData.Authors != null && nugetData.Authors.Any())
                {
                    lines.Add("");
                    var authors = string.Join(", ", nugetData.Authors);
                    lines.Add($"[grey50]Authors:[/] {Markup.Escape(authors)}");
                }
                if (nugetData.TotalDownloads > 0)
                {
                    lines.Add($"[grey50]Downloads:[/] {nugetData.TotalDownloads:N0}");
                }
            }

            lines.Add("");
            lines.Add($"[{ColorScheme.MutedMarkup}]Press Enter to install this package to the current project[/]");

            UpdateDetailsContent(lines);
        }
        catch (Exception ex)
        {
            _ = _errorHandler?.HandleAsync(ex, ErrorSeverity.Info,
                "Package Details Error", "Failed to load package details.", "NuGet", _window);
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
        var selectedPackage = await _searchCoordinator?.ShowSearchModalAsync()!;
        if (selectedPackage == null) return;

        // Switch to search results view with the selected package
        SwitchToSearchResultsView(selectedPackage);
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

    private void SwitchToSearchResultsView(NuGetPackage selectedPackage)
    {
        if (selectedPackage == null) return;

        _windowSystem.LogService.LogInfo($"SwitchToSearchResultsView - package={selectedPackage.Id}, current view={_currentViewState}", "SEARCH");

        // Remember the current view so Escape restores it exactly via SearchCoordinator
        _searchCoordinator?.SavePreSearchState(
            _currentViewState,
            _contextList?.SelectedIndex ?? 0,
            _currentViewState == ViewState.Projects && _contextList?.SelectedItem?.Tag is ProjectInfo currentProj ? currentProj : _selectedProject);

        _currentViewState = ViewState.Search;
        _windowSystem.LogService.LogInfo($"SwitchToSearchResultsView - ViewState NOW SET to Search", "SEARCH");
        _searchCoordinator?.SetSearchResults(new List<NuGetPackage> { selectedPackage });

        // Exit filter mode and hide filter display
        _filterMode = false;
        _packageFilter = string.Empty;
        if (_filterDisplay != null)
        {
            _filterDisplay.Visible = false;
        }

        // Hide search install button
        if (_searchInstallButton != null)
        {
            _searchInstallButton.Visible = false;
        }

        // Update panel titles and breadcrumb via StatusBarManager
        _statusBarManager?.UpdateHeadersForSearch();
        _statusBarManager?.UpdateBreadcrumbForSearch(selectedPackage.Id);

        // Show project list to pick install target
        _contextList?.ClearItems();
        int selectedProjectIndex = 0;
        int currentIndex = 0;
        foreach (var project in _projects)
        {
            var alreadyInstalled = project.Packages.Any(p =>
                string.Equals(p.Id, selectedPackage.Id, StringComparison.OrdinalIgnoreCase));

            var statusText = alreadyInstalled ? "[yellow]  (already installed)[/]" : "[green]  (available)[/]";
            var displayText = $"[cyan1]{Markup.Escape(project.Name)}[/]\n" +
                            $"[grey70]  {project.TargetFramework}[/]{statusText}";

            _contextList?.AddItem(new ListItem(displayText) { Tag = project });

            // Remember the index of the currently selected project
            if (_selectedProject != null && project.FilePath == _selectedProject.FilePath)
            {
                selectedProjectIndex = currentIndex;
            }
            currentIndex++;
        }

        // Show package details on right
        ShowSearchPackageDetails(selectedPackage);

        // Set initial selection to the previously selected project, or first project
        if (_contextList != null && _contextList.Items.Count > 0)
        {
            _contextList.SelectedIndex = selectedProjectIndex;
        }

        // Show install button and update its text based on selection
        if (_searchInstallButton != null)
        {
            _searchInstallButton.Visible = true;
            UpdateSearchInstallButtonText();
        }

        // Update help bar via StatusBarManager
        _statusBarManager?.UpdateHelpBar(_currentViewState);

        // Focus the left list by default (indicators update automatically)
        _contextList?.SetFocus(true, FocusReason.Programmatic);
    }

    private void ShowSearchPackageDetails(NuGetPackage package)
    {
        var lines = _searchCoordinator?.FormatSearchPackageDetails(package) ?? new List<string>();

        // Build the content controls
        var controls = new List<IWindowControl>();

        // Add the package details as markup
        var builder = Controls.Markup();
        foreach (var line in lines) builder.AddLine(line);
        var detailsMarkup = builder.WithMargin(1, 1, 1, 1).Build();
        controls.Add(detailsMarkup);

        // Add the install button right below the hint text
        if (_searchInstallButton != null)
        {
            _searchInstallButton.Visible = true;
            controls.Add(_searchInstallButton);
        }

        UpdateDetailsPanel(controls);
    }

    private void UpdateSearchInstallButtonText()
    {
        if (_searchInstallButton == null || _contextList == null) return;

        // Get the currently selected project
        if (_contextList.SelectedIndex >= 0 && _contextList.SelectedIndex < _contextList.Items.Count)
        {
            var selectedItem = _contextList.Items[_contextList.SelectedIndex];
            if (selectedItem.Tag is ProjectInfo project)
            {
                _searchInstallButton.Text = $"[white]Install in {Markup.Escape(project.Name)} (I)[/]";
                _searchInstallButton.IsEnabled = true;
                return;
            }
        }

        // No valid selection
        _searchInstallButton.Text = "[grey70]Select a project to install[/]";
        _searchInstallButton.IsEnabled = false;
    }

    private async Task HandleInstallFromSearchAsync(NuGetPackage package)
    {
        try
        {
            if (package == null) return;

            // In search view, the list items are projects — get the selected project
            // Use SelectedIndex instead of SelectedItem to be resilient to focus changes
            ProjectInfo? targetProject = null;
            if (_contextList != null && _contextList.SelectedIndex >= 0 && _contextList.SelectedIndex < _contextList.Items.Count)
            {
                targetProject = _contextList.Items[_contextList.SelectedIndex].Tag as ProjectInfo;
            }

            if (targetProject == null) return;
            var result = await (_searchCoordinator?.HandleInstallFromSearchAsync(package, targetProject) ?? Task.FromResult(new OperationResult { Success = false }));

            // Refresh project after successful installation
            if (result.Success)
            {
                await ReloadProjectAsync(targetProject);
                SwitchToProjectsView();
            }
        }
        catch (Exception ex)
        {
            await (_errorHandler?.HandleAsync(ex, ErrorSeverity.Critical,
                "Install Error", "An error occurred while installing the package.", "Search", _window)
                ?? Task.CompletedTask);
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
            RefreshPackageListForCurrentTab();
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
    Packages,
    Search
}

/// <summary>
/// Package view tabs
/// </summary>
public enum PackageTabView
{
    Installed,
    Recent
}
