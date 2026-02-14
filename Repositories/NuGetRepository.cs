using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LazyNuGet.Models;
using NuGet.Versioning;

namespace LazyNuGet.Repositories;

/// <summary>
/// Repository for accessing NuGet.org API v3 - handles all HTTP calls to NuGet feeds.
/// This is the data access layer - no business logic should be here.
/// </summary>
public class NuGetRepository : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly List<NuGetSource> _sources;
    private readonly ConcurrentDictionary<string, FeedEndpoints> _endpointCache = new();

    // Hardcoded nuget.org endpoints (fast path — skip service index resolution)
    private const string NuGetOrgSearchUrl = "https://azuresearch-usnc.nuget.org/query";
    private const string NuGetOrgRegistrationUrl = "https://api.nuget.org/v3/registration5-semver1";
    private const string NuGetOrgFlatContainerUrl = "https://api.nuget.org/v3-flatcontainer";
    private const string NuGetOrgUrl = "https://api.nuget.org/v3/index.json";

    public NuGetRepository(List<NuGetSource>? sources = null)
    {
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

        // Ensure at least one enabled source
        if (!_sources.Any(s => s.IsEnabled))
        {
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
    /// Get the list of configured sources
    /// </summary>
    public IReadOnlyList<NuGetSource> GetSources() => _sources.AsReadOnly();

    /// <summary>
    /// Search for packages on a specific source
    /// </summary>
    public async Task<List<NuGetSearchData>> SearchPackagesOnSourceAsync(
        NuGetSource source,
        string query,
        int take,
        CancellationToken cancellationToken)
    {
        var endpoints = await ResolveEndpointsAsync(source, cancellationToken);
        var url = $"{endpoints.SearchQueryService}?q={Uri.EscapeDataString(query)}&take={take}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyAuth(request, source);

        var httpResponse = await _httpClient.SendAsync(request, cancellationToken);
        httpResponse.EnsureSuccessStatusCode();
        var response = await httpResponse.Content.ReadFromJsonAsync<NuGetSearchResponse>(cancellationToken: cancellationToken);

        return response?.Data ?? new List<NuGetSearchData>();
    }

    /// <summary>
    /// Get package details from a specific source
    /// </summary>
    public async Task<NuGetSearchData?> GetPackageDetailsOnSourceAsync(
        NuGetSource source,
        string packageId,
        CancellationToken cancellationToken)
    {
        var endpoints = await ResolveEndpointsAsync(source, cancellationToken);

        // Use search API with exact packageid filter
        var url = $"{endpoints.SearchQueryService}?q=packageid:{Uri.EscapeDataString(packageId)}&take=1";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyAuth(request, source);

        var httpResponse = await _httpClient.SendAsync(request, cancellationToken);
        httpResponse.EnsureSuccessStatusCode();
        var response = await httpResponse.Content.ReadFromJsonAsync<NuGetSearchResponse>(cancellationToken: cancellationToken);

        return response?.Data?.FirstOrDefault();
    }

    /// <summary>
    /// Get all versions of a package from flat container API
    /// </summary>
    public async Task<List<string>> GetAllVersionsAsync(
        NuGetSource source,
        string packageId,
        CancellationToken cancellationToken)
    {
        var endpoints = await ResolveEndpointsAsync(source, cancellationToken);
        var flatUrl = $"{endpoints.PackageBaseAddress}/{packageId.ToLowerInvariant()}/index.json";

        using var request = new HttpRequestMessage(HttpMethod.Get, flatUrl);
        ApplyAuth(request, source);

        var httpResponse = await _httpClient.SendAsync(request, cancellationToken);
        httpResponse.EnsureSuccessStatusCode();
        var flatResponse = await httpResponse.Content.ReadFromJsonAsync<FlatContainerResponse>(cancellationToken: cancellationToken);

        if (flatResponse?.Versions == null || flatResponse.Versions.Count == 0)
            return new List<string>();

        return flatResponse.Versions
            .Select(v => (version: v, parsed: NuGetVersion.TryParse(v, out var nv) ? nv : null))
            .Where(x => x.parsed != null)
            .OrderByDescending(x => x.parsed)
            .Select(x => x.version)
            .ToList();
    }

    /// <summary>
    /// Get catalog entry (detailed metadata) from registration API
    /// </summary>
    public async Task<NuGetCatalogEntry?> GetCatalogEntryAsync(
        NuGetSource source,
        string packageId,
        string version,
        CancellationToken cancellationToken)
    {
        var endpoints = await ResolveEndpointsAsync(source, cancellationToken);
        if (string.IsNullOrEmpty(endpoints.RegistrationBaseUrl))
            return null;

        var registrationUrl = $"{endpoints.RegistrationBaseUrl}/{packageId.ToLowerInvariant()}/index.json";

        using var request = new HttpRequestMessage(HttpMethod.Get, registrationUrl);
        ApplyAuth(request, source);

        var httpResponse = await _httpClient.SendAsync(request, cancellationToken);
        httpResponse.EnsureSuccessStatusCode();
        var registrationResponse = await httpResponse.Content.ReadFromJsonAsync<NuGetRegistrationResponse>(cancellationToken: cancellationToken);

        if (registrationResponse?.Items == null || !registrationResponse.Items.Any())
            return null;

        // Find the catalog entry matching the version
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
                string.Equals(item.CatalogEntry?.Version, version, StringComparison.OrdinalIgnoreCase));
            if (match?.CatalogEntry != null)
            {
                return match.CatalogEntry;
            }

            // Fall back to last item in the last page (latest)
            if (i == registrationResponse.Items.Count - 1)
            {
                return page.Items.LastOrDefault()?.CatalogEntry;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolve the V3 service endpoints for a feed
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

/// <summary>
/// Resolved endpoints for a NuGet V3 feed
/// </summary>
internal class FeedEndpoints
{
    public string SearchQueryService { get; set; } = string.Empty;
    public string RegistrationBaseUrl { get; set; } = string.Empty;
    public string PackageBaseAddress { get; set; } = string.Empty;
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

public class NuGetSearchData
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

public class NuGetPackageType
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class NuGetVulnerability
{
    [JsonPropertyName("advisoryUrl")]
    public string? AdvisoryUrl { get; set; }

    [JsonPropertyName("severity")]
    public string? Severity { get; set; }
}

public class NuGetDeprecation
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("reasons")]
    public List<string>? Reasons { get; set; }

    [JsonPropertyName("alternatePackage")]
    public NuGetAlternatePackage? AlternatePackage { get; set; }
}

public class NuGetAlternatePackage
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("range")]
    public string? Range { get; set; }
}

public class NuGetVersionData
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

public class NuGetCatalogEntry
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

public class NuGetDependencyGroup
{
    [JsonPropertyName("targetFramework")]
    public string? TargetFramework { get; set; }

    [JsonPropertyName("dependencies")]
    public List<NuGetDependency>? Dependencies { get; set; }
}

public class NuGetDependency
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
