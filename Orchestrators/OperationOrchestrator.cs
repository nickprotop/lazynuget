using SharpConsoleUI;
using SharpConsoleUI.Core;
using LazyNuGet.Models;
using LazyNuGet.Services;
using LazyNuGet.UI.Modals;

namespace LazyNuGet.Orchestrators;

/// <summary>
/// Orchestrates package operation workflows (install, update, remove, restore).
/// Manages confirmation dialogs, progress modals, and operation history.
/// </summary>
public class OperationOrchestrator
{
    private readonly ConsoleWindowSystem _windowSystem;
    private readonly DotNetCliService _cliService;
    private readonly NuGetClientService _nugetService;
    private readonly OperationHistoryService _historyService;
    private readonly ErrorHandler _errorHandler;
    private readonly Window? _parentWindow;

    public OperationOrchestrator(
        ConsoleWindowSystem windowSystem,
        DotNetCliService cliService,
        NuGetClientService nugetService,
        OperationHistoryService historyService,
        ErrorHandler errorHandler,
        Window? parentWindow)
    {
        _windowSystem = windowSystem;
        _cliService = cliService;
        _nugetService = nugetService;
        _historyService = historyService;
        _errorHandler = errorHandler;
        _parentWindow = parentWindow;
    }

    /// <summary>
    /// Handle updating a single package to its latest version
    /// </summary>
    public async Task<OperationResult> HandleUpdatePackageAsync(
        PackageReference package,
        ProjectInfo project)
    {
        if (!package.IsOutdated || string.IsNullOrEmpty(package.LatestVersion))
            return new OperationResult { Success = false };

        var confirm = await ConfirmationModal.ShowAsync(_windowSystem,
            "Update Package",
            $"Update {package.Id} from {package.Version} to {package.LatestVersion}?",
            parentWindow: _parentWindow);
        if (!confirm) return new OperationResult { Success = false };

        // Show update progress modal with cancellation support
        var result = await OperationProgressModal.ShowAsync(
            _windowSystem,
            OperationType.Update,
            (ct, progress) => _cliService.UpdatePackageAsync(
                project.FilePath, package.Id, package.LatestVersion, ct, progress),
            "Updating Package",
            $"Updating {package.Id} to {package.LatestVersion}",
            _historyService,
            project.FilePath,
            project.Name,
            package.Id,
            package.LatestVersion,
            _parentWindow);

        return result;
    }

    /// <summary>
    /// Handle removing a package from a project
    /// </summary>
    public async Task<OperationResult> HandleRemovePackageAsync(
        PackageReference package,
        ProjectInfo project)
    {
        var confirm = await ConfirmationModal.ShowAsync(_windowSystem,
            "Remove Package",
            $"Remove {package.Id} from {project.Name}?",
            parentWindow: _parentWindow);
        if (!confirm) return new OperationResult { Success = false };

        // Show remove progress modal with cancellation support
        var result = await OperationProgressModal.ShowAsync(
            _windowSystem,
            OperationType.Remove,
            (ct, progress) => _cliService.RemovePackageAsync(
                project.FilePath, package.Id, ct, progress),
            "Removing Package",
            $"Removing {package.Id} from {project.Name}",
            _historyService,
            project.FilePath,
            project.Name,
            package.Id,
            null,
            _parentWindow);

        return result;
    }

    /// <summary>
    /// Handle changing a package to a specific version
    /// </summary>
    public async Task<OperationResult> HandleChangeVersionAsync(
        PackageReference package,
        ProjectInfo project)
    {
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
                    parentWindow: _parentWindow);
                return new OperationResult { Success = false };
            }

            // Show version selector modal
            var selectedVersion = await VersionSelectorModal.ShowAsync(
                _windowSystem, package, nugetData.Versions, _parentWindow);

            if (string.IsNullOrEmpty(selectedVersion))
                return new OperationResult { Success = false }; // User cancelled

            // Check if same version
            if (string.Equals(selectedVersion, package.Version, StringComparison.OrdinalIgnoreCase))
            {
                _windowSystem.NotificationStateService.ShowNotification(
                    "Same Version",
                    $"{package.Id} is already at version {selectedVersion}",
                    NotificationSeverity.Info,
                    timeout: 3000,
                    parentWindow: _parentWindow);
                return new OperationResult { Success = false };
            }

            // Confirm version change with vulnerability warnings
            var action = string.Compare(selectedVersion, package.Version, StringComparison.OrdinalIgnoreCase) > 0
                ? "upgrade" : "downgrade";

            // Update nugetData version to selected version for vulnerability check
            var packageForConfirmation = new NuGetPackage
            {
                Id = nugetData.Id,
                Version = selectedVersion,
                Description = nugetData.Description,
                VulnerabilityCount = nugetData.VulnerabilityCount,
                IsDeprecated = nugetData.IsDeprecated,
                DeprecationMessage = nugetData.DeprecationMessage,
                Dependencies = nugetData.Dependencies,
                TargetFrameworks = nugetData.TargetFrameworks
            };

            var confirm = await ConfirmInstallModal.ShowAsync(_windowSystem,
                packageForConfirmation,
                project.Name,
                $"{action.ToUpper()} {package.Id} from {package.Version} to {selectedVersion}?",
                project.TargetFramework,
                parentWindow: _parentWindow);
            if (!confirm) return new OperationResult { Success = false };

            // Use OperationProgressModal for progress feedback and history recording
            var result = await OperationProgressModal.ShowAsync(
                _windowSystem,
                OperationType.Update,  // Changing version is an update operation
                (ct, progress) => _cliService.AddPackageAsync(
                    project.FilePath, package.Id, selectedVersion, ct, progress),
                "Changing Package Version",
                $"Changing {package.Id} to version {selectedVersion}",
                _historyService,
                project.FilePath,
                project.Name,
                package.Id,
                selectedVersion,
                _parentWindow);

            return result;
        }
        catch (Exception ex)
        {
            await (_errorHandler?.HandleAsync(ex, ErrorSeverity.Critical,
                "Version Change Error", "An error occurred while changing package version.", "Actions", _parentWindow)
                ?? Task.CompletedTask);
            return new OperationResult { Success = false };
        }
    }

    /// <summary>
    /// Handle updating all outdated packages in a project
    /// </summary>
    public async Task<OperationResult> HandleUpdateAllAsync(ProjectInfo project)
    {
        var outdated = project.Packages.Where(p => p.IsOutdated && !string.IsNullOrEmpty(p.LatestVersion)).ToList();
        if (!outdated.Any())
            return new OperationResult { Success = false };

        // Show update strategy modal
        var strategy = await UpdateStrategyModal.ShowAsync(
            _windowSystem,
            outdated.Count,
            project.Name,
            _parentWindow);

        if (strategy == null)
            return new OperationResult { Success = false }; // User cancelled

        // Filter packages based on strategy
        var packagesToUpdate = outdated
            .Where(p => VersionComparisonService.IsUpdateAllowed(
                p.Version,
                p.LatestVersion!,
                strategy.Value))
            .ToList();

        // Handle no matches
        if (!packagesToUpdate.Any())
        {
            _windowSystem.NotificationStateService.ShowNotification(
                "No Updates Available",
                $"No packages match the selected update strategy ({strategy.Value.GetDisplayName()})",
                NotificationSeverity.Info,
                timeout: 3000,
                parentWindow: _parentWindow);
            return new OperationResult { Success = false };
        }

        // Update confirmation message
        var confirmMessage = packagesToUpdate.Count == outdated.Count
            ? $"Update {packagesToUpdate.Count} outdated package(s) in {project.Name}?"
            : $"Update {packagesToUpdate.Count} of {outdated.Count} outdated package(s) in {project.Name}?\n({strategy.Value.GetDisplayName()})";

        var confirm = await ConfirmationModal.ShowAsync(_windowSystem,
            "Update All Packages",
            confirmMessage,
            parentWindow: _parentWindow);
        if (!confirm) return new OperationResult { Success = false };

        // Build package list for batch update
        var packages = packagesToUpdate
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
            _parentWindow);

        return result;
    }

    /// <summary>
    /// Handle restoring packages for a project
    /// </summary>
    public async Task<OperationResult> HandleRestoreAsync(ProjectInfo project)
    {
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
            _parentWindow);

        return result;
    }

    /// <summary>
    /// Show operation history modal
    /// </summary>
    public async Task ShowOperationHistoryAsync()
    {
        await OperationHistoryModal.ShowAsync(_windowSystem, _historyService, _cliService, _parentWindow);
    }
}
