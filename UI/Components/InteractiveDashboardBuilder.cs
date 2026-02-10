using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using Spectre.Console;
using LazyNuGet.Models;
using HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment;

namespace LazyNuGet.UI.Components;

/// <summary>
/// Builder for interactive project dashboard with real button controls
/// </summary>
public static class InteractiveDashboardBuilder
{
    public static List<IWindowControl> BuildInteractiveDashboard(
        ProjectInfo project,
        List<PackageReference> outdatedPackages,
        Action onViewPackages,
        Action onUpdateAll,
        Action onRestore)
    {
        var controls = new List<IWindowControl>();

        // Project header
        var header = Controls.Markup()
            .AddLine($"[cyan1 bold]Project: {Markup.Escape(project.Name)}[/]")
            .AddLine($"[grey70]Path: {Markup.Escape(ShortenPath(project.FilePath))}[/]")
            .AddLine($"[grey70]Framework: {project.TargetFramework}[/]")
            .AddEmptyLine()
            .WithMargin(1, 1, 0, 0)
            .Build();
        controls.Add(header);

        // Stats cards
        var statsCard = BuildStatsCard(project, outdatedPackages);
        controls.Add(statsCard);

        // Package summary
        var summary = BuildPackageSummary(project, outdatedPackages);
        controls.Add(summary);

        // Interactive Quick Actions - Title + Real buttons!
        var actionTitle = BuildActionButtons(project, outdatedPackages, onViewPackages, onUpdateAll, onRestore);
        controls.Add(actionTitle);

        // Add actual button controls
        var buttons = GetActionButtons(outdatedPackages, onViewPackages, onUpdateAll, onRestore);
        controls.AddRange(buttons);

        return controls;
    }

    private static MarkupControl BuildStatsCard(ProjectInfo project, List<PackageReference> outdatedPackages)
    {
        var total = project.Packages.Count;
        var outdated = outdatedPackages.Count;
        var vulnerable = project.VulnerableCount;

        return Controls.Markup()
            .AddLine("┌─────────┬─────────┬─────────┐")
            .AddLine("│ Total   │Outdated │  Vuln   │")
            .AddLine("│         │         │         │")
            .AddLine($"│  [cyan1]{total,3}[/]    │  [yellow]{outdated,3}[/]    │  [red]{vulnerable,3}[/]    │")
            .AddLine("│         │         │         │")
            .AddLine("│ 📦      │ ⚠       │ ✓       │")
            .AddLine("└─────────┴─────────┴─────────┘")
            .AddEmptyLine()
            .WithMargin(1, 0, 0, 0)
            .Build();
    }

    private static MarkupControl BuildPackageSummary(ProjectInfo project, List<PackageReference> outdatedPackages)
    {
        var builder = Controls.Markup();

        if (project.Packages.Count > 0)
        {
            builder.AddLine("[grey70 bold]Installed Packages:[/]");
            var topPackages = project.Packages.Take(5);
            foreach (var pkg in topPackages)
            {
                var statusIndicator = pkg.IsOutdated ? "[yellow]⚠[/]" : "[green]✓[/]";
                builder.AddLine($"{statusIndicator} [grey70]{Markup.Escape(pkg.Id)} {pkg.Version}[/]");
            }

            if (project.Packages.Count > 5)
            {
                builder.AddLine($"[grey50]... and {project.Packages.Count - 5} more[/]");
            }
            builder.AddEmptyLine();
        }

        // Needs Attention section
        if (outdatedPackages.Any())
        {
            builder.AddLine("[yellow bold]Needs Attention:[/]");
            foreach (var pkg in outdatedPackages.Take(5))
            {
                builder.AddLine($"[yellow]⚠ {Markup.Escape(pkg.Id)}[/]");
                builder.AddLine($"  [grey70]{pkg.Version} → {pkg.LatestVersion} available[/]");
            }

            if (outdatedPackages.Count > 5)
            {
                builder.AddLine($"[grey50]... and {outdatedPackages.Count - 5} more outdated[/]");
            }
            builder.AddEmptyLine();
        }
        else if (project.Packages.Count > 0)
        {
            builder.AddLine("[green bold]✓ All packages up-to-date![/]");
            builder.AddEmptyLine();
        }

        return builder.WithMargin(1, 0, 0, 0).Build();
    }

    private static IWindowControl BuildActionButtons(
        ProjectInfo project,
        List<PackageReference> outdatedPackages,
        Action onViewPackages,
        Action onUpdateAll,
        Action onRestore)
    {
        // Title
        return Controls.Markup()
            .AddLine("[cyan1 bold]Quick Actions:[/]")
            .AddLine("")
            .AddLine("[grey70]Press buttons below or use keyboard shortcuts:[/]")
            .WithMargin(1, 0, 0, 1)
            .Build();
    }

    public static List<IWindowControl> GetActionButtons(
        List<PackageReference> outdatedPackages,
        Action onViewPackages,
        Action onUpdateAll,
        Action onRestore)
    {
        var buttons = new List<IWindowControl>();

        // View Packages button
        buttons.Add(Controls.Button("📦 View Packages (Enter)")
            .WithAlignment(HorizontalAlignment.Left)
            .WithMargin(1, 0, 0, 0)
            .OnClick((s, e) => onViewPackages())
            .Build());

        // Update All button
        buttons.Add(Controls.Button(outdatedPackages.Any() ? "⚡ Update All Outdated (Ctrl+U)" : "✓ All Up-to-Date")
            .WithAlignment(HorizontalAlignment.Left)
            .WithMargin(1, 0, 0, 0)
            .OnClick((s, e) => onUpdateAll())
            .Enabled(outdatedPackages.Any())
            .Build());

        // Restore button
        buttons.Add(Controls.Button("🔄 Restore Packages (Ctrl+R)")
            .WithAlignment(HorizontalAlignment.Left)
            .WithMargin(1, 0, 0, 1)
            .OnClick((s, e) => onRestore())
            .Build());

        return buttons;
    }

    private static string ShortenPath(string path)
    {
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
