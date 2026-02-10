namespace LazyNuGet.Models;

/// <summary>
/// Represents a .NET project with its metadata and packages
/// </summary>
public class ProjectInfo
{
    public string Name { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string TargetFramework { get; set; } = string.Empty;
    public List<PackageReference> Packages { get; set; } = new();
    public DateTime LastModified { get; set; }

    public int OutdatedCount => Packages.Count(p => p.IsOutdated);
    public int VulnerableCount => Packages.Count(p => p.HasVulnerability);
}
