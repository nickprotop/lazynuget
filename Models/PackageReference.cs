using NuGet.Versioning;
using SharpConsoleUI.Parsing;

namespace LazyNuGet.Models;

/// <summary>
/// Indicates where the package version is declared.
/// </summary>
public enum VersionSource
{
    /// <summary>Version is declared inline in the .csproj file (normal mode).</summary>
    Inline,
    /// <summary>Version is declared centrally in Directory.Packages.props (CPM).</summary>
    Central,
    /// <summary>Version is a VersionOverride inside the .csproj, overriding the central declaration.</summary>
    Override
}

/// <summary>
/// Represents an installed NuGet package reference
/// </summary>
public class PackageReference
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;

    /// <summary>Where the version is declared — Inline, Central (props file), or Override.</summary>
    public VersionSource VersionSource { get; set; } = VersionSource.Inline;

    /// <summary>Full path to Directory.Packages.props when VersionSource is Central or Override.</summary>
    public string? PropsFilePath { get; set; }
    public string? LatestVersion { get; set; }
    public bool IsOutdated
    {
        get
        {
            if (string.IsNullOrEmpty(LatestVersion)) return false;
            if (!NuGetVersion.TryParse(LatestVersion, out var latest)) return false;

            if (NuGetVersion.TryParse(Version, out var current))
                return latest > current;

            // Floating version (e.g. "9.*"): check if latest exceeds the pinned segments.
            // "9.*" pins major=9, so 10.x is outdated. "9.0.*" pins major.minor=9.0, so 9.1 is outdated.
            if (VersionRange.TryParse(Version, out var range) && range.Float != null)
            {
                var min = range.Float.MinVersion;
                return range.Float.FloatBehavior switch
                {
                    NuGetVersionFloatBehavior.Minor => latest.Major > min.Major,
                    NuGetVersionFloatBehavior.Patch => latest.Major > min.Major || latest.Minor > min.Minor,
                    NuGetVersionFloatBehavior.Revision => latest.Major > min.Major || latest.Minor > min.Minor || latest.Patch > min.Patch,
                    _ => latest > min
                };
            }

            return false;
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
            if (IsOutdated) return $"[yellow]⚠ {MarkupParser.Escape(Version)} → {MarkupParser.Escape(LatestVersion ?? "")}[/]";
            return $"[green]✓ {MarkupParser.Escape(Version)} (latest)[/]";
        }
    }
}
