namespace LazyNuGet.Models;

/// <summary>
/// Represents an installed NuGet package reference
/// </summary>
public class PackageReference
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? LatestVersion { get; set; }
    public bool IsOutdated
    {
        get
        {
            if (string.IsNullOrEmpty(LatestVersion)) return false;
            if (!NuGet.Versioning.NuGetVersion.TryParse(Version, out var current)) return false;
            if (!NuGet.Versioning.NuGetVersion.TryParse(LatestVersion, out var latest)) return false;
            return latest > current;
        }
    }
    public bool HasVulnerability { get; set; }
    public List<VulnerabilityInfo> Vulnerabilities { get; set; } = new();
    public string? LatestPrereleaseVersion { get; set; }
    public bool HasNewerPrerelease =>
        !string.IsNullOrEmpty(LatestPrereleaseVersion) &&
        LatestPrereleaseVersion != LatestVersion &&
        !IsOutdated; // only show prerelease hint when stable track is current
    public DateTime? LastUpdated { get; set; }

    public string DisplayStatus
    {
        get
        {
            if (HasVulnerability) return "[red]⚠ VULNERABLE[/]";
            if (IsOutdated) return $"[yellow]⚠ {Spectre.Console.Markup.Escape(Version)} → {Spectre.Console.Markup.Escape(LatestVersion ?? "")}[/]";
            return $"[green]✓ {Spectre.Console.Markup.Escape(Version)} (latest)[/]";
        }
    }
}
