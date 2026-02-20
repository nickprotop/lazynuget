using System.Collections.Concurrent;
using LazyNuGet.Models;
using LazyNuGet.Repositories;
using SharpConsoleUI.Logging;
using NuGet.Versioning;

namespace LazyNuGet.Services;

/// <summary>
/// Service for interacting with NuGet packages - provides business logic on top of NuGetRepository.
/// Coordinates package search, version resolution, and data enrichment.
/// </summary>
public class NuGetClientService : IDisposable
{
    private readonly NuGetRepository _repository;
    private readonly ILogService? _logService;
    private readonly ConcurrentDictionary<string, DateTime> _failedSources = new();

    public NuGetClientService(ILogService? logService = null, List<NuGetSource>? sources = null)
    {
        _logService = logService;
        _repository = new NuGetRepository(sources);

        // Validate at least one enabled source
        if (!_repository.GetSources().Any(s => s.IsEnabled))
        {
            _logService?.LogWarning("No enabled NuGet sources, defaulting to nuget.org", "NuGet");
        }
    }

    /// <summary>
    /// Get the list of active sources (for display in settings)
    /// </summary>
    public IReadOnlyList<NuGetSource> Sources => _repository.GetSources();

    /// <summary>
    /// Search for packages across all enabled feeds
    /// </summary>
    public virtual async Task<List<NuGetPackage>> SearchPackagesAsync(string query, int take = 20, CancellationToken cancellationToken = default)
    {
        try
        {
            var enabledSources = _repository.GetSources().Where(s => s.IsEnabled).ToList();
            var allResults = new ConcurrentBag<NuGetPackage>();

            // Fan out search to all enabled feeds in parallel
            var tasks = enabledSources.Select(source => Task.Run(async () =>
            {
                try
                {
                    var searchData = await _repository.SearchPackagesOnSourceAsync(source, query, take, cancellationToken);
                    var packages = ConvertSearchDataToPackages(searchData);
                    foreach (var pkg in packages)
                    {
                        allResults.Add(pkg);
                    }
                }
                catch (Exception ex)
                {
                    _logService?.LogWarning($"Search failed on source '{source.Name}': {ex.Message}", "NuGet");
                    // Non-critical: continue searching other sources
                }
            }, cancellationToken));

            await Task.WhenAll(tasks);

            // Deduplicate by package ID (prefer highest version)
            return allResults
                .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(p =>
                    NuGetVersion.TryParse(p.Version, out var v) ? v : new NuGetVersion(0, 0, 0))
                    .ThenByDescending(p => p.TotalDownloads)
                    .First())
                .Take(take)
                .ToList();
        }
        catch (Exception ex)
        {
            _logService?.LogError($"Error searching NuGet: {ex.Message}", ex, "NuGet");
            return new List<NuGetPackage>();
        }
    }

    private List<NuGetPackage> ConvertSearchDataToPackages(List<NuGetSearchData> searchData)
    {
        return searchData.Select(d => new NuGetPackage
        {
            Id = d.Id ?? string.Empty,
            Version = d.Version ?? string.Empty,
            Description = d.Description ?? string.Empty,
            TotalDownloads = d.TotalDownloads,
            ProjectUrl = d.ProjectUrl,
            LicenseUrl = d.LicenseUrl,
            LicenseExpression = d.LicenseExpression,
            RepositoryUrl = d.RepositoryUrl,
            Authors = d.Authors ?? new List<string>(),
            Tags = d.Tags ?? new List<string>(),
            Versions = d.Versions?.Select(v => v.Version ?? string.Empty).ToList() ?? new List<string>(),
            IsVerified = d.Verified,
            VulnerabilityCount = d.Vulnerabilities?.Count ?? 0,
            IsDeprecated = d.Deprecation != null,
            DeprecationMessage = d.Deprecation?.Message,
            AlternatePackageId = d.Deprecation?.AlternatePackage?.Id
        }).ToList();
    }

    /// <summary>
    /// Get detailed information about a specific package.
    /// Tries each enabled feed until the package is found.
    /// </summary>
    public virtual async Task<NuGetPackage?> GetPackageDetailsAsync(string packageId, CancellationToken cancellationToken = default)
    {
        var enabledSources = _repository.GetSources().Where(s => s.IsEnabled).ToList();

        foreach (var source in enabledSources)
        {
            try
            {
                var data = await _repository.GetPackageDetailsOnSourceAsync(source, packageId, cancellationToken);
                if (data == null)
                    continue;

                // Fetch ALL versions from flat container API
                List<string> allVersions;
                try
                {
                    allVersions = await _repository.GetAllVersionsAsync(source, packageId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logService?.LogWarning($"Could not fetch all versions from flat container: {ex.Message}", "NuGet");
                    // Fallback to versions from search API
                    allVersions = data.Versions?.Select(v => v.Version ?? string.Empty)
                        .Select(v => (version: v, parsed: NuGetVersion.TryParse(v, out var nv) ? nv : null))
                        .Where(x => x.parsed != null)
                        .OrderByDescending(x => x.parsed)
                        .Select(x => x.version)
                        .ToList() ?? new List<string>();
                }

                var package = new NuGetPackage
                {
                    Id = data.Id ?? packageId,
                    Version = data.Version ?? string.Empty,
                    Description = data.Description ?? string.Empty,
                    ProjectUrl = data.ProjectUrl,
                    LicenseUrl = data.LicenseUrl,
                    LicenseExpression = data.LicenseExpression,
                    RepositoryUrl = data.RepositoryUrl,
                    Authors = data.Authors ?? new List<string>(),
                    Tags = data.Tags ?? new List<string>(),
                    TotalDownloads = data.TotalDownloads,
                    Versions = allVersions,
                    IsVerified = data.Verified,
                    VulnerabilityCount = data.Vulnerabilities?.Count ?? 0,
                    IsDeprecated = data.Deprecation != null,
                    DeprecationMessage = data.Deprecation?.Message,
                    AlternatePackageId = data.Deprecation?.AlternatePackage?.Id
                };

                // Fetch additional details from registration API
                await EnrichPackageWithCatalogDataAsync(source, package, cancellationToken);

                return package;
            }
            catch (Exception ex)
            {
                _logService?.LogWarning($"Failed to get details from '{source.Name}': {ex.Message}", "NuGet");
                // Non-critical: continue trying other sources
            }
        }

        return null;
    }

    /// <summary>
    /// Get the latest stable version of a package
    /// </summary>
    public async Task<string?> GetLatestVersionAsync(string packageId, CancellationToken cancellationToken = default)
    {
        var enabledSources = _repository.GetSources().Where(s => s.IsEnabled).ToList();

        foreach (var source in enabledSources)
        {
            // Skip sources that failed recently (< 60s ago) to avoid repeated timeouts during bulk checks
            if (_failedSources.TryGetValue(source.Url, out var failedAt) &&
                (DateTime.UtcNow - failedAt).TotalSeconds < 60)
                continue;

            try
            {
                var versions = await _repository.GetAllVersionsAsync(source, packageId, cancellationToken);
                if (versions.Count == 0)
                    continue;

                _failedSources.TryRemove(source.Url, out _);

                var stable = versions
                    .Where(v => !v.Contains('-'))
                    .ToList();

                return stable.Count > 0 ? stable[0] : versions[0]; // Already sorted descending
            }
            catch (Exception ex)
            {
                _failedSources[source.Url] = DateTime.UtcNow;
                _logService?.LogWarning($"Failed to check version on '{source.Name}' for {packageId}: {ex.Message}", "NuGet");
                // Non-critical: continue trying other sources
            }
        }

        return null;
    }

    /// <summary>
    /// Check if a package version is outdated
    /// </summary>
    public virtual async Task<(bool IsOutdated, string? LatestVersion)> CheckIfOutdatedAsync(
        string packageId,
        string currentVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var latestVersion = await GetLatestVersionAsync(packageId, cancellationToken);
            if (string.IsNullOrEmpty(latestVersion))
                return (false, null);

            if (!NuGetVersion.TryParse(currentVersion, out var current) ||
                !NuGetVersion.TryParse(latestVersion, out var latest))
                return (false, null);
            var isOutdated = latest > current;
            return (isOutdated, latestVersion);
        }
        catch
        {
            return (false, null);
        }
    }

    /// <summary>
    /// Enriches a package with additional metadata from the registration/catalog API
    /// </summary>
    public async Task EnrichPackageWithCatalogDataAsync(NuGetSource source, NuGetPackage package, CancellationToken cancellationToken = default)
    {
        try
        {
            var catalogEntry = await _repository.GetCatalogEntryAsync(source, package.Id, package.Version, cancellationToken);
            if (catalogEntry == null)
                return;

            package.PackageSize = catalogEntry.PackageSize;
            package.ReleaseNotes = catalogEntry.ReleaseNotes;

            if (catalogEntry.DependencyGroups != null && catalogEntry.DependencyGroups.Any())
            {
                package.TargetFrameworks = catalogEntry.DependencyGroups
                    .Where(g => !string.IsNullOrEmpty(g.TargetFramework))
                    .Select(g => g.TargetFramework!)
                    .Distinct()
                    .ToList();

                // Store full dependency info (keep empty groups — they indicate supported frameworks)
                package.Dependencies = catalogEntry.DependencyGroups
                    .Select(g => new PackageDependencyGroup
                    {
                        TargetFramework = g.TargetFramework ?? "(any)",
                        Packages = g.Dependencies?
                            .Where(d => !string.IsNullOrEmpty(d.Id))
                            .Select(d => new PackageDependencyInfo
                            {
                                Id = d.Id!,
                                VersionRange = d.Range ?? ""
                            })
                            .ToList() ?? new()
                    })
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            _logService?.LogWarning($"Could not enrich package with catalog data: {ex.Message}", "NuGet");
            // Non-critical: package will have basic info without additional metadata
        }
    }

    /// <summary>
    /// Extract the latest stable version from an already-fetched package's version list,
    /// avoiding an extra HTTP call when we already have all versions.
    /// </summary>
    public static string? GetLatestStableVersion(NuGetPackage package)
    {
        if (package.Versions == null || package.Versions.Count == 0)
            return null;

        // Versions are already sorted descending from GetAllVersionsAsync
        return package.Versions
            .FirstOrDefault(v => !v.Contains('-'))
            ?? package.Versions.FirstOrDefault();
    }

    public void Dispose()
    {
        _repository?.Dispose();
    }
}
