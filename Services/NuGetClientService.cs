using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LazyNuGet.Models;
using SharpConsoleUI.Logging;

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
                Authors = d.Authors ?? new List<string>(),
                Tags = d.Tags ?? new List<string>(),
                Versions = d.Versions?.Select(v => v.Version ?? string.Empty).ToList() ?? new List<string>()
            }).ToList();
        }
        catch (Exception ex)
        {
            _logService?.LogError($"Error searching NuGet: {ex.Message}", ex, "NuGet");
            return new List<NuGetPackage>();
        }
    }

    /// <summary>
    /// Get detailed information about a specific package
    /// </summary>
    public async Task<NuGetPackage?> GetPackageDetailsAsync(string packageId, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"{RegistrationBaseUrl}/{packageId.ToLowerInvariant()}/index.json";
            var response = await _httpClient.GetFromJsonAsync<NuGetRegistrationResponse>(url, cancellationToken);

            if (response?.Items == null || response.Items.Count == 0)
                return null;

            // Get all versions from all pages
            var allVersions = new List<string>();
            var latestCatalogEntry = response.Items
                .SelectMany(i => i.Items ?? new List<NuGetCatalogItem>())
                .OrderByDescending(i => i.CatalogEntry?.Published)
                .FirstOrDefault()
                ?.CatalogEntry;

            foreach (var page in response.Items)
            {
                if (page.Items != null)
                {
                    allVersions.AddRange(page.Items.Select(i => i.CatalogEntry?.Version ?? string.Empty));
                }
            }

            if (latestCatalogEntry == null)
                return null;

            return new NuGetPackage
            {
                Id = latestCatalogEntry.Id ?? packageId,
                Version = latestCatalogEntry.Version ?? string.Empty,
                Description = latestCatalogEntry.Description ?? string.Empty,
                ProjectUrl = latestCatalogEntry.ProjectUrl,
                LicenseUrl = latestCatalogEntry.LicenseUrl,
                Authors = latestCatalogEntry.Authors?.Split(',').Select(a => a.Trim()).ToList() ?? new List<string>(),
                Tags = latestCatalogEntry.Tags?.Split(',').Select(t => t.Trim()).ToList() ?? new List<string>(),
                Published = latestCatalogEntry.Published,
                Versions = allVersions.OrderByDescending(v => v).ToList(),
                TotalDownloads = 0 // Registration API doesn't include download count
            };
        }
        catch (Exception ex)
        {
            _logService?.LogError($"Error getting package details: {ex.Message}", ex, "NuGet");
            return null;
        }
    }

    /// <summary>
    /// Get the latest version of a package
    /// </summary>
    public async Task<string?> GetLatestVersionAsync(string packageId, CancellationToken cancellationToken = default)
    {
        try
        {
            var package = await GetPackageDetailsAsync(packageId, cancellationToken);
            return package?.Version;
        }
        catch
        {
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

    [JsonPropertyName("authors")]
    public List<string>? Authors { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("versions")]
    public List<NuGetVersionData>? Versions { get; set; }
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
}
