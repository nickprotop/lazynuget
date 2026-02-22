using SharpConsoleUI;
using SharpConsoleUI.Core;
using LazyNuGet.Models;
using LazyNuGet.Repositories;
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
    private readonly CpmRepository _cpmRepository = new();

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

        var cpmNote = package.VersionSource == VersionSource.Central
            ? "\n\n[CPM] This version is shared centrally — all projects using this package will be updated."
            : string.Empty;

        var confirm = await ConfirmationModal.ShowAsync(_windowSystem,
            "Update Package",
            $"Update {package.Id} from {package.Version} to {package.LatestVersion}?{cpmNote}",
            parentWindow: _parentWindow);
        if (!confirm) return new OperationResult { Success = false };

        // For centrally-managed packages, update Directory.Packages.props directly.
        Func<CancellationToken, IProgress<string>, Task<OperationResult>> operation;
        string description;

        if (package.VersionSource == VersionSource.Central && !string.IsNullOrEmpty(package.PropsFilePath))
        {
            var propsPath = package.PropsFilePath;
            var latestVersion = package.LatestVersion!;
            operation = async (ct, progress) =>
            {
                progress.Report($"Updating {package.Id} to {latestVersion} in Directory.Packages.props...");
                await _cpmRepository.UpdatePackageVersionAsync(propsPath, package.Id, latestVersion);
                progress.Report($"Updated {package.Id} to {latestVersion}");
                return OperationResult.FromSuccess($"Updated {package.Id} to {latestVersion} in Directory.Packages.props");
            };
            description = $"Updating {package.Id} to {package.LatestVersion} (central)";
        }
        else
        {
            operation = (ct, progress) => _cliService.UpdatePackageAsync(
                project.FilePath, package.Id, package.LatestVersion, ct, progress);
            description = $"Updating {package.Id} to {package.LatestVersion}";
        }

        // Show update progress modal with cancellation support
        var result = await OperationProgressModal.ShowAsync(
            _windowSystem,
            OperationType.Update,
            operation,
            "Updating Package",
            description,
            _historyService,
            project.FilePath,
            project.Name,
            package.Id,
            package.LatestVersion,
            _parentWindow,
            previousVersion: package.Version,
            versionSource: package.VersionSource,
            propsFilePath: package.PropsFilePath);

        if (result.Success) InvalidateCacheFor(package.Id);
        return result;
    }

    /// <summary>
    /// Handle removing a package from a project
    /// </summary>
    public async Task<OperationResult> HandleRemovePackageAsync(
        PackageReference package,
        ProjectInfo project)
    {
        // For centrally-managed packages, inform the user that only the project reference
        // will be removed; the central version declaration in Directory.Packages.props is preserved.
        var confirmMessage = package.VersionSource == VersionSource.Central
            ? $"Remove {package.Id} from {project.Name}?\n\n[CPM] The central version in Directory.Packages.props will be preserved — other projects that reference this package are unaffected."
            : package.VersionSource == VersionSource.Override
            ? $"Remove {package.Id} (version override {package.Version}) from {project.Name}?"
            : $"Remove {package.Id} from {project.Name}?";

        var confirm = await ConfirmationModal.ShowAsync(_windowSystem,
            "Remove Package",
            confirmMessage,
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

        if (result.Success) InvalidateCacheFor(package.Id);
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

            // For centrally-managed packages, update Directory.Packages.props directly.
            // For inline/override packages, use dotnet add which modifies the project file.
            Func<CancellationToken, IProgress<string>, Task<OperationResult>> operation;
            string operationDescription;

            if (package.VersionSource == VersionSource.Central && !string.IsNullOrEmpty(package.PropsFilePath))
            {
                var propsPath = package.PropsFilePath;
                operation = async (ct, progress) =>
                {
                    progress.Report($"Updating {package.Id} to {selectedVersion} in Directory.Packages.props...");
                    await _cpmRepository.UpdatePackageVersionAsync(propsPath, package.Id, selectedVersion);
                    progress.Report($"Updated {package.Id} to {selectedVersion}");
                    return OperationResult.FromSuccess($"Updated {package.Id} to {selectedVersion} in Directory.Packages.props");
                };
                operationDescription = $"Changing {package.Id} to version {selectedVersion} (central)";
            }
            else
            {
                operation = (ct, progress) => _cliService.AddPackageAsync(
                    project.FilePath, package.Id, selectedVersion, ct, progress);
                operationDescription = $"Changing {package.Id} to version {selectedVersion}";
            }

            // Use OperationProgressModal for progress feedback and history recording
            var result = await OperationProgressModal.ShowAsync(
                _windowSystem,
                OperationType.Update,  // Changing version is an update operation
                operation,
                "Changing Package Version",
                operationDescription,
                _historyService,
                project.FilePath,
                project.Name,
                package.Id,
                selectedVersion,
                _parentWindow,
                previousVersion: package.Version,
                versionSource: package.VersionSource,
                propsFilePath: package.PropsFilePath);

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
            outdated,
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

        if (result.Success) InvalidateAllCache();
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
    /// Handle updating a selected subset of packages
    /// </summary>
    public async Task<OperationResult> HandleUpdateSelectedAsync(
        List<PackageReference> packages,
        ProjectInfo project)
    {
        var outdated = packages.Where(p => p.IsOutdated && !string.IsNullOrEmpty(p.LatestVersion)).ToList();
        if (!outdated.Any())
            return new OperationResult { Success = false };

        // Show update strategy modal (same as UpdateAll)
        var strategy = await UpdateStrategyModal.ShowAsync(
            _windowSystem,
            outdated,
            project.Name,
            _parentWindow);

        if (strategy == null)
            return new OperationResult { Success = false };

        // Filter packages based on strategy
        var packagesToUpdate = outdated
            .Where(p => VersionComparisonService.IsUpdateAllowed(
                p.Version,
                p.LatestVersion!,
                strategy.Value))
            .ToList();

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

        var confirmMessage = $"Update {packagesToUpdate.Count} selected package(s) in {project.Name}?";
        var confirm = await ConfirmationModal.ShowAsync(_windowSystem,
            "Update Selected Packages",
            confirmMessage,
            parentWindow: _parentWindow);
        if (!confirm) return new OperationResult { Success = false };

        var packageList = packagesToUpdate.Select(p => (p.Id, p.LatestVersion!)).ToList();

        var result = await OperationProgressModal.ShowAsync(
            _windowSystem,
            OperationType.Update,
            (ct, progress) => _cliService.UpdateAllPackagesAsync(
                project.FilePath, packageList, ct, progress),
            "Updating Selected Packages",
            $"Updating {packageList.Count} packages in {project.Name}",
            _historyService,
            project.FilePath,
            project.Name,
            null,
            null,
            _parentWindow);

        if (result.Success)
        {
            foreach (var pkg in packagesToUpdate)
                InvalidateCacheFor(pkg.Id);
        }
        return result;
    }

    /// <summary>
    /// Handle removing a selected set of packages from a project
    /// </summary>
    public async Task<OperationResult> HandleRemoveSelectedAsync(
        List<PackageReference> packages,
        ProjectInfo project)
    {
        if (!packages.Any())
            return new OperationResult { Success = false };

        var confirm = await ConfirmationModal.ShowAsync(_windowSystem,
            "Remove Selected Packages",
            $"Remove {packages.Count} package(s) from {project.Name}?",
            parentWindow: _parentWindow);
        if (!confirm) return new OperationResult { Success = false };

        OperationResult last = new OperationResult { Success = true };
        foreach (var package in packages)
        {
            last = await OperationProgressModal.ShowAsync(
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

            if (last.Success) InvalidateCacheFor(package.Id);
            if (!last.Success) break;
        }

        return last;
    }

    /// <summary>
    /// Handle migrating a deprecated package to its recommended alternate
    /// </summary>
    public async Task<OperationResult> HandleMigratePackageAsync(
        PackageReference package,
        ProjectInfo project)
    {
        try
        {
            var nugetData = await _nugetService.GetPackageDetailsAsync(package.Id);
            var alternateId = nugetData?.AlternatePackageId ?? string.Empty;

            if (string.IsNullOrEmpty(alternateId))
            {
                _windowSystem.NotificationStateService.ShowNotification(
                    "No Alternate Package",
                    $"No alternate package found for {package.Id}",
                    NotificationSeverity.Warning,
                    timeout: 3000,
                    parentWindow: _parentWindow);
                return new OperationResult { Success = false };
            }

            var confirm = await ConfirmationModal.ShowAsync(_windowSystem,
                "Migrate Package",
                $"Replace {package.Id} with {alternateId}?",
                parentWindow: _parentWindow);
            if (!confirm) return new OperationResult { Success = false };

            // Install the alternate package
            var addResult = await OperationProgressModal.ShowAsync(
                _windowSystem,
                OperationType.Add,
                (ct, progress) => _cliService.AddPackageAsync(
                    project.FilePath, alternateId, null, ct, progress),
                "Installing Replacement",
                $"Installing {alternateId} to replace {package.Id}",
                _historyService,
                project.FilePath,
                project.Name,
                alternateId,
                null,
                _parentWindow);

            if (!addResult.Success) return addResult;

            // Remove the old package
            var removeResult = await OperationProgressModal.ShowAsync(
                _windowSystem,
                OperationType.Remove,
                (ct, progress) => _cliService.RemovePackageAsync(
                    project.FilePath, package.Id, ct, progress),
                "Removing Old Package",
                $"Removing {package.Id}",
                _historyService,
                project.FilePath,
                project.Name,
                package.Id,
                null,
                _parentWindow);

            InvalidateCacheFor(package.Id);
            InvalidateCacheFor(alternateId);

            return removeResult;
        }
        catch (Exception ex)
        {
            await (_errorHandler?.HandleAsync(ex, ErrorSeverity.Critical,
                "Migration Error", "An error occurred while migrating the package.", "Actions", _parentWindow)
                ?? Task.CompletedTask);
            return new OperationResult { Success = false };
        }
    }

    private void InvalidateCacheFor(string packageId)
    {
        if (_nugetService is NuGetCacheService cache)
            cache.InvalidatePackage(packageId);
    }

    private void InvalidateAllCache()
    {
        if (_nugetService is NuGetCacheService cache)
            cache.ClearAll();
    }

    /// <summary>
    /// Show operation history modal
    /// </summary>
    public async Task ShowOperationHistoryAsync()
    {
        await OperationHistoryModal.ShowAsync(_windowSystem, _historyService, _cliService, _cpmRepository, _parentWindow);
    }
}
