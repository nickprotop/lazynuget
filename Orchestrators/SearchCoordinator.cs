using SharpConsoleUI;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;
using LazyNuGet.Models;
using LazyNuGet.Services;
using LazyNuGet.UI.Modals;
using LazyNuGet.UI.Utilities;
using Spectre.Console;

namespace LazyNuGet.Orchestrators;

/// <summary>
/// Orchestrates search operations and installation workflows.
/// Manages search modal, install planning modal, and batch installation.
/// </summary>
public class SearchCoordinator
{
    private readonly ConsoleWindowSystem _windowSystem;
    private readonly NuGetClientService _nugetService;
    private readonly DotNetCliService _cliService;
    private readonly OperationHistoryService _historyService;
    private readonly ErrorHandler _errorHandler;
    private readonly Window? _parentWindow;

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
    /// Show the install planning modal for a package across all projects.
    /// Returns the list of selected projects or null if cancelled.
    /// </summary>
    /// <param name="currentProject">The currently selected project (if in single project view), or null if in all projects view</param>
    public async Task<List<ProjectInfo>?> ShowInstallPlanningModalAsync(
        NuGetPackage package,
        List<ProjectInfo> projects,
        ProjectInfo? currentProject = null)
    {
        // Enrich package with catalog data (dependencies, target frameworks, etc.)
        var source = _nugetService.Sources.FirstOrDefault(s => s.IsEnabled);
        if (source != null)
        {
            await _nugetService.EnrichPackageWithCatalogDataAsync(source, package);
        }

        return await InstallPlanningModal.ShowAsync(_windowSystem, package, projects, currentProject, _parentWindow);
    }

    /// <summary>
    /// Handle installation of a package from search results to a single project
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

        if (result.Success)
        {
            _windowSystem.NotificationStateService.ShowNotification(
                "Install Successful",
                $"{package.Id} {package.Version} installed to {targetProject.Name}",
                NotificationSeverity.Success,
                timeout: 3000,
                parentWindow: _parentWindow);
        }

        return result;
    }

    /// <summary>
    /// Batch install a package to multiple projects, showing a single progress modal.
    /// </summary>
    public async Task<OperationResult> BatchInstallAsync(
        NuGetPackage package,
        List<ProjectInfo> targetProjects)
    {
        if (targetProjects.Count == 0)
            return new OperationResult { Success = false, Message = "No projects selected" };

        var description = targetProjects.Count == 1
            ? $"Installing {package.Id} {package.Version} to {targetProjects[0].Name}"
            : $"Installing {package.Id} {package.Version} to {targetProjects.Count} projects";

        var result = await OperationProgressModal.ShowAsync(
            _windowSystem,
            OperationType.Add,
            async (ct, progress) =>
            {
                var successes = 0;
                var failures = new List<string>();

                for (int i = 0; i < targetProjects.Count; i++)
                {
                    var project = targetProjects[i];
                    progress.Report($"[{i + 1}/{targetProjects.Count}] Installing to {project.Name}...");

                    var installResult = await _cliService.AddPackageAsync(
                        project.FilePath, package.Id, package.Version, ct, progress);

                    if (installResult.Success)
                    {
                        successes++;
                        progress.Report($"Installed to {project.Name} successfully");
                    }
                    else
                    {
                        failures.Add($"{project.Name}: {installResult.Message}");
                        progress.Report($"Failed to install to {project.Name}: {installResult.Message}");
                    }
                }

                if (failures.Count == 0)
                {
                    return OperationResult.FromSuccess(
                        $"Successfully installed {package.Id} to {successes} project{(successes != 1 ? "s" : "")}");
                }
                else if (successes > 0)
                {
                    return new OperationResult
                    {
                        Success = true,
                        Message = $"Installed to {successes} of {targetProjects.Count} projects. {failures.Count} failed.",
                        ErrorDetails = string.Join("\n", failures)
                    };
                }
                else
                {
                    return OperationResult.FromError(
                        "All installations failed",
                        string.Join("\n", failures));
                }
            },
            "Installing Package",
            description,
            _historyService,
            null,
            null,
            package.Id,
            package.Version,
            _parentWindow);

        if (result.Success)
        {
            var projectNames = string.Join(", ", targetProjects.Select(p => p.Name));
            _windowSystem.NotificationStateService.ShowNotification(
                "Install Successful",
                $"{package.Id} {package.Version} installed to {targetProjects.Count} project{(targetProjects.Count != 1 ? "s" : "")}",
                NotificationSeverity.Success,
                timeout: 3000,
                parentWindow: _parentWindow);
        }

        return result;
    }
}
