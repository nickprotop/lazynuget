namespace LazyNuGet.Models;

/// <summary>
/// Represents a .NET project with its metadata and packages
/// </summary>
public class ProjectInfo
{
    public string Name { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string TargetFramework { get; set; } = string.Empty;
    public List<string> TargetFrameworks { get; set; } = new();
    public string? SolutionName { get; set; }
    public List<PackageReference> Packages { get; set; } = new();
    public DateTime LastModified { get; set; }

    /// <summary>True when the project uses Central Package Management (ManagePackageVersionsCentrally=true).</summary>
    public bool IsCpmEnabled { get; set; }

    /// <summary>Full path to the governing Directory.Packages.props file, if CPM is active.</summary>
    public string? PropsFilePath { get; set; }

    /// <summary>True when the project uses the legacy packages.config format instead of PackageReference.</summary>
    public bool IsPackagesConfig { get; set; }

    /// <summary>Full path to the packages.config file, if the project uses legacy package management.</summary>
    public string? PackagesConfigPath { get; set; }

    public int OutdatedCount => Packages.Count(p => p.IsOutdated);
    public int VulnerableCount => Packages.Count(p => p.HasVulnerability);
}
