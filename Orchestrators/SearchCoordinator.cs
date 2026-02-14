using SharpConsoleUI;
using SharpConsoleUI.Controls;
using LazyNuGet.Models;
using LazyNuGet.Services;
using LazyNuGet.UI.Modals;
using LazyNuGet.UI.Utilities;
using Spectre.Console;

namespace LazyNuGet.Orchestrators;

/// <summary>
/// Orchestrates search operations and search-related UI coordination.
/// Manages search modal, search results view, and installation from search.
/// </summary>
public class SearchCoordinator
{
    private readonly ConsoleWindowSystem _windowSystem;
    private readonly NuGetClientService _nugetService;
    private readonly DotNetCliService _cliService;
    private readonly OperationHistoryService _historyService;
    private readonly ErrorHandler _errorHandler;
    private readonly Window? _parentWindow;

    // Search state
    private List<NuGetPackage> _searchResults = new();
    private ViewState _preSearchViewState = ViewState.Projects;
    private int _preSearchSelectedIndex = 0;

    public SearchCoordinator(
        ConsoleWindowSystem windowSystem,
        NuGetClientService nugetService,
        DotNetCliService cliService,
        OperationHistoryService historyService,
        ErrorHandler errorHandler,
        Window? parentWindow)
    {
        _windowSystem = windowSystem;
        _nugetService = nugetService;
        _cliService = cliService;
        _historyService = historyService;
        _errorHandler = errorHandler;
        _parentWindow = parentWindow;
    }

    /// <summary>
    /// Show the search modal and return the selected package
    /// </summary>
    public async Task<NuGetPackage?> ShowSearchModalAsync()
    {
        return await SearchPackageModal.ShowAsync(_windowSystem, _nugetService, _parentWindow);
    }

    /// <summary>
    /// Save the current view state before switching to search results
    /// </summary>
    public void SavePreSearchState(ViewState currentState, int selectedIndex, ProjectInfo? selectedProject)
    {
        _preSearchViewState = currentState;
        _preSearchSelectedIndex = selectedIndex;
    }

    /// <summary>
    /// Get the saved pre-search view state
    /// </summary>
    public (ViewState viewState, int selectedIndex) GetPreSearchState()
    {
        return (_preSearchViewState, _preSearchSelectedIndex);
    }

    /// <summary>
    /// Save search results for display
    /// </summary>
    public void SetSearchResults(List<NuGetPackage> results)
    {
        _searchResults = results;
    }

    /// <summary>
    /// Get the current search results
    /// </summary>
    public List<NuGetPackage> GetSearchResults()
    {
        return _searchResults;
    }

    /// <summary>
    /// Handle installation of a package from search results
    /// </summary>
    public async Task<OperationResult> HandleInstallFromSearchAsync(
        NuGetPackage package,
        ProjectInfo targetProject)
    {
        // Enrich package with catalog data (dependencies, target frameworks, etc.)
        var source = _nugetService.Sources.FirstOrDefault(s => s.IsEnabled);
        if (source != null)
        {
            await _nugetService.EnrichPackageWithCatalogDataAsync(source, package);
        }

        // Check if already installed
        var existing = targetProject.Packages.FirstOrDefault(p =>
            string.Equals(p.Id, package.Id, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            var replace = await ConfirmInstallModal.ShowAsync(_windowSystem,
                package,
                targetProject.Name,
                $"{package.Id} {existing.Version} is already installed in {targetProject.Name}.\nReplace with {package.Version}?",
                targetProject.TargetFramework,
                parentWindow: _parentWindow);
            if (!replace) return new OperationResult { Success = false };
        }
        else
        {
            var confirm = await ConfirmInstallModal.ShowAsync(_windowSystem,
                package,
                targetProject.Name,
                $"Install {package.Id} {package.Version} to {targetProject.Name}?",
                targetProject.TargetFramework,
                parentWindow: _parentWindow);
            if (!confirm) return new OperationResult { Success = false };
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
            _parentWindow);

        return result;
    }

    /// <summary>
    /// Format package details for search results display
    /// </summary>
    public List<string> FormatSearchPackageDetails(NuGetPackage package)
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
        lines.Add($"[{ColorScheme.PrimaryMarkup}]Select a project from the list on the left[/]");

        return lines;
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
}
