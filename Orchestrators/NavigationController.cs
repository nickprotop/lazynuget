using SharpConsoleUI;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;
using Spectre.Console;
using LazyNuGet.Models;
using LazyNuGet.Services;
using LazyNuGet.UI.Components;

namespace LazyNuGet.Orchestrators;

/// <summary>
/// Manages view state transitions, selection handling, and project/package navigation.
/// Extracted from LazyNuGetWindow to reduce god-class complexity.
/// </summary>
public class NavigationController
{
    private readonly ListControl? _contextList;
    private readonly MarkupControl? _leftPanelHeader;
    private readonly StatusBarManager? _statusBarManager;
    private readonly PackageFilterController? _filterController;
    private readonly PackageDetailsController? _packageDetailsController;
    private readonly ErrorHandler? _errorHandler;
    private readonly Window? _window;
    private readonly Func<List<ProjectInfo>> _getProjects;
    private readonly Action<List<string>> _updateDetailsContent;
    private readonly Action<List<IWindowControl>> _updateDetailsPanel;
    private readonly Func<ProjectInfo, Task> _handleUpdateAllAsync;
    private readonly Func<ProjectInfo, Task> _handleRestoreAsync;
    private readonly Func<ProjectInfo, PackageReference?, Task> _showDependencyTreeAsync;
    private readonly Func<Task> _confirmExitAsync;
    private readonly OperationOrchestrator? _operationOrchestrator;
    private readonly Func<ProjectInfo, Task>? _handleMigrateProjectAsync;
    private readonly Func<ProjectInfo, Task>? _reloadAndRefreshAsync;

    private ViewState _currentViewState = ViewState.Projects;
    private ProjectInfo? _selectedProject;

    public ViewState CurrentViewState => _currentViewState;
    public ProjectInfo? SelectedProject
    {
        get => _selectedProject;
        set => _selectedProject = value;
    }

    public NavigationController(
        ListControl? contextList,
        MarkupControl? leftPanelHeader,
        StatusBarManager? statusBarManager,
        PackageFilterController? filterController,
        PackageDetailsController? packageDetailsController,
        ErrorHandler? errorHandler,
        Window? window,
        Func<List<ProjectInfo>> getProjects,
        Action<List<string>> updateDetailsContent,
        Action<List<IWindowControl>> updateDetailsPanel,
        Func<ProjectInfo, Task> handleUpdateAllAsync,
        Func<ProjectInfo, Task> handleRestoreAsync,
        Func<ProjectInfo, PackageReference?, Task> showDependencyTreeAsync,
        Func<Task> confirmExitAsync,
        OperationOrchestrator? operationOrchestrator = null,
        Func<ProjectInfo, Task>? handleMigrateProjectAsync = null,
        Func<ProjectInfo, Task>? reloadAndRefreshAsync = null)
    {
        _contextList = contextList;
        _leftPanelHeader = leftPanelHeader;
        _statusBarManager = statusBarManager;
        _filterController = filterController;
        _packageDetailsController = packageDetailsController;
        _errorHandler = errorHandler;
        _window = window;
        _getProjects = getProjects;
        _updateDetailsContent = updateDetailsContent;
        _updateDetailsPanel = updateDetailsPanel;
        _handleUpdateAllAsync = handleUpdateAllAsync;
        _handleRestoreAsync = handleRestoreAsync;
        _showDependencyTreeAsync = showDependencyTreeAsync;
        _confirmExitAsync = confirmExitAsync;
        _operationOrchestrator = operationOrchestrator;
        _handleMigrateProjectAsync = handleMigrateProjectAsync;
        _reloadAndRefreshAsync = reloadAndRefreshAsync;
    }

    public void SwitchToProjectsView()
    {
        var projects = _getProjects();

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
        _filterController?.ResetFilter();

        // Update panel titles and breadcrumb via StatusBarManager
        _statusBarManager?.UpdateHeadersForProjects();
        _statusBarManager?.UpdateBreadcrumbForProjects();

        // Populate project list (grouped by solution)
        _contextList?.ClearItems();
        PopulateProjectsList(projects);

        // Update help bar via StatusBarManager
        _statusBarManager?.UpdateHelpBar(_currentViewState);

        // Restore previous selection or default to first selectable item
        if (_contextList != null && _contextList.Items.Count > 0)
        {
            var restoreIndex = FindFirstSelectableIndex();
            if (previousProject != null)
            {
                for (int i = 0; i < _contextList.Items.Count; i++)
                {
                    if (_contextList.Items[i].Tag is ProjectInfo p && p.FilePath == previousProject.FilePath)
                    {
                        restoreIndex = i;
                        break;
                    }
                }
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
            _updateDetailsContent(new List<string> { "[grey50]No projects found[/]" });
        }

        // Focus the left list by default (indicators update automatically)
        _contextList?.SetFocus(true, FocusReason.Programmatic);
    }

    public void RefreshCurrentView()
    {
        if (_contextList == null) return;

        var selectedIndex = _contextList.SelectedIndex;

        switch (_currentViewState)
        {
            case ViewState.Projects:
                var projects = _getProjects();
                // Rebuild project list items (display text includes outdated counts)
                _contextList.ClearItems();
                PopulateProjectsList(projects);

                // Restore selection
                if (selectedIndex >= 0 && selectedIndex < _contextList.Items.Count)
                    _contextList.SelectedIndex = selectedIndex;
                break;

            case ViewState.Packages:
                // Rebuild package list items (display text includes version status)
                var packages = _filterController?.GetFilteredPackages();
                if (packages != null)
                {
                    PopulatePackagesList(packages);
                    if (selectedIndex >= 0 && selectedIndex < _contextList.Items.Count)
                        _contextList.SelectedIndex = selectedIndex;
                }
                break;
        }

        // Rebuild the right panel for current selection
        HandleSelectionChanged();
    }

    public void SwitchToPackagesView(ProjectInfo project, PackageReference? initialPackage = null)
    {
        _currentViewState = ViewState.Packages;
        _selectedProject = project;

        // Reset filter state and set packages
        _filterController?.ResetFilter();
        _filterController?.SetPackages(_selectedProject.Packages);

        // Update panel titles and breadcrumb via StatusBarManager
        _statusBarManager?.UpdateHeadersForPackages(project);
        _statusBarManager?.UpdateBreadcrumbForPackages(project);

        // Show installed packages from the project
        var allPackages = _filterController?.AllInstalledPackages ?? _selectedProject.Packages;
        PopulatePackagesList(allPackages);

        // Update left panel header
        UpdateLeftPanelHeader($"Packages ({allPackages.Count})");

        // Update help bar via StatusBarManager (show migration hint for legacy projects)
        _statusBarManager?.UpdateHelpBar(_currentViewState, project.IsPackagesConfig);

        // Determine which index to select
        int indexToSelect = 0;
        if (initialPackage != null && _contextList != null)
        {
            for (int i = 0; i < _contextList.Items.Count; i++)
            {
                if (_contextList.Items[i].Tag is PackageReference pkg &&
                    pkg.Id.Equals(initialPackage.Id, StringComparison.OrdinalIgnoreCase))
                {
                    indexToSelect = i;
                    break;
                }
            }
        }

        // Trigger selection to show package details
        if (_contextList != null && _contextList.Items.Count > 0)
        {
            var wasAlready = _contextList.SelectedIndex == indexToSelect;
            _contextList.SelectedIndex = indexToSelect;
            // If already at the target index, event won't fire, so call manually
            if (wasAlready)
            {
                HandleSelectionChanged();
            }
        }
        else
        {
            _updateDetailsContent(new List<string> { "[grey50]No packages in this project[/]" });
        }

        // Focus the left list by default (indicators update automatically)
        _contextList?.SetFocus(true, FocusReason.Programmatic);
    }

    public void RefreshInstalledPackages()
    {
        if (_selectedProject == null) return;

        // Show installed packages from current project
        var allPackages = _filterController?.AllInstalledPackages ?? new List<PackageReference>();
        PopulatePackagesList(allPackages);
        UpdateLeftPanelHeader($"Packages ({allPackages.Count})");

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
            _updateDetailsContent(new List<string> { "[grey50]No packages to display[/]" });
        }
    }

    private void PopulateProjectsList(List<ProjectInfo> projects)
    {
        // Group by SolutionName (null = orphan)
        var grouped = projects
            .GroupBy(p => p.SolutionName)
            .OrderBy(g => g.Key == null ? 1 : 0)  // solutions first, orphans last
            .ThenBy(g => g.Key ?? "(no solution)")
            .ToList();

        var needHeaders = grouped.Count > 1 || grouped.Any(g => g.Key != null);

        foreach (var group in grouped)
        {
            if (needHeaders)
            {
                var headerName = group.Key ?? "(no solution)";
                var header = new ListItem($"[grey50]── {Markup.Escape(headerName)} ──[/]");
                header.Tag = null; // Non-selectable sentinel
                _contextList?.AddItem(header);
            }

            foreach (var project in group)
            {
                var tfDisplay = project.TargetFrameworks.Any()
                    ? string.Join(" | ", project.TargetFrameworks)
                    : project.TargetFramework;
                var legacyBadge = project.IsPackagesConfig ? "[grey50]legacy[/] " : string.Empty;
                var displayText = $"{legacyBadge}[cyan1]{Markup.Escape(project.Name)}[/]\n" +
                                $"[grey70]  📦 {project.Packages.Count} packages · {Markup.Escape(tfDisplay)}[/]";

                if (project.IsPackagesConfig)
                    displayText += $"\n[grey50]  packages.config — press Ctrl+M to migrate[/]";
                else if (project.OutdatedCount > 0)
                    displayText += $"\n[yellow]  ⚠ {project.OutdatedCount} outdated[/]";
                else
                    displayText += $"\n[green]  ✓ All up-to-date[/]";

                _contextList?.AddItem(new ListItem(displayText) { Tag = project });
            }
        }
    }

    public void PopulatePackagesList(IEnumerable<PackageReference> packages)
    {
        _contextList?.ClearItems();
        foreach (var package in packages)
        {
            var displayText = $"[cyan1]{Markup.Escape(package.Id)}[/]\n" +
                            $"[grey70]  {package.DisplayStatus}[/]";

            _contextList?.AddItem(new ListItem(displayText) { Tag = package });
        }
    }

    public void HandleEnterKey()
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

    public void HandleMigrateProjectKey()
    {
        if (_currentViewState != ViewState.Packages || _selectedProject == null) return;
        if (!_selectedProject.IsPackagesConfig) return;

        if (_handleMigrateProjectAsync != null)
        {
            AsyncHelper.FireAndForget(
                () => _handleMigrateProjectAsync(_selectedProject),
                ex => _errorHandler?.HandleAsync(ex, ErrorSeverity.Warning, "Migration Error", "Failed to migrate project.", "Migration", _window));
        }
    }

    public void HandleEscapeKey()
    {
        // If in filter mode, exit filter mode first
        if (_filterController?.IsFilterMode == true)
        {
            _filterController.ExitFilterMode();
            return;
        }

        switch (_currentViewState)
        {
            case ViewState.Packages:
                SwitchToProjectsView();
                break;

            case ViewState.Projects:
                AsyncHelper.FireAndForget(
                    () => _confirmExitAsync(),
                    ex => _errorHandler?.HandleAsync(ex, ErrorSeverity.Info, "Exit Error", "Failed to confirm exit.", "UI", _window));
                break;
        }
    }

    public void HandleSelectionChanged()
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
                    _packageDetailsController?.ShowPackageDetails(package);
                }
                break;
        }
    }

    private int FindFirstSelectableIndex()
    {
        if (_contextList == null) return 0;
        for (int i = 0; i < _contextList.Items.Count; i++)
        {
            if (_contextList.Items[i].Tag is ProjectInfo)
                return i;
        }
        return 0;
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

    private void ShowProjectDashboard(ProjectInfo project)
    {
        if (project.IsPackagesConfig)
        {
            var legacyControls = InteractiveDashboardBuilder.BuildLegacyDashboard(
                project,
                onViewPackages: () => SwitchToPackagesView(project),
                onMigrate: _handleMigrateProjectAsync == null ? null : () => AsyncHelper.FireAndForget(
                    () => _handleMigrateProjectAsync(project),
                    ex => _errorHandler?.HandleAsync(ex, ErrorSeverity.Warning, "Migration Error", "Failed to migrate project.", "Migration", _window)));
            _updateDetailsPanel(legacyControls);
            return;
        }

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
            onUpdateSelected: packages => AsyncHelper.FireAndForget(async () =>
                {
                    var result = await (_operationOrchestrator?.HandleUpdateSelectedAsync(packages, project)
                        ?? Task.FromResult(new OperationResult { Success = false }));
                    if (result.Success && _reloadAndRefreshAsync != null)
                        await _reloadAndRefreshAsync(project);
                },
                ex => _errorHandler?.HandleAsync(ex, ErrorSeverity.Warning, "Update Selected Error", "Failed to update selected packages.", "NuGet", _window)),
            onRemoveSelected: packages => AsyncHelper.FireAndForget(async () =>
                {
                    var result = await (_operationOrchestrator?.HandleRemoveSelectedAsync(packages, project)
                        ?? Task.FromResult(new OperationResult { Success = false }));
                    if (result.Success && _reloadAndRefreshAsync != null)
                        await _reloadAndRefreshAsync(project);
                },
                ex => _errorHandler?.HandleAsync(ex, ErrorSeverity.Warning, "Remove Selected Error", "Failed to remove selected packages.", "NuGet", _window)),
            onDeps: () => AsyncHelper.FireAndForget(
                () => _showDependencyTreeAsync(project, null),
                ex => _errorHandler?.HandleAsync(ex, ErrorSeverity.Info, "Dependency Error", "Failed to show dependencies.", "UI", _window)),
            onOpenPackage: pkg => SwitchToPackagesView(project, pkg));

        _updateDetailsPanel(controls);
    }

    private void HandleUpdateAll(ProjectInfo project)
    {
        AsyncHelper.FireAndForget(
            () => _handleUpdateAllAsync(project),
            ex => _errorHandler?.HandleAsync(ex, ErrorSeverity.Warning, "Update All Error", "Failed to update packages.", "NuGet", _window));
    }

    private void HandleRestore(ProjectInfo project)
    {
        AsyncHelper.FireAndForget(
            () => _handleRestoreAsync(project),
            ex => _errorHandler?.HandleAsync(ex, ErrorSeverity.Warning, "Restore Error", "Failed to restore packages.", "NuGet", _window));
    }
}
