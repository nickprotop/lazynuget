using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using Spectre.Console;
using LazyNuGet.Models;
using LazyNuGet.UI.Utilities;
using HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment;
using VerticalAlignment = SharpConsoleUI.Layout.VerticalAlignment;

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
        Action onRestore,
        Action<List<PackageReference>> onUpdateSelected,
        Action<List<PackageReference>> onRemoveSelected,
        Action? onDeps = null)
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

        // Separator before toolbar
        var separator = Controls.Rule("[grey70]Quick Actions[/]");
        separator.Margin = new Margin(1, 0, 1, 0);
        controls.Add(separator);

        // Empty markup above toolbar for background extension
        var toolbarTop = Controls.Markup()
            .AddEmptyLine()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithBackgroundColor(ColorScheme.DetailsPanelBackground)
            .WithMargin(1, 0, 1, 0)
            .Build();
        controls.Add(toolbarTop);

        // Build bulk action buttons upfront (initially hidden) using closures
        // to capture the list control reference after it's created below.
        ListControl? packageList = null;

        var updateSelectedBtn = Controls.Button("[cyan1]Update Selected[/] [grey78](Ctrl+S)[/]")
            .OnClick((_, _) =>
            {
                if (packageList == null) return;
                var selected = packageList.GetCheckedItems()
                    .Select(i => (PackageReference)i.Tag!)
                    .Where(p => p.IsOutdated)
                    .ToList();
                if (selected.Any())
                    onUpdateSelected(selected);
            })
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .Build();

        var removeSelectedBtn = Controls.Button("[red1]Remove Selected[/] [grey78](Ctrl+Del)[/]")
            .OnClick((_, _) =>
            {
                if (packageList == null) return;
                var selected = packageList.GetCheckedItems()
                    .Select(i => (PackageReference)i.Tag!)
                    .ToList();
                if (selected.Any())
                    onRemoveSelected(selected);
            })
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .Build();

        var deselectAllBtn = Controls.Button("[grey78]Deselect All[/]")
            .OnClick((_, _) => packageList?.SetAllChecked(false))
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .Build();

        // Action toolbar (top 4 buttons only)
        var toolbar = BuildActionToolbar(
            outdatedPackages, onViewPackages, onUpdateAll, onRestore, onDeps);
        controls.Add(toolbar);

        // Empty markup below toolbar for background extension
        var toolbarBottom = Controls.Markup()
            .AddEmptyLine()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithBackgroundColor(ColorScheme.DetailsPanelBackground)
            .WithMargin(1, 0, 1, 0)
            .Build();
        controls.Add(toolbarBottom);

        // Separator after toolbar
        var separatorAfter = Controls.Rule();
        separatorAfter.Margin = new Margin(1, 0, 1, 0);
        controls.Add(separatorAfter);

        // Hint line above the checkbox list
        var hint = Controls.Markup()
            .AddLine("[grey50]Space: toggle · Tab: reach bulk actions[/]")
            .WithMargin(1, 0, 1, 0)
            .Build();
        controls.Add(hint);

        // Build checkbox package list
        packageList = BuildCheckboxPackageList(project);
        controls.Add(packageList);

        // Separator above secondary action bar (hidden until items are checked)
        var selectionSeparator = Controls.Rule();
        selectionSeparator.Margin = new Margin(1, 0, 1, 0);
        selectionSeparator.Visible = false;
        controls.Add(selectionSeparator);

        // Secondary action bar (hidden until items are checked)
        var selectionToolbar = BuildSelectionToolbar(updateSelectedBtn, removeSelectedBtn, deselectAllBtn);
        selectionToolbar.Visible = false;
        controls.Add(selectionToolbar);

        // Wire up CheckedItemsChanged to show/hide the secondary action bar
        packageList.CheckedItemsChanged += (_, _) =>
        {
            var anyChecked = packageList.GetCheckedItems().Any();
            selectionSeparator.Visible = anyChecked;
            selectionToolbar.Visible = anyChecked;
        };

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
            .WithWidth(40)
            .Build();
    }

    private static ListControl BuildCheckboxPackageList(ProjectInfo project)
    {
        if (project.Packages.Count == 0)
        {
            // Return an empty, non-interactive list with a placeholder
            var empty = ListControl.Create()
                .AddItem("[grey50]No packages installed[/]")
                .WithMargin(1, 1, 1, 0)
                .WithName("packageCheckboxList")
                .WithAlignment(HorizontalAlignment.Stretch)
                .WithVerticalAlignment(VerticalAlignment.Fill)
                .Selectable(false)
                .Build();
            return empty;
        }

        var builder = ListControl.Create()
            .WithCheckboxMode()
            .WithTitle($"Packages ({project.Packages.Count})")
            .WithMargin(1, 1, 1, 0)
            .WithName("packageCheckboxList")
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill);

        foreach (var pkg in project.Packages)
        {
            string statusPrefix = pkg.IsOutdated ? "[yellow]⚠[/] " : "[green]✓[/] ";
            string name = Markup.Escape(pkg.Id);
            string versionInfo = pkg.IsOutdated && !string.IsNullOrEmpty(pkg.LatestVersion)
                ? $"[grey70]{Markup.Escape(pkg.Version)}[/] [yellow]→ {Markup.Escape(pkg.LatestVersion)}[/]"
                : $"[grey70]{Markup.Escape(pkg.Version)}[/]";

            // Pad package name to create pseudo-columns
            string text = $"{statusPrefix}{name,-30} {versionInfo}";

            builder.AddItem(new ListItem(text) { Tag = pkg });
        }

        return builder.Build();
    }

    private static IWindowControl BuildActionToolbar(
        List<PackageReference> outdatedPackages,
        Action onViewPackages,
        Action onUpdateAll,
        Action onRestore,
        Action? onDeps)
    {
        // View Packages button
        var viewBtn = Controls.Button("[cyan1]View Packages[/] [grey78](Enter)[/]")
            .OnClick((s, e) => onViewPackages())
            .WithMargin(1, 0, 0, 0)
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .WithDisabledBackgroundColor(Color.Grey23)
            .WithDisabledForegroundColor(Color.Grey50)
            .Build();

        // Update All button
        var updateBtn = Controls.Button(outdatedPackages.Any() ? "[cyan1]Update All[/] [grey78](Ctrl+U)[/]" : "[grey50]All Up-to-Date[/]")
            .OnClick((s, e) => onUpdateAll())
            .Enabled(outdatedPackages.Any())
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .WithDisabledBackgroundColor(Color.Grey23)
            .WithDisabledForegroundColor(Color.Grey50)
            .Build();

        // Deps button
        var depsBtn = Controls.Button("[cyan1]Deps[/] [grey78](Ctrl+D)[/]")
            .OnClick((s, e) => onDeps?.Invoke())
            .Enabled(onDeps != null)
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .WithDisabledBackgroundColor(Color.Grey23)
            .WithDisabledForegroundColor(Color.Grey50)
            .Build();

        // Restore button
        var restoreBtn = Controls.Button("[cyan1]Restore[/] [grey78](Ctrl+R)[/]")
            .OnClick((s, e) => onRestore())
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .WithDisabledBackgroundColor(Color.Grey23)
            .WithDisabledForegroundColor(Color.Grey50)
            .Build();

        return Controls.Toolbar()
            .AddButton(viewBtn)
            .AddButton(updateBtn)
            .AddButton(depsBtn)
            .AddButton(restoreBtn)
            .WithSpacing(2)
            .WithWrap()
            .WithBackgroundColor(ColorScheme.DetailsPanelBackground)
            .WithMargin(1, 0, 1, 0)
            .Build();
    }

    private static IWindowControl BuildSelectionToolbar(ButtonControl updateSelectedBtn, ButtonControl removeSelectedBtn, ButtonControl deselectAllBtn)
    {
        return Controls.Toolbar()
            .AddButton(updateSelectedBtn)
            .AddButton(removeSelectedBtn)
            .AddButton(deselectAllBtn)
            .WithSpacing(2)
            .WithWrap()
            .WithBackgroundColor(ColorScheme.DetailsPanelBackground)
            .WithMargin(1, 0, 1, 0)
            .Build();
    }

    internal static string ShortenPath(string path)
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
