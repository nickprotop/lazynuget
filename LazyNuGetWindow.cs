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


    // State
    private string _currentFolderPath;
    private List<ProjectInfo> _projects = new();
    private ProjectInfo? _selectedProject;
    private ViewState _currentViewState = ViewState.Projects;
    private ViewState _preSearchViewState = ViewState.Projects;
    private int _preSearchSelectedIndex = 0;
    private List<NuGetPackage> _searchResults = new();

    // Services
    private ProjectDiscoveryService? _discoveryService;
    private ProjectParserService? _parserService;
    private NuGetClientService? _nugetService;
    private DotNetCliService? _cliService;
    private ConfigurationService? _configService;
    private OperationHistoryService? _historyService;

    public LazyNuGetWindow(ConsoleWindowSystem windowSystem, string folderPath, ConfigurationService? configService = null)
    {
        _windowSystem = windowSystem ?? throw new ArgumentNullException(nameof(windowSystem));
        _currentFolderPath = folderPath;
        _configService = configService;

        // Initialize services
        _discoveryService = new ProjectDiscoveryService();
        _parserService = new ProjectParserService(_windowSystem.LogService);
        _nugetService = new NuGetClientService(_windowSystem.LogService);
        _cliService = new DotNetCliService(_windowSystem.LogService);

        // Initialize operation history service
        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LazyNuGet");
        Directory.CreateDirectory(configDir);
        _historyService = new OperationHistoryService(configDir);

        BuildUI();
        SetupEventHandlers();

        // Load projects asynchronously
        _ = LoadProjectsAsync();
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

        var mainGrid = Controls.HorizontalGrid()
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithAlignment(HorizontalAlignment.Stretch)
            .Column(col => col
                .Width(40)
                .Add(_leftPanelHeader)
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

        // Bottom help bar
        _bottomHelpBar = Controls.Markup(GetHelpText())
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
                // Update clock and context-aware stats
                if (_topStatusRight != null)
                {
                    var stats = GetStatusRightText();
                    _topStatusRight.SetContent(new List<string> { $"[grey70]{stats}[/]" });
                }

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
            // If a button has focus and handles Enter, don't also trigger navigation
            if (e.KeyInfo.Key == ConsoleKey.Enter)
            {
                if (!e.AlreadyHandled)
                {
                    HandleEnterKey();
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
            // Ctrl+L - Show log viewer
            else if (e.KeyInfo.Key == ConsoleKey.L && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                ShowLogViewer();
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
            _windowSystem.LogService.LogError($"Error selecting folder: {ex.Message}", ex, "UI");
            _ = ErrorModal.ShowAsync(_windowSystem, "Folder Error",
                "Failed to open folder.", ex.Message, _window);
        }
    }

    private async Task LoadProjectsAsync()
    {
        if (_discoveryService == null || _parserService == null) return;

        try
        {
            // Persist the current folder for next launch
            _configService?.TrackFolder(_currentFolderPath);

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

            // Update view
            SwitchToProjectsView();

            // Check for outdated packages in the background
            _ = CheckForOutdatedPackagesAsync();
        }
        catch (Exception ex)
        {
            _windowSystem.LogService.LogError($"Error loading projects: {ex.Message}", ex, "Projects");
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
                                // Refresh the current project view
                                var refreshed = _projects.FirstOrDefault(p => p.FilePath == _selectedProject.FilePath);
                                if (refreshed != null)
                                {
                                    SwitchToPackagesView(refreshed);
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
            _windowSystem.LogService.LogError($"Error checking for outdated packages: {ex.Message}", ex, "NuGet");
        }
    }

    private void SwitchToProjectsView()
    {
        // Remember which project was selected so we can restore it
        var previousProject = _selectedProject;

        _currentViewState = ViewState.Projects;
        _selectedProject = null;

        // Update panel titles
        _leftPanelHeader?.SetContent(new List<string> { "[grey70]Projects[/]" });
        _rightPanelHeader?.SetContent(new List<string> { "[grey70]Dashboard[/]" });

        // Update breadcrumb — show folder name (not full path) for a cleaner look
        var folderName = Path.GetFileName(_currentFolderPath.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrEmpty(folderName)) folderName = _currentFolderPath;
        _topStatusLeft?.SetContent(new List<string> { $"[cyan1]{Markup.Escape(folderName)}[/] [grey50]({Markup.Escape(_currentFolderPath)})[/]" });

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

        // Update help bar
        _bottomHelpBar?.SetContent(new List<string> { GetHelpText() });

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

        // Update panel titles
        _leftPanelHeader?.SetContent(new List<string> { $"[grey70]{Markup.Escape(project.Name)} › Packages[/]" });
        _rightPanelHeader?.SetContent(new List<string> { "[grey70]Details[/]" });

        // Update breadcrumb
        var projFolderName = Path.GetFileName(_currentFolderPath.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrEmpty(projFolderName)) projFolderName = _currentFolderPath;
        _topStatusLeft?.SetContent(new List<string> { $"[grey50]{Markup.Escape(projFolderName)}[/] [grey50]›[/] [cyan1]{Markup.Escape(project.Name)}[/] [grey50]› Packages[/]" });

        // Populate package list
        _contextList?.ClearItems();
        foreach (var package in project.Packages)
        {
            var displayText = $"[cyan1]{Markup.Escape(package.Id)}[/]\n" +
                            $"[grey70]  {package.DisplayStatus}[/]";

            _contextList?.AddItem(new ListItem(displayText) { Tag = package });
        }

        // Update help bar
        _bottomHelpBar?.SetContent(new List<string> { GetHelpText() });

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
                // Already showing details on selection change; nothing extra on Enter
                break;

            case ViewState.Search:
                // In search view, list items are projects; the package is in _searchResults
                if (_searchResults.Count > 0 && _contextList.SelectedItem.Tag is ProjectInfo)
                {
                    _ = HandleInstallFromSearchAsync(_searchResults[0]);
                }
                break;
        }
    }

    private void HandleEscapeKey()
    {
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
        if (_preSearchViewState == ViewState.Packages && _selectedProject != null)
        {
            // Return to the exact project's package list
            var project = _projects.FirstOrDefault(p => p.FilePath == _selectedProject.FilePath);
            if (project != null)
            {
                SwitchToPackagesView(project);
                // Restore the previously selected package index
                if (_contextList != null && _preSearchSelectedIndex >= 0
                    && _preSearchSelectedIndex < _contextList.Items.Count)
                {
                    _contextList.SelectedIndex = _preSearchSelectedIndex;
                }
                return;
            }
        }

        // Default: return to Projects view (preserves selected project via _selectedProject)
        SwitchToProjectsView();
    }

    private async Task ConfirmExitAsync()
    {
        var confirm = await ConfirmationModal.ShowAsync(_windowSystem,
            "Exit LazyNuGet",
            "Are you sure you want to exit?",
            yesText: "Exit", noText: "Cancel",
            parentWindow: _window);
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

            case ViewState.Search:
                // In search view, list shows projects; keep showing the searched package details
                if (_searchResults.Count > 0)
                {
                    ShowSearchPackageDetails(_searchResults[0]);
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
            onRestore: () => HandleRestore(project));

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
            onRemove: () => HandleRemovePackage(package));
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
                onRemove: () => HandleRemovePackage(package));
            UpdateDetailsPanel(controls);
        }
        catch (Exception ex)
        {
            _windowSystem.LogService.LogError($"Error loading package details: {ex.Message}", ex, "NuGet");
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
        if (_nugetService == null) return;

        var selectedPackage = await SearchPackageModal.ShowAsync(_windowSystem, _nugetService, _window);
        if (selectedPackage == null) return;

        // Switch to search results view with the selected package
        SwitchToSearchResultsView(selectedPackage);
    }

    private async Task ShowOperationHistoryAsync()
    {
        if (_cliService == null || _historyService == null) return;

        await OperationHistoryModal.ShowAsync(_windowSystem, _historyService, _cliService, _window);

        // Refresh current view after history modal closes (in case user retried operations)
        if (_selectedProject != null)
        {
            await ReloadProjectAsync(_selectedProject);
        }
    }

    private void SwitchToSearchResultsView(NuGetPackage selectedPackage)
    {
        // Remember the current view so Escape restores it exactly
        _preSearchViewState = _currentViewState;
        _preSearchSelectedIndex = _contextList?.SelectedIndex ?? 0;
        if (_currentViewState == ViewState.Projects && _contextList?.SelectedItem?.Tag is ProjectInfo currentProj)
        {
            _selectedProject = currentProj;
        }

        _currentViewState = ViewState.Search;
        _searchResults = new List<NuGetPackage> { selectedPackage };

        // Update panel titles
        _leftPanelHeader?.SetContent(new List<string> { "[grey70]Install Package[/]" });
        _rightPanelHeader?.SetContent(new List<string> { "[grey70]Details[/]" });

        // Update breadcrumb
        var searchFolderName = Path.GetFileName(_currentFolderPath.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrEmpty(searchFolderName)) searchFolderName = _currentFolderPath;
        _topStatusLeft?.SetContent(new List<string> { $"[grey50]{Markup.Escape(searchFolderName)}[/] [grey50]›[/] [cyan1]Search[/] [grey50]›[/] [{ColorScheme.InfoMarkup}]{Markup.Escape(selectedPackage.Id)}[/]" });

        // Show project list to pick install target
        _contextList?.ClearItems();
        foreach (var project in _projects)
        {
            var alreadyInstalled = project.Packages.Any(p =>
                string.Equals(p.Id, selectedPackage.Id, StringComparison.OrdinalIgnoreCase));

            var statusText = alreadyInstalled ? "[yellow]  (already installed)[/]" : "[green]  (available)[/]";
            var displayText = $"[cyan1]{Markup.Escape(project.Name)}[/]\n" +
                            $"[grey70]  {project.TargetFramework}[/]{statusText}";

            _contextList?.AddItem(new ListItem(displayText) { Tag = project });
        }

        // Show package details on right
        ShowSearchPackageDetails(selectedPackage);

        // Set initial selection
        if (_contextList != null && _contextList.Items.Count > 0)
        {
            _contextList.SelectedIndex = 0;
        }

        // Update help bar
        _bottomHelpBar?.SetContent(new List<string> { GetHelpText() });

        // Focus the left list by default (indicators update automatically)
        _contextList?.SetFocus(true, FocusReason.Programmatic);
    }

    private void ShowSearchPackageDetails(NuGetPackage package)
    {
        var lines = new List<string>
        {
            $"[cyan1 bold]Package: {Markup.Escape(package.Id)}[/]",
            $"[grey70]Version: {package.Version}[/]",
            ""
        };

        if (!string.IsNullOrEmpty(package.Description))
        {
            lines.Add($"[grey70 bold]Description:[/]");
            lines.Add($"[grey70]{Markup.Escape(package.Description)}[/]");
            lines.Add("");
        }

        if (package.TotalDownloads > 0)
        {
            lines.Add($"[grey70]Downloads: {FormatDownloads(package.TotalDownloads)}[/]");
        }

        if (package.Published.HasValue)
        {
            lines.Add($"[grey70]Published: {package.Published.Value:yyyy-MM-dd}[/]");
        }

        if (!string.IsNullOrEmpty(package.ProjectUrl))
        {
            lines.Add($"[grey70]URL: {Markup.Escape(package.ProjectUrl)}[/]");
        }

        lines.Add("");
        lines.Add($"[{ColorScheme.PrimaryMarkup}]Select a project and press Enter to install[/]");

        UpdateDetailsContent(lines);
    }

    private async Task HandleInstallFromSearchAsync(NuGetPackage package)
    {
        // In search view, the list items are projects — get the selected project
        if (_contextList?.SelectedItem?.Tag is not ProjectInfo targetProject) return;
        if (_cliService == null) return;

        // Check if already installed
        var existing = targetProject.Packages.FirstOrDefault(p =>
            string.Equals(p.Id, package.Id, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            var replace = await ConfirmationModal.ShowAsync(_windowSystem,
                "Package Already Installed",
                $"{package.Id} {existing.Version} is already installed in {targetProject.Name}.\nReplace with {package.Version}?",
                parentWindow: _window);
            if (!replace) return;
        }
        else
        {
            var confirm = await ConfirmationModal.ShowAsync(_windowSystem,
                "Install Package",
                $"Install {package.Id} {package.Version} to {targetProject.Name}?",
                parentWindow: _window);
            if (!confirm) return;
        }

        // Show install progress modal with cancellation support
        var result = await OperationProgressModal.ShowAsync(
            _windowSystem,
            OperationType.Add,
            (ct, progress) => _cliService.AddPackageAsync(
                targetProject.FilePath, package.Id, package.Version, ct, progress),
            "Installing Package",
            $"Adding {package.Id} {package.Version} to {targetProject.Name}",
            _historyService,
            targetProject.FilePath,
            targetProject.Name,
            package.Id,
            package.Version,
            _window);

        // Refresh project after successful installation
        if (result.Success)
        {
            await ReloadProjectAsync(targetProject);
            SwitchToProjectsView();
        }
    }

    private async Task HandleUpdatePackageAsync(PackageReference package)
    {
        if (_selectedProject == null || _cliService == null) return;
        if (!package.IsOutdated || string.IsNullOrEmpty(package.LatestVersion)) return;

        var confirm = await ConfirmationModal.ShowAsync(_windowSystem,
            "Update Package",
            $"Update {package.Id} from {package.Version} to {package.LatestVersion}?",
            parentWindow: _window);
        if (!confirm) return;

        // Show update progress modal with cancellation support
        var result = await OperationProgressModal.ShowAsync(
            _windowSystem,
            OperationType.Update,
            (ct, progress) => _cliService.UpdatePackageAsync(
                _selectedProject.FilePath, package.Id, package.LatestVersion, ct, progress),
            "Updating Package",
            $"Updating {package.Id} to {package.LatestVersion}",
            _historyService,
            _selectedProject.FilePath,
            _selectedProject.Name,
            package.Id,
            package.LatestVersion,
            _window);

        // Refresh project after successful update
        if (result.Success)
        {
            await ReloadProjectAndRefreshView(_selectedProject);
        }
    }

    private async Task HandleRemovePackageAsync(PackageReference package)
    {
        if (_selectedProject == null || _cliService == null) return;

        var confirm = await ConfirmationModal.ShowAsync(_windowSystem,
            "Remove Package",
            $"Remove {package.Id} from {_selectedProject.Name}?",
            parentWindow: _window);
        if (!confirm) return;

        // Show remove progress modal with cancellation support
        var result = await OperationProgressModal.ShowAsync(
            _windowSystem,
            OperationType.Remove,
            (ct, progress) => _cliService.RemovePackageAsync(
                _selectedProject.FilePath, package.Id, ct, progress),
            "Removing Package",
            $"Removing {package.Id} from {_selectedProject.Name}",
            _historyService,
            _selectedProject.FilePath,
            _selectedProject.Name,
            package.Id,
            null,
            _window);

        // Refresh project after successful removal
        if (result.Success)
        {
            await ReloadProjectAndRefreshView(_selectedProject);
        }
    }

    private async Task HandleChangeVersionAsync(PackageReference package)
    {
        if (_selectedProject == null || _cliService == null || _nugetService == null) return;

        try
        {
            // Fetch available versions from NuGet
            var nugetData = await _nugetService.GetPackageDetailsAsync(package.Id);
            if (nugetData == null || !nugetData.Versions.Any())
            {
                _windowSystem.NotificationStateService.ShowNotification(
                    "No Versions Available",
                    $"Could not retrieve version list for {package.Id}",
                    NotificationSeverity.Warning,
                    timeout: 3000,
                    parentWindow: _window);
                return;
            }

            // Show version selector modal
            var selectedVersion = await VersionSelectorModal.ShowAsync(
                _windowSystem, package, nugetData.Versions, _window);

            if (string.IsNullOrEmpty(selectedVersion))
                return; // User cancelled

            // Check if same version
            if (string.Equals(selectedVersion, package.Version, StringComparison.OrdinalIgnoreCase))
            {
                _windowSystem.NotificationStateService.ShowNotification(
                    "Same Version",
                    $"{package.Id} is already at version {selectedVersion}",
                    NotificationSeverity.Info,
                    timeout: 3000,
                    parentWindow: _window);
                return;
            }

            // Confirm version change
            var action = string.Compare(selectedVersion, package.Version, StringComparison.OrdinalIgnoreCase) > 0
                ? "upgrade" : "downgrade";
            var confirm = await ConfirmationModal.ShowAsync(_windowSystem,
                $"Change Version",
                $"{action.ToUpper()} {package.Id} from {package.Version} to {selectedVersion}?",
                parentWindow: _window);
            if (!confirm) return;

            // Use OperationProgressModal for progress feedback and history recording
            var result = await OperationProgressModal.ShowAsync(
                _windowSystem,
                OperationType.Update,  // Changing version is an update operation
                (ct, progress) => _cliService.AddPackageAsync(
                    _selectedProject.FilePath, package.Id, selectedVersion, ct, progress),
                "Changing Package Version",
                $"Changing {package.Id} to version {selectedVersion}",
                _historyService,
                _selectedProject.FilePath,
                _selectedProject.Name,
                package.Id,
                selectedVersion,
                _window);

            if (result.Success)
            {
                await ReloadProjectAndRefreshView(_selectedProject);
            }
        }
        catch (Exception ex)
        {
            _windowSystem.LogService.LogError($"Error changing package version: {ex.Message}", ex, "Actions");
            await ErrorModal.ShowAsync(_windowSystem, "Error",
                "An error occurred while changing package version.", ex.Message, _window);
        }
    }

    private async Task HandleUpdateAllAsync(ProjectInfo project)
    {
        if (_cliService == null) return;

        var outdated = project.Packages.Where(p => p.IsOutdated && !string.IsNullOrEmpty(p.LatestVersion)).ToList();
        if (!outdated.Any()) return;

        var confirm = await ConfirmationModal.ShowAsync(_windowSystem,
            "Update All Packages",
            $"Update {outdated.Count} outdated package(s) in {project.Name}?",
            parentWindow: _window);
        if (!confirm) return;

        // Build package list for batch update
        var packages = outdated
            .Select(p => (p.Id, p.LatestVersion!))
            .ToList();

        // Show progress modal for batch operation
        var result = await OperationProgressModal.ShowAsync(
            _windowSystem,
            OperationType.Update,
            (ct, progress) => _cliService.UpdateAllPackagesAsync(
                project.FilePath, packages, ct, progress),
            "Updating All Packages",
            $"Updating {packages.Count} packages in {project.Name}",
            _historyService,
            project.FilePath,
            project.Name,
            null,  // No single package ID for batch operation
            null,  // No single version for batch operation
            _window);

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
        if (_cliService == null) return;

        // Show restore progress modal with cancellation support
        var result = await OperationProgressModal.ShowAsync(
            _windowSystem,
            OperationType.Restore,
            (ct, progress) => _cliService.RestorePackagesAsync(project.FilePath, ct, progress),
            "Restoring Packages",
            $"Restoring packages for {project.Name}",
            _historyService,
            project.FilePath,
            project.Name,
            null,
            null,
            _window);

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
            SwitchToPackagesView(refreshed);
        }
        else
        {
            SwitchToProjectsView();
        }
    }

    private static string FormatDownloads(long downloads)
    {
        if (downloads >= 1_000_000_000)
            return $"{downloads / 1_000_000_000.0:F1}B";
        if (downloads >= 1_000_000)
            return $"{downloads / 1_000_000.0:F1}M";
        if (downloads >= 1_000)
            return $"{downloads / 1_000.0:F1}K";
        return downloads.ToString();
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

        // Update header and help bar to reflect new content state
        UpdateRightPanelHeader();
        _bottomHelpBar?.SetContent(new List<string> { GetHelpText() });
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

        // Update header and help bar to reflect new content state
        UpdateRightPanelHeader();
        _bottomHelpBar?.SetContent(new List<string> { GetHelpText() });
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

    private string GetHelpText()
    {
        bool scrollable = IsRightPanelScrollable();
        string scrollHint = scrollable ? "[cyan1]PgUp/PgDn[/][grey70]:Scroll  [/]" : "";

        return _currentViewState switch
        {
            ViewState.Projects => $"[cyan1]↑↓[/][grey70]:Navigate  [/]{scrollHint}[cyan1]Enter[/][grey70]:View  [/][cyan1]Ctrl+S[/][grey70]:Search  [/][cyan1]Ctrl+H[/][grey70]:History  [/][cyan1]Ctrl+O[/][grey70]:Open  [/][cyan1]Ctrl+R[/][grey70]:Reload  [/][cyan1]Esc[/][grey70]:Exit[/]",
            ViewState.Packages => $"[cyan1]↑↓[/][grey70]:Navigate  [/]{scrollHint}[cyan1]Esc[/][grey70]:Back  [/][cyan1]Ctrl+U[/][grey70]:Update  [/][cyan1]Ctrl+X[/][grey70]:Remove  [/][cyan1]Ctrl+S[/][grey70]:Search  [/][cyan1]Ctrl+H[/][grey70]:History[/]",
            ViewState.Search => $"[cyan1]↑↓[/][grey70]:Navigate  [/]{scrollHint}[cyan1]Enter[/][grey70]:Install  [/][cyan1]Esc[/][grey70]:Cancel  [/][cyan1]Ctrl+H[/][grey70]:History[/]",
            _ => "[grey70]?:Help[/]"
        };
    }

    private void ShowLogViewer()
    {
        // Create fresh log viewer on demand (constructor builds UI and shows it)
        _logViewer = new LogViewerWindow(_windowSystem);
    }

    // UpdatePanelFocusIndicators method removed - panel title highlighting feature disabled

    private string GetStatusRightText()
    {
        var time = DateTime.Now.ToString("HH:mm:ss");

        return _currentViewState switch
        {
            ViewState.Projects =>
                $"{_projects.Count} projects · {_projects.Sum(p => p.Packages.Count)} pkgs · " +
                (_projects.Sum(p => p.OutdatedCount) is var outdated && outdated > 0
                    ? $"[yellow]{outdated} outdated[/] · "
                    : "") +
                time,
            ViewState.Packages when _selectedProject != null =>
                $"{_selectedProject.Packages.Count} pkgs · " +
                (_selectedProject.OutdatedCount > 0
                    ? $"[yellow]{_selectedProject.OutdatedCount} outdated[/] · "
                    : "[green]up-to-date[/] · ") +
                time,
            ViewState.Search =>
                $"{_searchResults.Count} selected · {_projects.Count} projects · " + time,
            _ => time
        };
    }

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
