using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drawing;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Dialogs;
using Spectre.Console;
using LazyNuGet.Models;
using LazyNuGet.UI.Utilities;
using LazyNuGet.Services;
using LazyNuGet.UI.Components;
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
    private MenuControl? _menuControl;

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

    public LazyNuGetWindow(ConsoleWindowSystem windowSystem, string folderPath)
    {
        _windowSystem = windowSystem ?? throw new ArgumentNullException(nameof(windowSystem));
        _currentFolderPath = folderPath;

        // Initialize services
        _discoveryService = new ProjectDiscoveryService();
        _parserService = new ProjectParserService();
        _nugetService = new NuGetClientService(_windowSystem.LogService);

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

        // Top menu bar
        _menuControl = Controls.Menu()
            .Horizontal()
            .WithName("mainMenu")
            .Sticky()
            .AddItem("File", m => m
                .AddItem("Open Folder", () => _ = PromptForFolderAsync())
                .AddItem("Reload", () => _ = LoadProjectsAsync())
                .AddSeparator()
                .AddItem("Exit", () => _windowSystem.Shutdown()))
            .Build();
        _window.AddControl(_menuControl);

        _window.AddControl(Controls.RuleBuilder()
            .StickyTop()
            .WithColor(ColorScheme.RuleColor)
            .Build());

        // Top status bar
        _topStatusLeft = Controls.Markup($"[grey70]Folder: [/][cyan1]{Markup.Escape(_currentFolderPath)}[/]")
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
        _leftPanelHeader = Controls.Markup($"[{ColorScheme.PrimaryMarkup}]Projects[/]")
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

        _rightPanelHeader = Controls.Markup($"[{ColorScheme.PrimaryMarkup}]Dashboard[/]")
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
                // Update clock and stats
                if (_topStatusRight != null)
                {
                    var stats = $"{_projects.Count} projects | {_projects.Sum(p => p.Packages.Count)} packages | {DateTime.Now:HH:mm:ss}";
                    _topStatusRight.SetContent(new List<string> { $"[grey70]{stats}[/]" });
                }

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
                // TODO: Show search modal (Phase 4)
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
            // Escape - Navigate back
            else if (e.KeyInfo.Key == ConsoleKey.Escape)
            {
                HandleEscapeKey();
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
                _topStatusLeft?.SetContent(new List<string> { $"[grey70]Folder: [/][cyan1]{Markup.Escape(_currentFolderPath)}[/]" });
                await LoadProjectsAsync();
            }
        }
        catch (Exception ex)
        {
            // TODO: Show error modal (Phase 4)
            _windowSystem.LogService.LogError($"Error selecting folder: {ex.Message}", ex, "UI");
        }
    }

    private async Task LoadProjectsAsync()
    {
        if (_discoveryService == null || _parserService == null) return;

        try
        {
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
        _currentViewState = ViewState.Projects;
        _selectedProject = null;
        _selectedPackage = null;

        // Update headers
        _leftPanelHeader?.SetContent(new List<string> { $"[{ColorScheme.PrimaryMarkup}]Projects[/]" });
        _rightPanelHeader?.SetContent(new List<string> { $"[{ColorScheme.PrimaryMarkup}]Dashboard[/]" });

        // Update breadcrumb
        _topStatusLeft?.SetContent(new List<string> { $"[grey70]Folder: [/][cyan1]{Markup.Escape(_currentFolderPath)}[/]" });

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

        // Update details content
        UpdateDetailsContent(new List<string> { "[grey50]Select a project to view dashboard[/]" });
    }

    private void SwitchToPackagesView(ProjectInfo project)
    {
        _currentViewState = ViewState.Packages;
        _selectedProject = project;
        _selectedPackage = null;

        // Update headers
        _leftPanelHeader?.SetContent(new List<string> { $"[{ColorScheme.PrimaryMarkup}]{Markup.Escape(project.Name)} › Packages[/]" });
        _rightPanelHeader?.SetContent(new List<string> { $"[{ColorScheme.PrimaryMarkup}]Details[/]" });

        // Update breadcrumb
        _topStatusLeft?.SetContent(new List<string> { $"[cyan1]{Markup.Escape(project.Name)}[/] [grey70]› Packages[/]" });

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

        // Update details content
        UpdateDetailsContent(new List<string> { "[grey50]Select a package to view details[/]" });
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
                // TODO: Show package details (Phase 3)
                break;

            case ViewState.Search:
                // TODO: Install package (Phase 4)
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
                // Already at top level, do nothing
                break;
        }
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
                // TODO: Show package details from search (Phase 4)
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
        // TODO: Phase 4 - Implement update all functionality
        _windowSystem.LogService.LogInfo($"Update all packages requested for {project.Name}", "Actions");
    }

    private void HandleRestore(ProjectInfo project)
    {
        // TODO: Phase 4 - Implement restore functionality
        _windowSystem.LogService.LogInfo($"Restore packages requested for {project.Name}", "Actions");
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
        // TODO: Phase 4 - Implement update functionality
        _windowSystem.LogService.LogInfo($"Update package requested: {package.Id}", "Actions");
    }

    private void HandleRemovePackage(PackageReference package)
    {
        // TODO: Phase 4 - Implement remove functionality
        _windowSystem.LogService.LogInfo($"Remove package requested: {package.Id}", "Actions");
    }

    private void UpdateDetailsContent(List<string> lines)
    {
        if (_detailsContent != null)
        {
            var builder = Controls.Markup();
            foreach (var line in lines)
            {
                builder.AddLine(line);
            }
            var newContent = builder.WithMargin(1, 1, 1, 1).Build();

            // Replace the old content with new content
            if (_detailsPanel != null)
            {
                _detailsPanel.RemoveControl(_detailsContent);
                _detailsContent = newContent;
                _detailsPanel.AddControl(_detailsContent);
            }
        }
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
    }

    private string GetHelpText()
    {
        return _currentViewState switch
        {
            ViewState.Projects => "[grey70]Enter:View Packages  Ctrl+S:Search  Ctrl+O:Open Folder  Ctrl+R:Reload[/]",
            ViewState.Packages => "[grey70]Esc:Back  Ctrl+U:Update  Ctrl+X:Remove  Ctrl+S:Search[/]",
            ViewState.Search => "[grey70]Esc:Cancel  Enter:Install  ↑↓:Navigate[/]",
            _ => "[grey70]?:Help[/]"
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
