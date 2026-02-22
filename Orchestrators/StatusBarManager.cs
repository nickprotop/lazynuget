using SharpConsoleUI.Controls;
using Spectre.Console;
using LazyNuGet.Models;
using LazyNuGet.UI.Utilities;

namespace LazyNuGet.Orchestrators;

/// <summary>
/// Manages status bar updates, breadcrumbs, and help text.
/// Centralizes all status display logic for Projects and Packages views.
/// </summary>
public class StatusBarManager
{
    private readonly MarkupControl?          _topStatusLeft;
    private readonly MarkupControl?          _topStatusRight;
    private readonly MarkupControl?          _bottomHelpBar;
    private readonly MarkupControl?          _leftPanelHeader;
    private readonly MarkupControl?          _rightPanelHeader;
    private readonly ScrollablePanelControl? _detailsPanel;
    private readonly HelpBar                 _helpBar;
    private readonly StatusBar               _statusBar = new();
    private readonly Action<string>?         _onAction;
    private readonly Func<bool>?             _isCacheWarm;
    private readonly Action?                 _onRefresh;

    private string           _currentFolderPath;
    private List<ProjectInfo> _projects;

    public StatusBarManager(
        MarkupControl?          topStatusLeft,
        MarkupControl?          topStatusRight,
        MarkupControl?          bottomHelpBar,
        MarkupControl?          leftPanelHeader,
        MarkupControl?          rightPanelHeader,
        ScrollablePanelControl? detailsPanel,
        string                  currentFolderPath,
        List<ProjectInfo>       projects,
        Action<string>?         onAction    = null,
        Func<bool>?             isCacheWarm = null,
        Action?                 onRefresh   = null)
    {
        _topStatusLeft     = topStatusLeft;
        _topStatusRight    = topStatusRight;
        _bottomHelpBar     = bottomHelpBar;
        _leftPanelHeader   = leftPanelHeader;
        _rightPanelHeader  = rightPanelHeader;
        _detailsPanel      = detailsPanel;
        _currentFolderPath = currentFolderPath;
        _projects          = projects;
        _onAction          = onAction;
        _isCacheWarm       = isCacheWarm;
        _onRefresh         = onRefresh;
        _helpBar           = new HelpBar(marginLeft: 1); // matches MarkupControl's left margin
    }

    // ── Folder / project data ─────────────────────────────────────────────────

    public void SetFolderPath(string folderPath)  => _currentFolderPath = folderPath;
    public void SetProjects(List<ProjectInfo> projects) => _projects = projects;

    // ── Breadcrumbs ───────────────────────────────────────────────────────────

    public void UpdateBreadcrumbForProjects()
    {
        var folderName = Path.GetFileName(_currentFolderPath.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrEmpty(folderName)) folderName = _currentFolderPath;
        _topStatusLeft?.SetContent(new List<string>
        {
            $"[cyan1]{Markup.Escape(folderName)}[/] [grey50]({Markup.Escape(_currentFolderPath)})[/] [grey50](Esc: Exit)[/]"
        });
    }

    public void UpdateBreadcrumbForPackages(ProjectInfo project)
    {
        var folderName = Path.GetFileName(_currentFolderPath.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrEmpty(folderName)) folderName = _currentFolderPath;
        var cpmBadge    = project.IsCpmEnabled      ? " [grey50][CPM][/]"    : string.Empty;
        var legacyBadge = project.IsPackagesConfig  ? " [grey50][legacy][/]" : string.Empty;
        _topStatusLeft?.SetContent(new List<string>
        {
            $"[grey50]{Markup.Escape(folderName)}[/] [grey50]>[/] [cyan1]{Markup.Escape(project.Name)}[/]{cpmBadge}{legacyBadge} [grey50]> Packages[/] [grey50](Esc: Back)[/]"
        });
    }

    // ── Panel headers ─────────────────────────────────────────────────────────

    public void UpdateHeadersForProjects()
    {
        _leftPanelHeader?.SetContent(new List<string> { "[grey70]Projects[/]" });
        _rightPanelHeader?.SetContent(new List<string> { "[grey70]Dashboard[/]" });
    }

    public void UpdateHeadersForPackages(ProjectInfo project)
    {
        _leftPanelHeader?.SetContent(new List<string>
        {
            $"[grey70]{Markup.Escape(project.Name)} > Packages[/]"
        });
        UpdateRightPanelHeader("Details");
    }

    private void UpdateRightPanelHeader(string title)
    {
        if (_rightPanelHeader == null) return;
        var content = IsRightPanelScrollable()
            ? $"[grey70]{title}[/] [grey50](Ctrl+↑↓ to scroll)[/]"
            : $"[grey70]{title}[/]";
        _rightPanelHeader.SetContent(new List<string> { content });
    }

    private bool IsRightPanelScrollable() =>
        _detailsPanel != null && (_detailsPanel.CanScrollDown || _detailsPanel.CanScrollUp);

    // ── Help bar (bottom) ─────────────────────────────────────────────────────

    public void UpdateHelpBar(ViewState viewState, bool isLegacyProject = false)
    {
        _bottomHelpBar?.SetContent(new List<string> { BuildHelpBar(viewState, isLegacyProject) });
    }

    public bool HandleHelpBarClick(int x) => _helpBar.HandleClick(x);

    private string BuildHelpBar(ViewState viewState, bool isLegacyProject = false)
    {
        _helpBar.Clear();

        switch (viewState)
        {
            case ViewState.Projects:
                var hasMigratable = _projects.Any(p =>
                    !p.IsPackagesConfig &&
                    p.Packages.Any(pkg => pkg.VersionSource == VersionSource.Inline));
                _helpBar
                    .Add("↑↓",      "Navigate")
                    .Add("Ctrl+↑↓", "Scroll")
                    .Add("Enter",   "View")
                    .Add("Ctrl+O",  "Open",     () => _onAction?.Invoke("open"))
                    .Add("Ctrl+S",  "Search",   () => _onAction?.Invoke("search"));
                if (hasMigratable)
                    _helpBar.Add("Ctrl+G", "CPM", () => _onAction?.Invoke("migrate-cpm"));
                _helpBar
                    .Add("Ctrl+H",  "History",  () => _onAction?.Invoke("history"))
                    .Add("Ctrl+P",  "Settings", () => _onAction?.Invoke("settings"))
                    .Add("?",       "Help",     () => _onAction?.Invoke("help"))
                    .Add("Esc",     "Exit",     () => _onAction?.Invoke("exit"));
                break;
            case ViewState.Packages when isLegacyProject:
                _helpBar
                    .Add("↑↓",      "Navigate")
                    .Add("Ctrl+↑↓", "Scroll")
                    .Add("Ctrl+M",  "Migrate",  () => _onAction?.Invoke("migrate"))
                    .Add("Ctrl+O",  "Open",     () => _onAction?.Invoke("open"))
                    .Add("?",       "Help",     () => _onAction?.Invoke("help"))
                    .Add("Esc",     "Back",     () => _onAction?.Invoke("back"));
                break;
            case ViewState.Packages:
                _helpBar
                    .Add("↑↓",      "Navigate")
                    .Add("Ctrl+↑↓", "Scroll")
                    .Add("F1-F4",   "Tabs")
                    .Add("Ctrl+O",  "Open",   () => _onAction?.Invoke("open"))
                    .Add("Ctrl+S",  "Search", () => _onAction?.Invoke("search"))
                    .Add("Ctrl+F",  "Filter", () => _onAction?.Invoke("filter"))
                    .Add("?",       "Help",   () => _onAction?.Invoke("help"))
                    .Add("Esc",     "Back",   () => _onAction?.Invoke("back"));
                break;
            default:
                _helpBar.Add("?", "Help", () => _onAction?.Invoke("help"));
                break;
        }

        return _helpBar.Render();
    }

    // ── Status bar (top-right) ────────────────────────────────────────────────

    /// <summary>
    /// Rebuild the top-right status bar for the current view and push it to the control.
    /// </summary>
    public void UpdateStatusRight(ViewState viewState, ProjectInfo? selectedProject)
    {
        BuildStatusBar(viewState, selectedProject);
        _topStatusRight?.SetContent(new List<string> { _statusBar.Render() });
    }

    /// <summary>
    /// Map a raw mouse X click on the top-right control to a hint action.
    /// <paramref name="windowWidth"/> should be the current terminal/window column count.
    /// </summary>
    public bool HandleStatusBarClick(int x, int windowWidth) =>
        _statusBar.HandleClick(x, windowWidth, rightMargin: 1);

    private void BuildStatusBar(ViewState viewState, ProjectInfo? selectedProject)
    {
        _statusBar.Clear();

        switch (viewState)
        {
            case ViewState.Projects:
            {
                var pkgCount = _projects.Sum(p => p.Packages.Count);
                _statusBar.AddSegment(
                    $"[grey70]{_projects.Count} projects | {pkgCount} pkgs | [/]",
                    $"{_projects.Count} projects | {pkgCount} pkgs | ");

                var outdated = _projects.Sum(p => p.OutdatedCount);
                if (outdated > 0)
                    _statusBar.AddSegment(
                        $"[yellow]{outdated} outdated[/][grey70] | [/]",
                        $"{outdated} outdated | ");
                break;
            }

            case ViewState.Packages when selectedProject != null:
            {
                _statusBar.AddSegment(
                    $"[grey70]{selectedProject.Packages.Count} pkgs | [/]",
                    $"{selectedProject.Packages.Count} pkgs | ");

                if (selectedProject.OutdatedCount > 0)
                    _statusBar.AddSegment(
                        $"[yellow]{selectedProject.OutdatedCount} outdated[/][grey70] | [/]",
                        $"{selectedProject.OutdatedCount} outdated | ");
                else
                    _statusBar.AddSegment(
                        "[green]up-to-date[/][grey70] | [/]",
                        "up-to-date | ");
                break;
            }
        }

        var time = DateTime.Now.ToString("HH:mm:ss");
        _statusBar.AddSegment($"[grey70]{time}[/]", time);

        if (_isCacheWarm?.Invoke() == true)
            _statusBar
                .AddSegment("[grey70]  [/]", "  ")   // separator before hint
                .AddHint("^R", "Refresh", _onRefresh);
    }
}
