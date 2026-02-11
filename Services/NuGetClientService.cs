using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LazyNuGet.Models;
using SharpConsoleUI.Logging;
using NuGet.Versioning;

namespace LazyNuGet.Services;

/// <summary>
/// Service for interacting with NuGet.org API v3
/// </summary>
public class NuGetClientService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogService? _logService;
    private const string SearchBaseUrl = "https://azuresearch-usnc.nuget.org/query";
    private const string RegistrationBaseUrl = "https://api.nuget.org/v3/registration5-semver1";
    private const string FlatContainerBaseUrl = "https://api.nuget.org/v3-flatcontainer";

    public NuGetClientService(ILogService? logService = null)
    {
        _logService = logService;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "LazyNuGet/1.0");
    }

    /// <summary>
    /// Search for packages on NuGet.org
    /// </summary>
    public async Task<List<NuGetPackage>> SearchPackagesAsync(string query, int take = 20, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"{SearchBaseUrl}?q={Uri.EscapeDataString(query)}&take={take}";
            var response = await _httpClient.GetFromJsonAsync<NuGetSearchResponse>(url, cancellationToken);

            if (response?.Data == null)
                return new List<NuGetPackage>();

            return response.Data.Select(d => new NuGetPackage
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
        catch (Exception ex)
        {
            _logService?.LogError($"Error searching NuGet: {ex.Message}", ex, "NuGet");
            return new List<NuGetPackage>();
        }
    }

    /// <summary>
    /// Get detailed information about a specific package.
    /// Uses the Search API for metadata and Flat Container API for ALL versions.
    /// </summary>
    public async Task<NuGetPackage?> GetPackageDetailsAsync(string packageId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Use search API with exact packageid filter — returns metadata + versions in one call
            var url = $"{SearchBaseUrl}?q=packageid:{Uri.EscapeDataString(packageId)}&take=1";
            var response = await _httpClient.GetFromJsonAsync<NuGetSearchResponse>(url, cancellationToken);

            var data = response?.Data?.FirstOrDefault();
            if (data == null)
                return null;

            // Fetch ALL versions from flat container API (no pagination, complete list)
            List<string> allVersions = new();
            try
            {
                var flatUrl = $"{FlatContainerBaseUrl}/{packageId.ToLowerInvariant()}/index.json";
                var flatResponse = await _httpClient.GetFromJsonAsync<FlatContainerResponse>(flatUrl, cancellationToken);
                if (flatResponse?.Versions != null && flatResponse.Versions.Count > 0)
                {
                    // Sort using semantic versioning (descending - newest first)
                    allVersions = flatResponse.Versions
                        .Select(v => (version: v, parsed: NuGetVersion.TryParse(v, out var nv) ? nv : null))
                        .Where(x => x.parsed != null)
                        .OrderByDescending(x => x.parsed)
                        .Select(x => x.version)
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                _logService?.LogWarning($"Could not fetch all versions from flat container, using search API versions: {ex.Message}", "NuGet");
                // Fallback to search API versions if flat container fails
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

            // Fetch additional details from registration API (catalog entry)
            await EnrichPackageWithCatalogDataAsync(package, cancellationToken);

            return package;
        }
        catch (Exception ex)
        {
            _logService?.LogError($"Error getting package details: {ex.Message}", ex, "NuGet");
            return null;
        }
    }

    /// <summary>
    /// Get the latest stable version of a package using the flat container API.
    /// This is a simple JSON array of all versions — no pagination.
    /// </summary>
    public async Task<string?> GetLatestVersionAsync(string packageId, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"{FlatContainerBaseUrl}/{packageId.ToLowerInvariant()}/index.json";
            var response = await _httpClient.GetFromJsonAsync<FlatContainerResponse>(url, cancellationToken);

            if (response?.Versions == null || response.Versions.Count == 0)
                return null;

            // Filter to stable versions (no prerelease tags like -beta, -rc, -preview)
            var stable = response.Versions
                .Where(v => !v.Contains('-'))
                .ToList();

            // Return last stable version (flat container returns them in ascending order)
            return stable.Count > 0 ? stable[^1] : response.Versions[^1];
        }
        catch (Exception ex)
        {
            _logService?.LogError($"Error getting latest version for {packageId}: {ex.Message}", ex, "NuGet");
            return null;
        }
    }

    /// <summary>
    /// Check if a package version is outdated
    /// </summary>
    public async Task<(bool IsOutdated, string? LatestVersion)> CheckIfOutdatedAsync(
        string packageId,
        string currentVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var latestVersion = await GetLatestVersionAsync(packageId, cancellationToken);
            if (string.IsNullOrEmpty(latestVersion))
                return (false, null);

            var isOutdated = !string.Equals(currentVersion, latestVersion, StringComparison.OrdinalIgnoreCase);
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
    private async Task EnrichPackageWithCatalogDataAsync(NuGetPackage package, CancellationToken cancellationToken = default)
    {
        try
        {
            // Fetch latest version's catalog entry from registration API
            var registrationUrl = $"{RegistrationBaseUrl}/{package.Id.ToLowerInvariant()}/index.json";
            var registrationResponse = await _httpClient.GetFromJsonAsync<NuGetRegistrationResponse>(registrationUrl, cancellationToken);

            if (registrationResponse?.Items == null || !registrationResponse.Items.Any())
                return;

            // Get the latest version's catalog entry
            var latestPage = registrationResponse.Items.LastOrDefault();
            if (latestPage?.Items == null || !latestPage.Items.Any())
                return;

            var latestCatalogItem = latestPage.Items.LastOrDefault();
            var catalogEntry = latestCatalogItem?.CatalogEntry;

            if (catalogEntry == null)
                return;

            // Enrich with catalog data
            package.PackageSize = catalogEntry.PackageSize;
            package.ReleaseNotes = catalogEntry.ReleaseNotes;

            // Extract target frameworks from dependency groups
            if (catalogEntry.DependencyGroups != null && catalogEntry.DependencyGroups.Any())
            {
                package.TargetFrameworks = catalogEntry.DependencyGroups
                    .Where(g => !string.IsNullOrEmpty(g.TargetFramework))
                    .Select(g => g.TargetFramework!)
                    .Distinct()
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            // Don't fail the entire request if catalog enrichment fails
            _logService?.LogWarning($"Could not enrich package with catalog data: {ex.Message}", "NuGet");
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

// NuGet API response models
internal class NuGetSearchResponse
{
    [JsonPropertyName("data")]
    public List<NuGetSearchData>? Data { get; set; }
}

internal class NuGetSearchData
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("totalDownloads")]
    public long TotalDownloads { get; set; }

    [JsonPropertyName("projectUrl")]
    public string? ProjectUrl { get; set; }

    [JsonPropertyName("licenseUrl")]
    public string? LicenseUrl { get; set; }

    [JsonPropertyName("licenseExpression")]
    public string? LicenseExpression { get; set; }

    [JsonPropertyName("repositoryUrl")]
    public string? RepositoryUrl { get; set; }

    [JsonPropertyName("authors")]
    public List<string>? Authors { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("versions")]
    public List<NuGetVersionData>? Versions { get; set; }

    [JsonPropertyName("verified")]
    public bool Verified { get; set; }

    [JsonPropertyName("packageTypes")]
    public List<NuGetPackageType>? PackageTypes { get; set; }

    [JsonPropertyName("vulnerabilities")]
    public List<NuGetVulnerability>? Vulnerabilities { get; set; }

    [JsonPropertyName("deprecation")]
    public NuGetDeprecation? Deprecation { get; set; }
}

internal class NuGetPackageType
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal class NuGetVulnerability
{
    [JsonPropertyName("advisoryUrl")]
    public string? AdvisoryUrl { get; set; }

    [JsonPropertyName("severity")]
    public string? Severity { get; set; }
}

internal class NuGetDeprecation
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("reasons")]
    public List<string>? Reasons { get; set; }

    [JsonPropertyName("alternatePackage")]
    public NuGetAlternatePackage? AlternatePackage { get; set; }
}

internal class NuGetAlternatePackage
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("range")]
    public string? Range { get; set; }
}

internal class NuGetVersionData
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

internal class NuGetRegistrationResponse
{
    [JsonPropertyName("items")]
    public List<NuGetRegistrationPage>? Items { get; set; }
}

internal class NuGetRegistrationPage
{
    [JsonPropertyName("items")]
    public List<NuGetCatalogItem>? Items { get; set; }
}

internal class NuGetCatalogItem
{
    [JsonPropertyName("catalogEntry")]
    public NuGetCatalogEntry? CatalogEntry { get; set; }
}

internal class NuGetCatalogEntry
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("projectUrl")]
    public string? ProjectUrl { get; set; }

    [JsonPropertyName("licenseUrl")]
    public string? LicenseUrl { get; set; }

    [JsonPropertyName("authors")]
    public string? Authors { get; set; }

    [JsonPropertyName("tags")]
    public string? Tags { get; set; }

    [JsonPropertyName("published")]
    public DateTime? Published { get; set; }

    [JsonPropertyName("packageSize")]
    public long? PackageSize { get; set; }

    [JsonPropertyName("releaseNotes")]
    public string? ReleaseNotes { get; set; }

    [JsonPropertyName("dependencyGroups")]
    public List<NuGetDependencyGroup>? DependencyGroups { get; set; }
}

internal class NuGetDependencyGroup
{
    [JsonPropertyName("targetFramework")]
    public string? TargetFramework { get; set; }

    [JsonPropertyName("dependencies")]
    public List<NuGetDependency>? Dependencies { get; set; }
}

internal class NuGetDependency
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("range")]
    public string? Range { get; set; }
}

internal class FlatContainerResponse
{
    [JsonPropertyName("versions")]
    public List<string> Versions { get; set; } = new();
}
