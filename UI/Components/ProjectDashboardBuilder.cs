using LazyNuGet.Models;
using Spectre.Console;

namespace LazyNuGet.UI.Components;

/// <summary>
/// Builder for project dashboard view with stats cards and package summaries
/// </summary>
public static class ProjectDashboardBuilder
{
    public static List<string> BuildDashboard(ProjectInfo project, List<PackageReference>? outdatedPackages = null)
    {
        outdatedPackages ??= new List<PackageReference>();

        var lines = new List<string>();

        // Project header
        lines.Add($"[cyan1 bold]Project: {Markup.Escape(project.Name)}[/]");
        lines.Add($"[grey70]Path: {Markup.Escape(ShortenPath(project.FilePath))}[/]");
        lines.Add($"[grey70]Framework: {project.TargetFramework}[/]");
        lines.Add("");

        // Stats cards (3 columns using fixed-width formatting)
        var total = project.Packages.Count;
        var outdated = outdatedPackages.Count;
        var vulnerable = project.VulnerableCount;

        lines.Add("┌─────────┬─────────┬─────────┐");
        lines.Add("│ Total   │Outdated │  Vuln   │");
        lines.Add("│         │         │         │");
        lines.Add($"│  [cyan1]{total,3}[/]    │  [yellow]{outdated,3}[/]    │  [red]{vulnerable,3}[/]    │");
        lines.Add("│         │         │         │");
        lines.Add("│ 📦      │ ⚠       │ ✓       │");
        lines.Add("└─────────┴─────────┴─────────┘");
        lines.Add("");

        // Recently Updated section (placeholder - will need actual date tracking)
        if (project.Packages.Count > 0)
        {
            lines.Add("[grey70 bold]Installed Packages:[/]");
            var topPackages = project.Packages.Take(5);
            foreach (var pkg in topPackages)
            {
                var statusIndicator = pkg.IsOutdated ? "[yellow]⚠[/]" : "[green]✓[/]";
                lines.Add($"{statusIndicator} [grey70]{Markup.Escape(pkg.Id)} {pkg.Version}[/]");
            }

            if (project.Packages.Count > 5)
            {
                lines.Add($"[grey50]... and {project.Packages.Count - 5} more[/]");
            }
            lines.Add("");
        }

        // Needs Attention section
        if (outdatedPackages.Any())
        {
            lines.Add("[yellow bold]Needs Attention:[/]");
            foreach (var pkg in outdatedPackages.Take(5))
            {
                lines.Add($"[yellow]⚠ {Markup.Escape(pkg.Id)}[/]");
                lines.Add($"  [grey70]{pkg.Version} → {pkg.LatestVersion} available[/]");
            }

            if (outdatedPackages.Count > 5)
            {
                lines.Add($"[grey50]... and {outdatedPackages.Count - 5} more outdated[/]");
            }
            lines.Add("");
        }
        else if (project.Packages.Count > 0)
        {
            lines.Add("[green bold]✓ All packages up-to-date![/]");
            lines.Add("");
        }

        // Quick Actions
        lines.Add("[cyan1 bold]Quick Actions:[/]");
        lines.Add("[grey70][Enter] View packages[/]");
        if (outdatedPackages.Any())
        {
            lines.Add("[grey70][Ctrl+U] Update all outdated[/]");
        }
        lines.Add("[grey70][Ctrl+R] Restore packages[/]");

        return lines;
    }

    private static string ShortenPath(string path)
    {
        // Shorten path to fit in dashboard - show only filename or last 2 directories
        try
        {
            var parts = path.Split(Path.DirectorySeparatorChar);
            if (parts.Length > 3)
            {
                return $"...{Path.DirectorySeparatorChar}{parts[^2]}{Path.DirectorySeparatorChar}{parts[^1]}";
            }
            return path;
        }
        catch
        {
            return path;
        }
    }
}
