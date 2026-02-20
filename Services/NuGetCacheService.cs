using System.Collections.Concurrent;
using LazyNuGet.Models;
using SharpConsoleUI.Logging;

namespace LazyNuGet.Services;

/// <summary>
/// Session-scoped in-memory cache layer over NuGetClientService.
/// Eliminates redundant network calls within a single app session.
/// Cache is cleared on Ctrl+R (ClearAll) or after package mutations (InvalidatePackage).
/// </summary>
public class NuGetCacheService : NuGetClientService
{
    private readonly ConcurrentDictionary<string, List<NuGetPackage>> _searchCache  = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, NuGetPackage>       _detailsCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (bool, string?)>    _versionCache = new(StringComparer.OrdinalIgnoreCase);

    public NuGetCacheService(ILogService? logService, List<NuGetSource>? sources)
        : base(logService, sources) { }

    public override async Task<List<NuGetPackage>> SearchPackagesAsync(
        string query, int take = 20, CancellationToken cancellationToken = default)
    {
        var key = $"{query}|{take}";
        if (_searchCache.TryGetValue(key, out var hit)) return hit;
        var result = await base.SearchPackagesAsync(query, take, cancellationToken);
        _searchCache[key] = result;
        return result;
    }

    public override async Task<NuGetPackage?> GetPackageDetailsAsync(
        string packageId, CancellationToken cancellationToken = default)
    {
        if (_detailsCache.TryGetValue(packageId, out var hit)) return hit;
        var result = await base.GetPackageDetailsAsync(packageId, cancellationToken);
        if (result != null) _detailsCache[packageId] = result;
        return result;
    }

    public override async Task<(bool IsOutdated, string? LatestVersion)> CheckIfOutdatedAsync(
        string packageId, string currentVersion, CancellationToken cancellationToken = default)
    {
        if (_versionCache.TryGetValue(packageId, out var hit)) return hit;
        var result = await base.CheckIfOutdatedAsync(packageId, currentVersion, cancellationToken);
        _versionCache[packageId] = result;
        return result;
    }

    /// <summary>
    /// True when any cache dictionary has entries — used to display a "cached" indicator in the UI.
    /// </summary>
    public bool IsAnyCacheWarm =>
        _versionCache.Count > 0 || _detailsCache.Count > 0 || _searchCache.Count > 0;

    /// <summary>
    /// True when the details for a specific package are already in the cache.
    /// Check this BEFORE calling GetPackageDetailsAsync to know whether the result will be instant.
    /// </summary>
    public bool IsPackageDetailsCached(string packageId) =>
        _detailsCache.ContainsKey(packageId);

    /// <summary>
    /// Drop cached entries for one package (after add/remove/update on that package).
    /// Search cache is fully cleared since results contain stale package snapshots.
    /// </summary>
    public void InvalidatePackage(string packageId)
    {
        _detailsCache.TryRemove(packageId, out _);
        _versionCache.TryRemove(packageId, out _);
        _searchCache.Clear();
    }

    /// <summary>
    /// Drop all cached data — called on Ctrl+R for a fully fresh session.
    /// </summary>
    public void ClearAll()
    {
        _detailsCache.Clear();
        _versionCache.Clear();
        _searchCache.Clear();
    }
}
