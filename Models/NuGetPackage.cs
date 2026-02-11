namespace LazyNuGet.Models;

/// <summary>
/// Represents a package from NuGet.org
/// </summary>
public class NuGetPackage
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ProjectUrl { get; set; }
    public string? LicenseUrl { get; set; }
    public string? LicenseExpression { get; set; }
    public string? RepositoryUrl { get; set; }
    public long TotalDownloads { get; set; }
    public List<string> Versions { get; set; } = new();
    public DateTime? Published { get; set; }
    public List<string> Authors { get; set; } = new();
    public List<string> Tags { get; set; } = new();

    // Additional metadata
    public bool IsVerified { get; set; }
    public bool IsDeprecated { get; set; }
    public string? DeprecationMessage { get; set; }
    public string? AlternatePackageId { get; set; }
    public int VulnerabilityCount { get; set; }
    public List<string> TargetFrameworks { get; set; } = new();
    public long? PackageSize { get; set; }
    public string? ReleaseNotes { get; set; }
}
