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

        // Framework compatibility check
        if (HasFrameworkIncompatibility(targetProject, package))
        {
            var tfDisplay = targetProject.TargetFrameworks.Any()
                ? string.Join(", ", targetProject.TargetFrameworks)
                : targetProject.TargetFramework;
            var proceed = await ConfirmationModal.ShowAsync(_windowSystem,
                "Framework Compatibility Warning",
                $"⚠ {package.Id} does not list {tfDisplay} as a supported framework.\n\nInstall anyway?",
                "Install Anyway",
                "Cancel",
                _parentWindow);
            if (!proceed) return new OperationResult { Success = false };
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
    /// Returns true when the package only lists legacy .NET Framework (net4x) target frameworks
    /// and the project targets .NET 5+, indicating likely incompatibility.
    /// </summary>
    private static bool HasFrameworkIncompatibility(ProjectInfo project, NuGetPackage package)
    {
        if (!package.TargetFrameworks.Any()) return false;

        // Check whether all package TFs are legacy net4x (no netstandard, netcoreapp, net5+)
        bool allLegacy = package.TargetFrameworks.All(tf =>
        {
            var lower = tf.ToLowerInvariant().TrimStart('.');
            return lower.StartsWith("net4") && !lower.StartsWith("net4.") ||
                   lower.StartsWith("net40") || lower.StartsWith("net45") ||
                   lower.StartsWith("net46") || lower.StartsWith("net47") ||
                   lower.StartsWith("net48");
        });

        if (!allLegacy) return false;

        // Check if the project targets net5+ (modern .NET)
        var projectTfs = project.TargetFrameworks.Any()
            ? project.TargetFrameworks
            : new List<string> { project.TargetFramework };

        return projectTfs.Any(tf =>
        {
            var lower = tf.ToLowerInvariant().TrimStart('.');
            if (!lower.StartsWith("net")) return false;
            // Modern .NET 5+ TFs always contain a dot (e.g. net9.0, net10.0).
            // Legacy .NET Framework TFs never contain a dot (e.g. net472, net48).
            if (!lower.Contains('.')) return false;
            var afterNet = lower.Substring(3);
            var dotIndex = afterNet.IndexOf('.');
            var numStr = dotIndex >= 0 ? afterNet.Substring(0, dotIndex) : afterNet;
            return int.TryParse(numStr, out var v) && v >= 5;
        });
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

                    var startTime = DateTime.Now;
                    var installResult = await _cliService.AddPackageAsync(
                        project.FilePath, package.Id, package.Version, ct, progress);
                    var duration = DateTime.Now - startTime;

                    // Record individual history entry for each project
                    _historyService.AddEntry(new OperationHistoryEntry
                    {
                        Type = OperationType.Add,
                        ProjectName = project.Name,
                        ProjectPath = project.FilePath,
                        PackageId = package.Id,
                        PackageVersion = package.Version,
                        Description = $"Install {package.Id} {package.Version} to {project.Name}",
                        Success = installResult.Success,
                        ErrorMessage = installResult.Success ? null : installResult.Message,
                        Duration = duration
                    });

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
            null, // Don't record at batch level - we're recording individually
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
