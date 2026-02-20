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
    private readonly MarkupControl? _topStatusLeft;
    private readonly MarkupControl? _topStatusRight;
    private readonly MarkupControl? _bottomHelpBar;
    private readonly MarkupControl? _leftPanelHeader;
    private readonly MarkupControl? _rightPanelHeader;
    private readonly ScrollablePanelControl? _detailsPanel;
    private readonly HelpBar _helpBar;
    private readonly Action<string>? _onAction;

    private string _currentFolderPath;
    private List<ProjectInfo> _projects;

    public StatusBarManager(
        MarkupControl? topStatusLeft,
        MarkupControl? topStatusRight,
        MarkupControl? bottomHelpBar,
        MarkupControl? leftPanelHeader,
        MarkupControl? rightPanelHeader,
        ScrollablePanelControl? detailsPanel,
        string currentFolderPath,
        List<ProjectInfo> projects,
        Action<string>? onAction = null)
    {
        _topStatusLeft = topStatusLeft;
        _topStatusRight = topStatusRight;
        _bottomHelpBar = bottomHelpBar;
        _leftPanelHeader = leftPanelHeader;
        _rightPanelHeader = rightPanelHeader;
        _detailsPanel = detailsPanel;
        _currentFolderPath = currentFolderPath;
        _projects = projects;
        _onAction = onAction;
        _helpBar = new HelpBar(marginLeft: 1); // matches MarkupControl's left margin
    }

    /// <summary>
    /// Update the folder path for breadcrumb display
    /// </summary>
    public void SetFolderPath(string folderPath)
    {
        _currentFolderPath = folderPath;
    }

    /// <summary>
    /// Update the projects list for statistics
    /// </summary>
    public void SetProjects(List<ProjectInfo> projects)
    {
        _projects = projects;
    }

    /// <summary>
    /// Update breadcrumb for projects view
    /// </summary>
    public void UpdateBreadcrumbForProjects()
    {
        var folderName = Path.GetFileName(_currentFolderPath.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrEmpty(folderName)) folderName = _currentFolderPath;
        _topStatusLeft?.SetContent(new List<string> { $"[cyan1]{Markup.Escape(folderName)}[/] [grey50]({Markup.Escape(_currentFolderPath)})[/] [grey50](Esc: Exit)[/]" });
    }

    /// <summary>
    /// Update breadcrumb for packages view
    /// </summary>
    public void UpdateBreadcrumbForPackages(ProjectInfo project)
    {
        var projFolderName = Path.GetFileName(_currentFolderPath.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrEmpty(projFolderName)) projFolderName = _currentFolderPath;
        _topStatusLeft?.SetContent(new List<string> { $"[grey50]{Markup.Escape(projFolderName)}[/] [grey50]>[/] [cyan1]{Markup.Escape(project.Name)}[/] [grey50]> Packages[/] [grey50](Esc: Back)[/]" });
    }

    /// <summary>
    /// Update panel headers for projects view
    /// </summary>
    public void UpdateHeadersForProjects()
    {
        _leftPanelHeader?.SetContent(new List<string> { "[grey70]Projects[/]" });
        _rightPanelHeader?.SetContent(new List<string> { "[grey70]Dashboard[/]" });
    }

    /// <summary>
    /// Update panel headers for packages view
    /// </summary>
    public void UpdateHeadersForPackages(ProjectInfo project)
    {
        _leftPanelHeader?.SetContent(new List<string> { $"[grey70]{Markup.Escape(project.Name)} > Packages[/]" });
        UpdateRightPanelHeader("Details");
    }

    /// <summary>
    /// Update right panel header with scroll hint if needed
    /// </summary>
    private void UpdateRightPanelHeader(string title)
    {
        if (_rightPanelHeader == null) return;

        bool scrollable = IsRightPanelScrollable();

        if (scrollable)
        {
            _rightPanelHeader.SetContent(new List<string> { $"[grey70]{title}[/] [grey50](Ctrl+↑↓ to scroll)[/]" });
        }
        else
        {
            _rightPanelHeader.SetContent(new List<string> { $"[grey70]{title}[/]" });
        }
    }

    /// <summary>
    /// Update the help bar for the current view state
    /// </summary>
    public void UpdateHelpBar(ViewState viewState)
    {
        _bottomHelpBar?.SetContent(new List<string> { GetHelpText(viewState) });
    }

    /// <summary>
    /// Update the status right text (clock and statistics)
    /// </summary>
    public void UpdateStatusRight(ViewState viewState, ProjectInfo? selectedProject)
    {
        var stats = GetStatusRightText(viewState, selectedProject);
        _topStatusRight?.SetContent(new List<string> { $"[grey70]{stats}[/]" });
    }

    /// <summary>
    /// Check if right panel content is scrollable
    /// </summary>
    private bool IsRightPanelScrollable()
    {
        if (_detailsPanel == null) return false;
        return _detailsPanel.CanScrollDown || _detailsPanel.CanScrollUp;
    }

    /// <summary>
    /// Handle a mouse click on the help bar at the given X position.
    /// </summary>
    public bool HandleHelpBarClick(int x)
    {
        return _helpBar.HandleClick(x);
    }

    /// <summary>
    /// Build help bar items for the current view state and return rendered markup.
    /// </summary>
    private string GetHelpText(ViewState viewState)
    {
        _helpBar.Clear();

        switch (viewState)
        {
            case ViewState.Projects:
                _helpBar
                    .Add("↑↓", "Navigate")
                    .Add("Ctrl+↑↓", "Scroll")
                    .Add("Enter", "View")
                    .Add("Ctrl+O", "Open", () => _onAction?.Invoke("open"))
                    .Add("Ctrl+S", "Search", () => _onAction?.Invoke("search"))
                    .Add("Ctrl+H", "History", () => _onAction?.Invoke("history"))
                    .Add("Ctrl+P", "Settings", () => _onAction?.Invoke("settings"))
                    .Add("?", "Help", () => _onAction?.Invoke("help"))
                    .Add("Esc", "Exit", () => _onAction?.Invoke("exit"));
                break;
            case ViewState.Packages:
                _helpBar
                    .Add("↑↓", "Navigate")
                    .Add("Ctrl+↑↓", "Scroll")
                    .Add("F1-F4", "Tabs")
                    .Add("Ctrl+O", "Open", () => _onAction?.Invoke("open"))
                    .Add("Ctrl+S", "Search", () => _onAction?.Invoke("search"))
                    .Add("Ctrl+F", "Filter", () => _onAction?.Invoke("filter"))
                    .Add("?", "Help", () => _onAction?.Invoke("help"))
                    .Add("Esc", "Back", () => _onAction?.Invoke("back"));
                break;
            default:
                _helpBar.Add("?", "Help", () => _onAction?.Invoke("help"));
                break;
        }

        return _helpBar.Render();
    }

    /// <summary>
    /// Get status right text with context-aware statistics
    /// </summary>
    private string GetStatusRightText(ViewState viewState, ProjectInfo? selectedProject)
    {
        var time = DateTime.Now.ToString("HH:mm:ss");

        return viewState switch
        {
            ViewState.Projects =>
                $"{_projects.Count} projects | {_projects.Sum(p => p.Packages.Count)} pkgs | " +
                (_projects.Sum(p => p.OutdatedCount) is var outdated && outdated > 0
                    ? $"[yellow]{outdated} outdated[/] | "
                    : "") +
                time,
            ViewState.Packages when selectedProject != null =>
                $"{selectedProject.Packages.Count} pkgs | " +
                (selectedProject.OutdatedCount > 0
                    ? $"[yellow]{selectedProject.OutdatedCount} outdated[/] | "
                    : "[green]up-to-date[/] | ") +
                time,
            _ => time
        };
    }
}
