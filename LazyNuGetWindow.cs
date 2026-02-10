using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drawing;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Core;
using SharpConsoleUI.Dialogs;
using Spectre.Console;
using LazyNuGet.Models;
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
    private List<IWindowControl> _currentDetailControls = new();  // Track all controls in details panel


    // State
    private string _currentFolderPath;
    private List<ProjectInfo> _projects = new();
    private ProjectInfo? _selectedProject;
    private PackageReference? _selectedPackage;
    private ViewState _currentViewState = ViewState.Projects;
    private List<NuGetPackage> _searchResults = new();

    // Services
    private ProjectDiscoveryService? _discoveryService;
    private ProjectParserService? _parserService;
    private NuGetClientService? _nugetService;
    private DotNetCliService? _cliService;
    private ConfigurationService? _configService;

    public LazyNuGetWindow(ConsoleWindowSystem windowSystem, string folderPath, ConfigurationService? configService = null)
    {
        _windowSystem = windowSystem ?? throw new ArgumentNullException(nameof(windowSystem));
        _currentFolderPath = folderPath;
        _configService = configService;

        // Initialize services
        _discoveryService = new ProjectDiscoveryService();
        _parserService = new ProjectParserService();
        _nugetService = new NuGetClientService(_windowSystem.LogService);
        _cliService = new DotNetCliService(_windowSystem.LogService);

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
        _leftPanelHeader = Controls.Markup($"[{ColorScheme.PrimaryMarkup} bold]▸ Projects[/]")
            .WithMargin(1, 0, 0, 0)
            .WithBackgroundColor(ColorScheme.WindowBackground)
            .Build();

        _contextList = Controls.List()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithColors(ColorScheme.SidebarBackground, Color.Grey93)
            .WithFocusedColors(ColorScheme.SidebarBackground, Color.Grey93)
            .WithHighlightColors(Color.Grey35, Color.White)
            .SimpleMode()
            .Build();

        _rightPanelHeader = Controls.Markup("[grey50]Dashboard[/]")
            .WithMargin(1, 0, 0, 0)
            .WithBackgroundColor(ColorScheme.DetailsPanelBackground)
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

                // Keep panel focus indicators in sync
                UpdatePanelFocusIndicators();

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

        _window.KeyPressed += (sender, e) =>
        {
            // Up/Down arrows redirect to left panel list when it doesn't have focus.
            // Must be checked BEFORE AlreadyHandled — the dispatcher uses arrows for
            // focus traversal, which sets AlreadyHandled and would block us.
            // BUT: if the details panel or any interactive control inside it has focus,
            // the user is scrolling/navigating the right panel — don't steal the arrows.
            if (e.KeyInfo.Key == ConsoleKey.UpArrow && _contextList != null && !_contextList.HasFocus
                && !DetailsAreaHasFocus() && _contextList.Items.Count > 0)
            {
                if (_contextList.SelectedIndex > 0)
                    _contextList.SelectedIndex--;
                e.Handled = true;
                return;
            }
            if (e.KeyInfo.Key == ConsoleKey.DownArrow && _contextList != null && !_contextList.HasFocus
                && !DetailsAreaHasFocus() && _contextList.Items.Count > 0)
            {
                if (_contextList.SelectedIndex < _contextList.Items.Count - 1)
                    _contextList.SelectedIndex++;
                e.Handled = true;
                return;
            }

            // Left/Right arrows switch focus between the left list and right details panel.
            // Must also be before AlreadyHandled for the same reason as Up/Down.
            if (e.KeyInfo.Key == ConsoleKey.LeftArrow && !(_contextList?.HasFocus == true))
            {
                _contextList?.SetFocus(true, FocusReason.Programmatic);
                UpdatePanelFocusIndicators();
                e.Handled = true;
                return;
            }
            if (e.KeyInfo.Key == ConsoleKey.RightArrow && !DetailsAreaHasFocus())
            {
                _detailsPanel?.SetFocus(true, FocusReason.Programmatic);
                UpdatePanelFocusIndicators();
                e.Handled = true;
                return;
            }

            // Escape must run BEFORE AlreadyHandled check — the dispatcher
            // consumes the first Escape to unfocus controls, setting AlreadyHandled.
            // We want Escape to always mean "navigate back / exit".
            if (e.KeyInfo.Key == ConsoleKey.Escape)
            {
                HandleEscapeKey();
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
            // Enter - Navigate forward
            else if (e.KeyInfo.Key == ConsoleKey.Enter)
            {
                HandleEnterKey();
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
            // Ctrl+X - Remove package
            else if (e.KeyInfo.Key == ConsoleKey.X && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                if (_currentViewState == ViewState.Packages && _contextList?.SelectedItem?.Tag is PackageReference pkgToRemove)
                {
                    _ = HandleRemovePackageAsync(pkgToRemove);
                }
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
            // Check each project's packages for updates
            foreach (var project in _projects)
            {
                foreach (var package in project.Packages)
                {
                    try
                    {
                        var (isOutdated, latestVersion) = await _nugetService.CheckIfOutdatedAsync(
                            package.Id,
                            package.Version);

                        package.LatestVersion = latestVersion;

                        // Update the UI if we're still in the projects view
                        if (_currentViewState == ViewState.Projects)
                        {
                            // Refresh the project list to show updated counts
                            var currentSelection = _contextList?.SelectedIndex ?? 0;
                            SwitchToProjectsView();
                            if (_contextList != null && currentSelection >= 0 && currentSelection < _projects.Count)
                            {
                                _contextList.SelectedIndex = currentSelection;
                            }
                        }
                    }
                    catch
                    {
                        // Continue checking other packages even if one fails
                    }

                    // Small delay to avoid overwhelming the API
                    await Task.Delay(100);
                }
            }
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
        _selectedPackage = null;

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
            _contextList.SelectedIndex = restoreIndex;
            HandleSelectionChanged();
        }
        else
        {
            UpdateDetailsContent(new List<string> { "[grey50]No projects found[/]" });
        }

        // Focus the left list by default
        _contextList?.SetFocus(true, FocusReason.Programmatic);
        UpdatePanelFocusIndicators();
    }

    private void SwitchToPackagesView(ProjectInfo project)
    {
        _currentViewState = ViewState.Packages;
        _selectedProject = project;
        _selectedPackage = null;

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
            _contextList.SelectedIndex = 0;
            HandleSelectionChanged();
        }
        else
        {
            UpdateDetailsContent(new List<string> { "[grey50]No packages in this project[/]" });
        }

        // Focus the left list by default
        _contextList?.SetFocus(true, FocusReason.Programmatic);
        UpdatePanelFocusIndicators();
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
                SwitchToProjectsView();
                break;

            case ViewState.Projects:
                _ = ConfirmExitAsync();
                break;
        }
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

    private void SwitchToSearchResultsView(NuGetPackage selectedPackage)
    {
        _currentViewState = ViewState.Search;
        _searchResults = new List<NuGetPackage> { selectedPackage };

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

        // Focus the left list by default
        _contextList?.SetFocus(true, FocusReason.Programmatic);
        UpdatePanelFocusIndicators();
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

        var notifId = _windowSystem.NotificationStateService.ShowNotification(
            "Installing Package", $"Adding {Markup.Escape(package.Id)} to {Markup.Escape(targetProject.Name)}...",
            NotificationSeverity.Info, timeout: 0, parentWindow: _window);

        var result = await _cliService.AddPackageAsync(targetProject.FilePath, package.Id, package.Version);

        _windowSystem.NotificationStateService.DismissNotification(notifId);

        if (result.Success)
        {
            await ReloadProjectAsync(targetProject);
            SwitchToProjectsView();
            _windowSystem.NotificationStateService.ShowNotification(
                "Package Installed",
                $"{package.Id} {package.Version} added to {targetProject.Name}",
                NotificationSeverity.Success,
                timeout: 3000,
                parentWindow: _window);
        }
        else
        {
            await ErrorModal.ShowAsync(_windowSystem, "Install Failed",
                $"Failed to install {package.Id}.", result.ErrorDetails, _window);
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

        var notifId = _windowSystem.NotificationStateService.ShowNotification(
            "Updating Package", $"Updating {Markup.Escape(package.Id)} to {package.LatestVersion}...",
            NotificationSeverity.Info, timeout: 0, parentWindow: _window);

        var result = await _cliService.UpdatePackageAsync(
            _selectedProject.FilePath, package.Id, package.LatestVersion);

        _windowSystem.NotificationStateService.DismissNotification(notifId);

        if (result.Success)
        {
            await ReloadProjectAndRefreshView(_selectedProject);
            _windowSystem.NotificationStateService.ShowNotification(
                "Package Updated",
                $"{package.Id} updated to {package.LatestVersion}",
                NotificationSeverity.Success,
                timeout: 3000,
                parentWindow: _window);
        }
        else
        {
            await ErrorModal.ShowAsync(_windowSystem, "Update Failed",
                $"Failed to update {package.Id}.", result.ErrorDetails, _window);
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

        var notifId = _windowSystem.NotificationStateService.ShowNotification(
            "Removing Package", $"Removing {Markup.Escape(package.Id)}...",
            NotificationSeverity.Info, timeout: 0, parentWindow: _window);

        var result = await _cliService.RemovePackageAsync(_selectedProject.FilePath, package.Id);

        _windowSystem.NotificationStateService.DismissNotification(notifId);

        if (result.Success)
        {
            await ReloadProjectAndRefreshView(_selectedProject);
            _windowSystem.NotificationStateService.ShowNotification(
                "Package Removed",
                $"{package.Id} removed from {_selectedProject.Name}",
                NotificationSeverity.Success,
                timeout: 3000,
                parentWindow: _window);
        }
        else
        {
            await ErrorModal.ShowAsync(_windowSystem, "Remove Failed",
                $"Failed to remove {package.Id}.", result.ErrorDetails, _window);
        }
    }

    private async Task HandleUpdateAllAsync(ProjectInfo project)
    {
        if (_cliService == null) return;

        var outdated = project.Packages.Where(p => p.IsOutdated).ToList();
        if (!outdated.Any()) return;

        var confirm = await ConfirmationModal.ShowAsync(_windowSystem,
            "Update All Packages",
            $"Update {outdated.Count} outdated package(s) in {project.Name}?",
            parentWindow: _window);
        if (!confirm) return;

        // Build a progress panel with a determinate progress bar
        var progressLabel = Controls.Markup()
            .AddLine($"[{ColorScheme.PrimaryMarkup}]Updating packages...[/]")
            .AddLine($"[grey70]0/{outdated.Count} completed[/]")
            .WithMargin(1, 1, 1, 0)
            .Build();

        var progressBar = Controls.ProgressBar()
            .WithMaxValue(outdated.Count)
            .WithValue(0)
            .ShowPercentage(true)
            .WithMargin(1, 0, 1, 1)
            .Build();

        UpdateDetailsPanel(new List<IWindowControl> { progressLabel, progressBar });

        var failures = new List<string>();
        for (int i = 0; i < outdated.Count; i++)
        {
            var pkg = outdated[i];
            if (string.IsNullOrEmpty(pkg.LatestVersion)) continue;

            progressLabel.SetContent(new List<string>
            {
                $"[{ColorScheme.PrimaryMarkup}]Updating packages...[/]",
                $"[grey70]{i}/{outdated.Count} completed[/]",
                $"[grey50]Current: {Markup.Escape(pkg.Id)}[/]"
            });
            progressBar.Value = i;

            var result = await _cliService.UpdatePackageAsync(
                project.FilePath, pkg.Id, pkg.LatestVersion);

            if (!result.Success)
            {
                failures.Add($"{pkg.Id}: {result.ErrorDetails ?? result.Message}");
            }
        }

        progressLabel.SetContent(new List<string>
        {
            $"[{ColorScheme.PrimaryMarkup}]Updating packages...[/]",
            $"[grey70]{outdated.Count}/{outdated.Count} completed[/]"
        });
        progressBar.Value = outdated.Count;

        await ReloadProjectAsync(project);

        if (failures.Any())
        {
            await ErrorModal.ShowAsync(_windowSystem, "Some Updates Failed",
                $"{failures.Count} of {outdated.Count} updates failed.",
                string.Join("\n\n", failures), _window);
        }
        else
        {
            _windowSystem.NotificationStateService.ShowNotification(
                "All Packages Updated",
                $"{outdated.Count} package(s) updated in {project.Name}",
                NotificationSeverity.Success,
                timeout: 3000,
                parentWindow: _window);
        }

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

    private async Task HandleRestoreAsync(ProjectInfo project)
    {
        if (_cliService == null) return;

        var notifId = _windowSystem.NotificationStateService.ShowNotification(
            "Restoring Packages", $"Restoring packages for {Markup.Escape(project.Name)}...",
            NotificationSeverity.Info, timeout: 0, parentWindow: _window);

        var result = await _cliService.RestorePackagesAsync(project.FilePath);

        _windowSystem.NotificationStateService.DismissNotification(notifId);

        if (result.Success)
        {
            _windowSystem.NotificationStateService.ShowNotification(
                "Packages Restored",
                $"Packages restored for {project.Name}",
                NotificationSeverity.Success,
                timeout: 3000,
                parentWindow: _window);
        }
        else
        {
            await ErrorModal.ShowAsync(_windowSystem, "Restore Failed",
                $"Failed to restore packages for {project.Name}.", result.ErrorDetails, _window);
        }
    }

    private async Task ReloadProjectAsync(ProjectInfo project)
    {
        if (_parserService == null) return;

        var updated = await _parserService.ParseProjectAsync(project.FilePath);
        if (updated == null) return;

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

        // Clear any multi-control content from UpdateDetailsPanel
        foreach (var control in _currentDetailControls)
        {
            _detailsPanel.RemoveControl(control);
        }
        _currentDetailControls.Clear();

        // Also remove single-content control if it wasn't in the list
        if (_detailsContent != null)
        {
            _detailsPanel.RemoveControl(_detailsContent);
        }

        // Build and add new content
        var builder = Controls.Markup();
        foreach (var line in lines) builder.AddLine(line);
        _detailsContent = builder.WithMargin(1, 1, 1, 1).Build();
        _detailsPanel.AddControl(_detailsContent);
        _currentDetailControls.Add(_detailsContent);
        _detailsPanel.ScrollToTop();
    }

    private void UpdateDetailsPanel(List<IWindowControl> controls)
    {
        if (_detailsPanel == null) return;

        // Remove all previously added controls
        foreach (var control in _currentDetailControls)
        {
            _detailsPanel.RemoveControl(control);
        }
        _currentDetailControls.Clear();

        // Add all new controls
        foreach (var control in controls)
        {
            _detailsPanel.AddControl(control);
            _currentDetailControls.Add(control);
        }

        // Keep track of the first control as _detailsContent for backward compatibility
        _detailsContent = controls.FirstOrDefault() as MarkupControl;
        _detailsPanel.ScrollToTop();
    }

    private string GetHelpText()
    {
        return _currentViewState switch
        {
            ViewState.Projects => "[grey70]←→:Panel  Enter:View Packages  Ctrl+S:Search  Ctrl+O:Open Folder  Ctrl+R:Reload  Esc:Exit[/]",
            ViewState.Packages => "[grey70]←→:Panel  Esc:Back  Ctrl+U:Update  Ctrl+X:Remove  Ctrl+S:Search[/]",
            ViewState.Search => "[grey70]←→:Panel  Esc:Cancel  Enter:Install  ↑↓:Navigate[/]",
            _ => "[grey70]?:Help[/]"
        };
    }

    /// <summary>
    /// Updates the left and right panel headers to indicate which panel is focused.
    /// Focused panel gets a highlighted title, unfocused panel gets a dimmed title.
    /// </summary>
    private void UpdatePanelFocusIndicators()
    {
        bool rightFocused = DetailsAreaHasFocus();
        bool leftFocused = _contextList?.HasFocus == true;

        // Compute titles based on current view state
        var leftTitle = _currentViewState switch
        {
            ViewState.Projects => "Projects",
            ViewState.Packages when _selectedProject != null => $"{Markup.Escape(_selectedProject.Name)} › Packages",
            ViewState.Search => "Install Package",
            _ => "Projects"
        };
        var rightTitle = _currentViewState switch
        {
            ViewState.Projects => "Dashboard",
            _ => "Details"
        };

        // Style: focused = bold primary color with arrow indicator, unfocused = dimmed
        var leftStyled = leftFocused
            ? $"[{ColorScheme.PrimaryMarkup} bold]▸ {leftTitle}[/]"
            : $"[grey50]{leftTitle}[/]";
        var rightStyled = rightFocused
            ? $"[{ColorScheme.PrimaryMarkup} bold]▸ {rightTitle}[/]"
            : $"[grey50]{rightTitle}[/]";

        _leftPanelHeader?.SetContent(new List<string> { leftStyled });
        _rightPanelHeader?.SetContent(new List<string> { rightStyled });
    }

    /// <summary>
    /// Returns true if the details panel or any interactive control inside it has focus.
    /// Used to avoid redirecting arrow keys to the context list while the user scrolls
    /// or navigates buttons in the right panel.
    /// </summary>
    private bool DetailsAreaHasFocus()
    {
        if (_detailsPanel is IInteractiveControl panelCtrl && panelCtrl.HasFocus)
            return true;
        foreach (var ctrl in _currentDetailControls)
        {
            if (ctrl is IInteractiveControl interactive && interactive.HasFocus)
                return true;
        }
        return false;
    }

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
