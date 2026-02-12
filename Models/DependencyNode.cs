namespace LazyNuGet.Models;

public class DependencyNode
{
    public string PackageId { get; set; } = string.Empty;
    public string ResolvedVersion { get; set; } = string.Empty;
    public string? RequestedVersion { get; set; }
    public bool IsTransitive { get; set; }
}

public class ProjectDependencyTree
{
    public string ProjectName { get; set; } = string.Empty;
    public string TargetFramework { get; set; } = string.Empty;
    public List<DependencyNode> TopLevelPackages { get; set; } = new();
    public List<DependencyNode> TransitivePackages { get; set; } = new();
}
