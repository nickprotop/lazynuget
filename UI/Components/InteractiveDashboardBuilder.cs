using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using Spectre.Console;
using LazyNuGet.Models;
using LazyNuGet.UI.Utilities;
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

        // Separator before toolbar
        var separator = Controls.Rule("[grey70]Quick Actions[/]");
        separator.Margin = new Margin(1, 0, 1, 0);
        controls.Add(separator);

        // Empty markup above toolbar for background extension
        var toolbarTop = Controls.Markup()
            .AddEmptyLine()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithBackgroundColor(ColorScheme.StatusBarBackground)
            .WithMargin(1, 0, 1, 0)
            .Build();
        controls.Add(toolbarTop);

        // Action toolbar
        var toolbar = BuildActionToolbar(outdatedPackages, onViewPackages, onUpdateAll, onRestore);
        controls.Add(toolbar);

        // Empty markup below toolbar for background extension
        var toolbarBottom = Controls.Markup()
            .AddEmptyLine()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithBackgroundColor(ColorScheme.StatusBarBackground)
            .WithMargin(1, 0, 1, 0)
            .Build();
        controls.Add(toolbarBottom);

        // Separator after toolbar
        var separatorAfter = Controls.Rule();
        separatorAfter.Margin = new Margin(1, 0, 1, 0);
        controls.Add(separatorAfter);

        return controls;
    }

    private static IWindowControl BuildStatsCard(ProjectInfo project, List<PackageReference> outdatedPackages)
    {
        var total = project.Packages.Count;
        var outdated = outdatedPackages.Count;
        var vulnerable = project.VulnerableCount;

        return Controls.Table()
            .AddColumn("📦 [cyan1]Total[/]", Spectre.Console.Justify.Center, 11)
            .AddColumn("⚠ [yellow]Outdated[/]", Spectre.Console.Justify.Center, 11)
            .AddColumn("✓ [grey70]Vuln[/]", Spectre.Console.Justify.Center, 11)
            .AddRow($"[cyan1 bold]{total}[/]", $"[yellow bold]{outdated}[/]", $"[red bold]{vulnerable}[/]")
            .WithBorderColor(Color.Grey50)
            .SingleLine()
            .ShowHeader()
            .WithHeaderColors(Color.Grey70, Color.Black)
            .WithBackgroundColor(null)
            .WithMargin(1, 0, 0, 0)
            .WithHorizontalAlignment(HorizontalAlignment.Left)
            .Build();
    }

    private static IWindowControl BuildPackageSummary(ProjectInfo project, List<PackageReference> outdatedPackages)
    {
        if (project.Packages.Count == 0)
        {
            return Controls.Markup()
                .AddLine("[grey50]No packages installed[/]")
                .AddEmptyLine()
                .WithMargin(1, 0, 0, 0)
                .Build();
        }

        // Create table with proper TableControl
        var table = Controls.Table()
            .AddColumn("St", Spectre.Console.Justify.Center, 4)
            .AddColumn("Package", Spectre.Console.Justify.Left, 30)
            .AddColumn("Version", Spectre.Console.Justify.Left, 22)
            .WithBorderColor(Color.Grey50)
            .SingleLine()
            .ShowHeader()
            .WithHeaderColors(Color.Grey70, Color.Black)
            .WithBackgroundColor(null)
            .WithMargin(1, 0, 0, 0)
            .WithHorizontalAlignment(HorizontalAlignment.Left);

        // Add package rows
        foreach (var pkg in project.Packages)
        {
            string status = pkg.IsOutdated ? "[yellow]⚠[/]" : "[green]✓[/]";
            string packageName = Markup.Escape(pkg.Id);

            // Truncate package name if too long (max 28 chars to fit in column)
            if (packageName.Length > 28)
            {
                packageName = packageName.Substring(0, 25) + "...";
            }

            string versionInfo;
            if (pkg.IsOutdated && !string.IsNullOrEmpty(pkg.LatestVersion))
            {
                versionInfo = $"[grey70]{pkg.Version}[/] [yellow]→ {pkg.LatestVersion}[/]";
            }
            else
            {
                versionInfo = $"[grey70]{pkg.Version}[/]";
            }

            table.AddRow(status, $"[grey70]{packageName}[/]", versionInfo);
        }

        return table.Build();
    }

    private static IWindowControl BuildActionToolbar(
        List<PackageReference> outdatedPackages,
        Action onViewPackages,
        Action onUpdateAll,
        Action onRestore)
    {
        // View Packages button
        var viewBtn = Controls.Button("[grey93]View Packages[/] [grey50](Enter)[/]")
            .OnClick((s, e) => onViewPackages())
            .WithMargin(1, 0, 0, 0)
            .Build();

        // Update All button
        var updateBtn = Controls.Button(outdatedPackages.Any() ? "[grey93]Update All[/] [grey50](Ctrl+U)[/]" : "[grey50]All Up-to-Date[/]")
            .OnClick((s, e) => onUpdateAll())
            .Enabled(outdatedPackages.Any())
            .Build();

        // Restore button
        var restoreBtn = Controls.Button("[grey93]Restore[/] [grey50](Ctrl+R)[/]")
            .OnClick((s, e) => onRestore())
            .Build();

        return Controls.Toolbar()
            .AddButton(viewBtn)
            .AddButton(updateBtn)
            .AddButton(restoreBtn)
            .WithSpacing(2)
            .WithBackgroundColor(ColorScheme.StatusBarBackground)
            .WithMargin(1, 0, 1, 0)
            .Build();
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
