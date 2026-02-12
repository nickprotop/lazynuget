using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LazyNuGet.Models;
using SharpConsoleUI.Logging;
using NuGet.Versioning;

namespace LazyNuGet.Services;

/// <summary>
/// Resolved endpoints for a NuGet V3 feed
/// </summary>
internal class FeedEndpoints
{
    public string SearchQueryService { get; set; } = string.Empty;
    public string RegistrationBaseUrl { get; set; } = string.Empty;
    public string PackageBaseAddress { get; set; } = string.Empty;
}

/// <summary>
/// Service for interacting with NuGet API v3, supporting multiple feed sources
/// </summary>
public class NuGetClientService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogService? _logService;
    private readonly List<NuGetSource> _sources;
    private readonly ConcurrentDictionary<string, FeedEndpoints> _endpointCache = new();

    // Hardcoded nuget.org endpoints (fast path — skip service index resolution)
    private const string NuGetOrgSearchUrl = "https://azuresearch-usnc.nuget.org/query";
    private const string NuGetOrgRegistrationUrl = "https://api.nuget.org/v3/registration5-semver1";
    private const string NuGetOrgFlatContainerUrl = "https://api.nuget.org/v3-flatcontainer";
    private const string NuGetOrgUrl = "https://api.nuget.org/v3/index.json";

    public NuGetClientService(ILogService? logService = null, List<NuGetSource>? sources = null)
    {
        _logService = logService;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "LazyNuGet/1.0");

        // Default to nuget.org if no sources provided
        _sources = sources ?? new List<NuGetSource>
        {
            new NuGetSource
            {
                Name = "nuget.org",
                Url = NuGetOrgUrl,
                IsEnabled = true,
                Origin = NuGetSourceOrigin.NuGetConfig
            }
        };

        // Ensure at least nuget.org is present
        if (!_sources.Any(s => s.IsEnabled))
        {
            _logService?.LogWarning("No enabled NuGet sources, defaulting to nuget.org", "NuGet");
            _sources.Add(new NuGetSource
            {
                Name = "nuget.org",
                Url = NuGetOrgUrl,
                IsEnabled = true,
                Origin = NuGetSourceOrigin.NuGetConfig
            });
        }
    }

    /// <summary>
    /// Get the list of active sources (for display in settings)
    /// </summary>
    public IReadOnlyList<NuGetSource> Sources => _sources.AsReadOnly();

    /// <summary>
    /// Search for packages across all enabled feeds
    /// </summary>
    public async Task<List<NuGetPackage>> SearchPackagesAsync(string query, int take = 20, CancellationToken cancellationToken = default)
    {
        try
        {
            var enabledSources = _sources.Where(s => s.IsEnabled).ToList();
            var allResults = new ConcurrentBag<NuGetPackage>();

            // Fan out search to all enabled feeds in parallel
            var tasks = enabledSources.Select(source => Task.Run(async () =>
            {
                try
                {
                    var results = await SearchPackagesOnSourceAsync(source, query, take, cancellationToken);
                    foreach (var pkg in results)
                    {
                        allResults.Add(pkg);
                    }
                }
                catch (Exception ex)
                {
                    _logService?.LogWarning($"Search failed on source '{source.Name}': {ex.Message}", "NuGet");
                }
            }, cancellationToken));

            await Task.WhenAll(tasks);

            // Deduplicate by package ID (prefer highest version)
            return allResults
                .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(p =>
                    NuGetVersion.TryParse(p.Version, out var v) ? v : new NuGetVersion(0, 0, 0))
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

    private async Task<List<NuGetPackage>> SearchPackagesOnSourceAsync(
        NuGetSource source, string query, int take, CancellationToken cancellationToken)
    {
        var endpoints = await ResolveEndpointsAsync(source, cancellationToken);
        var url = $"{endpoints.SearchQueryService}?q={Uri.EscapeDataString(query)}&take={take}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyAuth(request, source);

        var httpResponse = await _httpClient.SendAsync(request, cancellationToken);
        httpResponse.EnsureSuccessStatusCode();
        var response = await httpResponse.Content.ReadFromJsonAsync<NuGetSearchResponse>(cancellationToken: cancellationToken);

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

    /// <summary>
    /// Get detailed information about a specific package.
    /// Tries each enabled feed until the package is found.
    /// </summary>
    public async Task<NuGetPackage?> GetPackageDetailsAsync(string packageId, CancellationToken cancellationToken = default)
    {
        var enabledSources = _sources.Where(s => s.IsEnabled).ToList();

        foreach (var source in enabledSources)
        {
            try
            {
                var result = await GetPackageDetailsOnSourceAsync(source, packageId, cancellationToken);
                if (result != null)
                    return result;
            }
            catch (Exception ex)
            {
                _logService?.LogWarning($"Failed to get details from '{source.Name}': {ex.Message}", "NuGet");
            }
        }

        return null;
    }

    private async Task<NuGetPackage?> GetPackageDetailsOnSourceAsync(
        NuGetSource source, string packageId, CancellationToken cancellationToken)
    {
        var endpoints = await ResolveEndpointsAsync(source, cancellationToken);

        // Use search API with exact packageid filter
        var url = $"{endpoints.SearchQueryService}?q=packageid:{Uri.EscapeDataString(packageId)}&take=1";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyAuth(request, source);

        var httpResponse = await _httpClient.SendAsync(request, cancellationToken);
        httpResponse.EnsureSuccessStatusCode();
        var response = await httpResponse.Content.ReadFromJsonAsync<NuGetSearchResponse>(cancellationToken: cancellationToken);

        var data = response?.Data?.FirstOrDefault();
        if (data == null)
            return null;

        // Fetch ALL versions from flat container API
        List<string> allVersions = new();
        try
        {
            var flatUrl = $"{endpoints.PackageBaseAddress}/{packageId.ToLowerInvariant()}/index.json";
            using var flatRequest = new HttpRequestMessage(HttpMethod.Get, flatUrl);
            ApplyAuth(flatRequest, source);

            var flatHttpResponse = await _httpClient.SendAsync(flatRequest, cancellationToken);
            flatHttpResponse.EnsureSuccessStatusCode();
            var flatResponse = await flatHttpResponse.Content.ReadFromJsonAsync<FlatContainerResponse>(cancellationToken: cancellationToken);

            if (flatResponse?.Versions != null && flatResponse.Versions.Count > 0)
            {
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
            _logService?.LogWarning($"Could not fetch all versions from flat container: {ex.Message}", "NuGet");
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

    /// <summary>
    /// Get the latest stable version of a package
    /// </summary>
    public async Task<string?> GetLatestVersionAsync(string packageId, CancellationToken cancellationToken = default)
    {
        var enabledSources = _sources.Where(s => s.IsEnabled).ToList();

        foreach (var source in enabledSources)
        {
            try
            {
                var endpoints = await ResolveEndpointsAsync(source, cancellationToken);
                var url = $"{endpoints.PackageBaseAddress}/{packageId.ToLowerInvariant()}/index.json";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                ApplyAuth(request, source);

                var httpResponse = await _httpClient.SendAsync(request, cancellationToken);
                if (!httpResponse.IsSuccessStatusCode) continue;

                var response = await httpResponse.Content.ReadFromJsonAsync<FlatContainerResponse>(cancellationToken: cancellationToken);
                if (response?.Versions == null || response.Versions.Count == 0)
                    continue;

                var stable = response.Versions
                    .Where(v => !v.Contains('-'))
                    .ToList();

                return stable.Count > 0 ? stable[^1] : response.Versions[^1];
            }
            catch (Exception ex)
            {
                _logService?.LogWarning($"Failed to check version on '{source.Name}' for {packageId}: {ex.Message}", "NuGet");
            }
        }

        return null;
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
    private async Task EnrichPackageWithCatalogDataAsync(NuGetSource source, NuGetPackage package, CancellationToken cancellationToken = default)
    {
        try
        {
            var endpoints = await ResolveEndpointsAsync(source, cancellationToken);
            if (string.IsNullOrEmpty(endpoints.RegistrationBaseUrl))
                return;
            var registrationUrl = $"{endpoints.RegistrationBaseUrl}/{package.Id.ToLowerInvariant()}/index.json";

            using var request = new HttpRequestMessage(HttpMethod.Get, registrationUrl);
            ApplyAuth(request, source);

            var httpResponse = await _httpClient.SendAsync(request, cancellationToken);
            httpResponse.EnsureSuccessStatusCode();
            var registrationResponse = await httpResponse.Content.ReadFromJsonAsync<NuGetRegistrationResponse>(cancellationToken: cancellationToken);

            if (registrationResponse?.Items == null || !registrationResponse.Items.Any())
                return;

            // Find the catalog entry matching this package's version.
            // Registration pages may be stubs (Items == null) requiring a separate fetch.
            NuGetCatalogEntry? catalogEntry = null;

            for (var i = registrationResponse.Items.Count - 1; i >= 0; i--)
            {
                var page = registrationResponse.Items[i];

                // If page items aren't inlined, fetch the full page
                if (page.Items == null && !string.IsNullOrEmpty(page.Url))
                {
                    try
                    {
                        using var pageRequest = new HttpRequestMessage(HttpMethod.Get, page.Url);
                        ApplyAuth(pageRequest, source);
                        var pageResponse = await _httpClient.SendAsync(pageRequest, cancellationToken);
                        pageResponse.EnsureSuccessStatusCode();
                        page = await pageResponse.Content.ReadFromJsonAsync<NuGetRegistrationPage>(cancellationToken: cancellationToken);
                    }
                    catch
                    {
                        continue;
                    }
                }

                if (page?.Items == null || page.Items.Count == 0)
                    continue;

                // Try to find the exact version match first
                var match = page.Items.FirstOrDefault(item =>
                    string.Equals(item.CatalogEntry?.Version, package.Version, StringComparison.OrdinalIgnoreCase));
                if (match?.CatalogEntry != null)
                {
                    catalogEntry = match.CatalogEntry;
                    break;
                }

                // Fall back to last item in the last page (latest)
                if (i == registrationResponse.Items.Count - 1)
                {
                    catalogEntry = page.Items.LastOrDefault()?.CatalogEntry;
                }
            }

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
        }
    }

    /// <summary>
    /// Resolve the V3 service endpoints for a feed.
    /// Uses hardcoded URLs for nuget.org as a fast path.
    /// </summary>
    private async Task<FeedEndpoints> ResolveEndpointsAsync(NuGetSource source, CancellationToken cancellationToken)
    {
        // Fast path for nuget.org
        if (IsNuGetOrg(source.Url))
        {
            return new FeedEndpoints
            {
                SearchQueryService = NuGetOrgSearchUrl,
                RegistrationBaseUrl = NuGetOrgRegistrationUrl,
                PackageBaseAddress = NuGetOrgFlatContainerUrl
            };
        }

        // Check cache
        if (_endpointCache.TryGetValue(source.Url, out var cached))
            return cached;

        // Resolve from service index
        var indexUrl = source.Url.TrimEnd('/');
        if (!indexUrl.EndsWith("/index.json"))
            indexUrl += "/index.json";

        using var request = new HttpRequestMessage(HttpMethod.Get, indexUrl);
        ApplyAuth(request, source);

        var httpResponse = await _httpClient.SendAsync(request, cancellationToken);
        httpResponse.EnsureSuccessStatusCode();
        var index = await httpResponse.Content.ReadFromJsonAsync<ServiceIndex>(cancellationToken: cancellationToken);

        var endpoints = new FeedEndpoints();

        if (index?.Resources != null)
        {
            endpoints.SearchQueryService = index.Resources
                .FirstOrDefault(r => r.Type?.StartsWith("SearchQueryService") == true)?.Id ?? string.Empty;
            endpoints.RegistrationBaseUrl = index.Resources
                .FirstOrDefault(r => r.Type?.StartsWith("RegistrationsBaseUrl") == true)?.Id?.TrimEnd('/') ?? string.Empty;
            endpoints.PackageBaseAddress = index.Resources
                .FirstOrDefault(r => r.Type?.StartsWith("PackageBaseAddress") == true)?.Id?.TrimEnd('/') ?? string.Empty;
        }

        if (string.IsNullOrEmpty(endpoints.SearchQueryService))
        {
            _logService?.LogWarning($"Could not resolve SearchQueryService for '{source.Name}' ({source.Url})", "NuGet");
        }

        _endpointCache[source.Url] = endpoints;
        return endpoints;
    }

    private static bool IsNuGetOrg(string url)
    {
        return url.Contains("api.nuget.org", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("nuget.org/v3", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyAuth(HttpRequestMessage request, NuGetSource source)
    {
        if (!string.IsNullOrEmpty(source.Username) && !string.IsNullOrEmpty(source.ClearTextPassword))
        {
            var credentials = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"{source.Username}:{source.ClearTextPassword}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

// NuGet V3 service index model
internal class ServiceIndex
{
    [JsonPropertyName("resources")]
    public List<ServiceIndexResource>? Resources { get; set; }
}

internal class ServiceIndexResource
{
    [JsonPropertyName("@id")]
    public string? Id { get; set; }

    [JsonPropertyName("@type")]
    public string? Type { get; set; }
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
    [JsonPropertyName("@id")]
    public string? Url { get; set; }

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
    [JsonPropertyName("version")]
    public string? Version { get; set; }

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
