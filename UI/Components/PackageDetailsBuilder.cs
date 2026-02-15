using LazyNuGet.Models;
using Spectre.Console;

namespace LazyNuGet.UI.Components;

/// <summary>
/// Builder for package details view
/// This will be fully enhanced in Phase 3 with NuGet API integration
/// </summary>
public static class PackageDetailsBuilder
{
    public static List<string> BuildDetails(PackageReference package, NuGetPackage? nugetData = null)
    {
        var lines = new List<string>();

        // Package header
        lines.Add($"[cyan1 bold]Package: {Markup.Escape(package.Id)}[/]");
        lines.Add($"[grey70]Installed: {Markup.Escape(package.Version)}[/]");
        lines.Add("");

        // Status section
        if (package.HasVulnerability)
        {
            lines.Add($"[red bold]⚠ SECURITY VULNERABILITY[/]");
            lines.Add("");
        }

        if (package.IsOutdated && !string.IsNullOrEmpty(package.LatestVersion))
        {
            lines.Add($"[yellow bold]Update Available[/]");
            lines.Add($"[grey70]Latest Version: {Markup.Escape(package.LatestVersion)}[/]");
            lines.Add("");
        }
        else if (!package.IsOutdated)
        {
            lines.Add($"[green bold]✓ Up to date[/]");
            lines.Add("");
        }

        // NuGet.org data section (Phase 3)
        if (nugetData != null)
        {
            if (!string.IsNullOrEmpty(nugetData.Description))
            {
                lines.Add($"[grey70 bold]Description:[/]");
                lines.Add($"[grey70]{Markup.Escape(WrapText(nugetData.Description, 50))}[/]");
                lines.Add("");
            }

            if (nugetData.TotalDownloads > 0)
            {
                lines.Add($"[grey70]Downloads: {FormatDownloads(nugetData.TotalDownloads)}[/]");
            }

            if (nugetData.Published.HasValue)
            {
                lines.Add($"[grey70]Published: {nugetData.Published.Value:yyyy-MM-dd}[/]");
            }

            if (!string.IsNullOrEmpty(nugetData.ProjectUrl))
            {
                lines.Add($"[grey70]Project URL: {Markup.Escape(nugetData.ProjectUrl)}[/]");
            }

            if (nugetData.Versions.Any())
            {
                lines.Add("");
                lines.Add($"[grey70 bold]Available Versions:[/]");
                foreach (var version in nugetData.Versions.Take(5))
                {
                    var indicator = version == package.Version ? "◄ installed" : "";
                    lines.Add($"[grey70]{Markup.Escape(version)} {indicator}[/]");
                }
                if (nugetData.Versions.Count > 5)
                {
                    lines.Add($"[grey50]... and {nugetData.Versions.Count - 5} more[/]");
                }
            }
        }
        else
        {
            lines.Add("[grey50 italic]Loading details from NuGet.org...[/]");
            lines.Add("[grey50 italic](Phase 3: NuGet API integration)[/]");
        }

        lines.Add("");
        lines.Add("[cyan1 bold]Actions:[/]");
        if (package.IsOutdated)
        {
            lines.Add("[grey70][Ctrl+U] Update to latest[/]");
        }
        lines.Add("[grey70][Ctrl+X] Remove package[/]");

        return lines;
    }

    internal static string FormatDownloads(long downloads)
    {
        if (downloads >= 1_000_000_000)
            return $"{downloads / 1_000_000_000.0:F1}B";
        if (downloads >= 1_000_000)
            return $"{downloads / 1_000_000.0:F1}M";
        if (downloads >= 1_000)
            return $"{downloads / 1_000.0:F1}K";
        return downloads.ToString();
    }

    internal static string WrapText(string text, int maxLength)
    {
        if (text.Length <= maxLength)
            return text;

        // Simple word wrap - just truncate for now
        return text.Substring(0, maxLength) + "...";
    }
}
