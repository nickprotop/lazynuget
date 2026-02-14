using SharpConsoleUI.Controls;
using Spectre.Console;
using LazyNuGet.Models;
using LazyNuGet.UI.Utilities;

namespace LazyNuGet.Orchestrators;

/// <summary>
/// Manages status bar updates, breadcrumbs, and help text.
/// Centralizes all status display logic.
/// </summary>
public class StatusBarManager
{
    private readonly MarkupControl? _topStatusLeft;
    private readonly MarkupControl? _topStatusRight;
    private readonly MarkupControl? _bottomHelpBar;
    private readonly MarkupControl? _leftPanelHeader;
    private readonly MarkupControl? _rightPanelHeader;
    private readonly ScrollablePanelControl? _detailsPanel;

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
        List<ProjectInfo> projects)
    {
        _topStatusLeft = topStatusLeft;
        _topStatusRight = topStatusRight;
        _bottomHelpBar = bottomHelpBar;
        _leftPanelHeader = leftPanelHeader;
        _rightPanelHeader = rightPanelHeader;
        _detailsPanel = detailsPanel;
        _currentFolderPath = currentFolderPath;
        _projects = projects;
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
        _topStatusLeft?.SetContent(new List<string> { $"[cyan1]{Markup.Escape(folderName)}[/] [grey50]({Markup.Escape(_currentFolderPath)})[/]" });
    }

    /// <summary>
    /// Update breadcrumb for packages view
    /// </summary>
    public void UpdateBreadcrumbForPackages(ProjectInfo project)
    {
        var projFolderName = Path.GetFileName(_currentFolderPath.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrEmpty(projFolderName)) projFolderName = _currentFolderPath;
        _topStatusLeft?.SetContent(new List<string> { $"[grey50]{Markup.Escape(projFolderName)}[/] [grey50]›[/] [cyan1]{Markup.Escape(project.Name)}[/] [grey50]› Packages[/]" });
    }

    /// <summary>
    /// Update breadcrumb for search view
    /// </summary>
    public void UpdateBreadcrumbForSearch(string packageId)
    {
        var searchFolderName = Path.GetFileName(_currentFolderPath.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrEmpty(searchFolderName)) searchFolderName = _currentFolderPath;
        _topStatusLeft?.SetContent(new List<string> { $"[grey50]{Markup.Escape(searchFolderName)}[/] [grey50]›[/] [cyan1]Search[/] [grey50]›[/] [{ColorScheme.InfoMarkup}]{Markup.Escape(packageId)}[/]" });
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
        _leftPanelHeader?.SetContent(new List<string> { $"[grey70]{Markup.Escape(project.Name)} › Packages[/]" });
        UpdateRightPanelHeader("Details");
    }

    /// <summary>
    /// Update panel headers for search view
    /// </summary>
    public void UpdateHeadersForSearch()
    {
        _leftPanelHeader?.SetContent(new List<string> { "[grey70]Install Package[/]" });
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
            _rightPanelHeader.SetContent(new List<string> { $"[grey70]{title}[/] [grey50](PgUp/PgDn to scroll)[/]" });
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
    public void UpdateStatusRight(ViewState viewState, ProjectInfo? selectedProject, List<NuGetPackage> searchResults)
    {
        var stats = GetStatusRightText(viewState, selectedProject, searchResults);
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
    /// Get help text for the current view state
    /// </summary>
    private string GetHelpText(ViewState viewState)
    {
        bool scrollable = IsRightPanelScrollable();
        string scrollHint = scrollable ? "[cyan1]PgUp/PgDn[/][grey70]:Scroll  [/]" : "";

        return viewState switch
        {
            ViewState.Projects => $"[cyan1]↑↓[/][grey70]:Navigate  [/]{scrollHint}[cyan1]Enter[/][grey70]:View  [/][cyan1]Ctrl+S[/][grey70]:Search  [/][cyan1]Ctrl+P[/][grey70]:Settings  [/][cyan1]Ctrl+H[/][grey70]:History  [/][cyan1]Ctrl+O[/][grey70]:Open  [/][cyan1]Ctrl+R[/][grey70]:Reload  [/][cyan1]Esc[/][grey70]:Exit[/]",
            ViewState.Packages => $"[cyan1]↑↓[/][grey70]:Navigate  [/]{scrollHint}[cyan1]Ctrl+T[/][grey70]:Tabs  [/][cyan1]Ctrl+S[/][grey70]:Search  [/][cyan1]Ctrl+F[/][grey70]:Filter  [/][cyan1]Esc[/][grey70]:Back[/]",
            ViewState.Search => $"[cyan1]↑↓[/][grey70]:Navigate  [/]{scrollHint}[cyan1]I[/][grey70]:Install  [/][cyan1]Esc[/][grey70]:Cancel  [/][cyan1]Ctrl+P[/][grey70]:Settings  [/][cyan1]Ctrl+H[/][grey70]:History[/]",
            _ => "[grey70]?:Help[/]"
        };
    }

    /// <summary>
    /// Get status right text with context-aware statistics
    /// </summary>
    private string GetStatusRightText(ViewState viewState, ProjectInfo? selectedProject, List<NuGetPackage> searchResults)
    {
        var time = DateTime.Now.ToString("HH:mm:ss");

        return viewState switch
        {
            ViewState.Projects =>
                $"{_projects.Count} projects · {_projects.Sum(p => p.Packages.Count)} pkgs · " +
                (_projects.Sum(p => p.OutdatedCount) is var outdated && outdated > 0
                    ? $"[yellow]{outdated} outdated[/] · "
                    : "") +
                time,
            ViewState.Packages when selectedProject != null =>
                $"{selectedProject.Packages.Count} pkgs · " +
                (selectedProject.OutdatedCount > 0
                    ? $"[yellow]{selectedProject.OutdatedCount} outdated[/] · "
                    : "[green]up-to-date[/] · ") +
                time,
            ViewState.Search =>
                $"{searchResults.Count} selected · {_projects.Count} projects · " + time,
            _ => time
        };
    }
}
