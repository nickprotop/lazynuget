using NuGet.Versioning;
using LazyNuGet.Models;

namespace LazyNuGet.Services;

/// <summary>
/// Service for comparing package versions and determining update eligibility
/// </summary>
public class VersionComparisonService
{
    /// <summary>
    /// Determines if a package update is allowed based on the update strategy
    /// </summary>
    /// <param name="currentVersion">Current installed version</param>
    /// <param name="latestVersion">Latest available version</param>
    /// <param name="strategy">Update strategy to apply</param>
    /// <returns>True if the update is allowed by the strategy</returns>
    public static bool IsUpdateAllowed(
        string currentVersion,
        string latestVersion,
        UpdateStrategy strategy)
    {
        if (!NuGetVersion.TryParse(currentVersion, out var current))
            return false;

        if (!NuGetVersion.TryParse(latestVersion, out var latest))
            return false;

        if (current >= latest)
            return false;

        return strategy switch
        {
            UpdateStrategy.UpdateAllToLatest => true,
            UpdateStrategy.MinorAndPatchOnly => latest.Major == current.Major,
            UpdateStrategy.PatchOnly => latest.Major == current.Major && latest.Minor == current.Minor,
            _ => false
        };
    }
}
