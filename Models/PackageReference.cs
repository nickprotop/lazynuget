namespace LazyNuGet.Models;

/// <summary>
/// Represents an installed NuGet package reference
/// </summary>
public class PackageReference
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? LatestVersion { get; set; }
    public bool IsOutdated => !string.IsNullOrEmpty(LatestVersion) && LatestVersion != Version;
    public bool HasVulnerability { get; set; }
    public DateTime? LastUpdated { get; set; }

    public string DisplayStatus
    {
        get
        {
            if (HasVulnerability) return "[red]⚠ VULNERABLE[/]";
            if (IsOutdated) return $"[yellow]⚠ {Version} → {LatestVersion}[/]";
            return $"[green]✓ {Version} (latest)[/]";
        }
    }
}
