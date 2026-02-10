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
    public long TotalDownloads { get; set; }
    public List<string> Versions { get; set; } = new();
    public DateTime? Published { get; set; }
    public List<string> Authors { get; set; } = new();
    public List<string> Tags { get; set; } = new();
}
