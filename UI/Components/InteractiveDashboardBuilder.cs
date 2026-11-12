using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using LazyNuGet.Models;
using LazyNuGet.UI.Utilities;
using SharpConsoleUI.Parsing;

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
        Action? onDeps = null,
        Action<PackageReference>? onOpenPackage = null)
    {
        var controls = new List<IWindowControl>();

        // Project header
        var frameworkDisplay = project.TargetFrameworks.Any()
            ? string.Join(" | ", project.TargetFrameworks)
            : project.TargetFramework;
        // Name and framework on one line: the framework is metadata about the project, not a
        // third fact of equal weight. Path below, dimmed, because it is for confirming you are
        // on the right project rather than for reading.
        var header = Controls.Markup()
            .AddLine($"[cyan1 bold]{MarkupParser.Escape(project.Name)}[/]  [grey50]{MarkupParser.Escape(frameworkDisplay)}[/]")
            .AddLine($"[grey50]{MarkupParser.Escape(ShortenPath(project.FilePath))}[/]")
            .WithMargin(1, 1, 0, 0)
            .Build();
        controls.Add(header);

        // Stats as a single line. This was a 3-column TableControl of three single numbers,
        // five rows tall and 39 cells wide in a 133-wide pane: a toy table where a strip says
        // the same thing. Counts are coloured only when they are non-zero, so a clean project
        // reads as quiet rather than as three green badges.
        var statsStrip = BuildStatsStrip(project, outdatedPackages);
        controls.Add(statsStrip);

        // CPM migration hint: shown whenever any package still has an inline Version attribute,
        // including projects that are under a CPM folder but haven't had their versions centralized yet.
        if (!project.IsPackagesConfig && project.Packages.Any(p => p.VersionSource == VersionSource.Inline))
        {
            var cpmHint = Controls.Markup()
                .AddLine("[grey50]💡 Press [/][cyan1]Ctrl+G[/][grey50] to centralize versions with CPM[/]")
                .WithMargin(1, 0, 0, 1)
                .Build();
            controls.Add(cpmHint);
        }

        // Separator before toolbar
        var separator = Controls.Rule("[grey70]Quick Actions[/]");
        separator.Margin = new Margin(1, 0, 1, 0);
        controls.Add(separator);


        // Build bulk action buttons upfront (initially hidden) using closures
        // to capture the table reference after it's created below.
        TableControl? packageTable = null;
        var packageSource = new PackageTableDataSource(project.Packages);

        var updateSelectedBtn = Controls.Button("[cyan1]Update Selected[/] [grey78](Ctrl+S)[/]")
            .OnClick((_, _) =>
            {
                if (packageTable == null) return;
                var selected = SelectedPackages(packageTable, packageSource)
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
                if (packageTable == null) return;
                var selected = SelectedPackages(packageTable, packageSource);
                if (selected.Any())
                    onRemoveSelected(selected);
            })
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .Build();

        var deselectAllBtn = Controls.Button("[grey78]Deselect All[/]")
            .OnClick((_, _) => packageTable?.ClearSelection())
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .Build();

        // Action toolbar (top 4 buttons only)
        var toolbar = BuildActionToolbar(
            outdatedPackages, onViewPackages, onUpdateAll, onRestore, onDeps);
        controls.Add(toolbar);

        // No rule after the toolbar: the table's own frame below is the next boundary, and a
        // rule plus a frame two rows apart read as clutter rather than structure.

        // One dim hint line above the table. The CPM warning is a real caveat, so it stays,
        // but it reads as a second clause rather than a second banner.
        var hintBuilder = Controls.Markup()
            .AddLine("[grey50]Space toggles · Tab reaches the bulk actions[/]");
        if (project.IsCpmEnabled)
            hintBuilder.AddLine("[grey50]CPM: updates write to [/][grey70]Directory.Packages.props[/][grey50] and affect every project[/]");
        var hint = hintBuilder.WithMargin(1, 1, 1, 0).Build();
        controls.Add(hint);

        // Build the package table
        packageTable = BuildPackageTable(project, packageSource);
        controls.Add(packageTable);

        // Separator above secondary action bar (hidden until items are checked)
        var selectionSeparator = Controls.Rule();
        selectionSeparator.Margin = new Margin(1, 0, 1, 0);
        selectionSeparator.Visible = false;
        controls.Add(selectionSeparator);

        // Secondary action bar (hidden until items are checked)
        var selectionToolbar = BuildSelectionToolbar(updateSelectedBtn, removeSelectedBtn, deselectAllBtn);
        selectionToolbar.Visible = false;
        controls.Add(selectionToolbar);

        // Show/hide the secondary action bar with the selection. Checks ARE the selection in
        // checkbox mode, so MultiSelectionChanged is the signal; its payload is the count.
        packageTable.MultiSelectionChanged += (_, count) =>
        {
            bool anyChecked = count > 0;
            selectionSeparator.Visible = anyChecked;
            selectionToolbar.Visible = anyChecked;
        };

        // Enter or double-click opens the package in the packages view. RowActivated covers
        // both, where the old MouseDoubleClick wiring covered only the mouse.
        if (onOpenPackage != null)
        {
            packageTable.RowActivated += (_, rowIndex) =>
            {
                if (packageSource.PackageAt(rowIndex) is PackageReference pkg)
                    onOpenPackage(pkg);
            };
        }

        return controls;
    }

    /// <summary>
    /// The project's counts, as one line.
    /// </summary>
    private static IWindowControl BuildStatsStrip(ProjectInfo project, List<PackageReference> outdatedPackages)
    {
        int total = project.Packages.Count;
        int outdated = outdatedPackages.Count;
        int vulnerable = project.VulnerableCount;

        // A zero stays grey: only a count that needs attention earns a colour.
        string outdatedPart = outdated > 0
            ? $"[yellow bold]{outdated}[/][grey70] outdated[/]"
            : "[grey50]0 outdated[/]";
        string vulnerablePart = vulnerable > 0
            ? $"[red bold]{vulnerable}[/][grey70] vulnerable[/]"
            : "[grey50]0 vulnerable[/]";

        return Controls.Markup()
            .AddLine($"[cyan1 bold]{total}[/][grey70] packages[/]    {outdatedPart}    {vulnerablePart}")
            .WithMargin(1, 1, 1, 1)
            .Build();
    }

    /// <summary>
    /// The package list, as a real table over a data source.
    /// </summary>
    /// <remarks>
    /// This was a <c>ListControl</c> whose <c>ItemFormatter</c> hand-rolled a three-column
    /// layout: a magic <c>listOverhead = 9</c>, <c>PadRight</c> for alignment, manual
    /// truncation with an ellipsis, and <c>string.Length</c> as a display width. Status, name
    /// and version are columns, so they are columns now, measured by the table.
    ///
    /// In checkbox mode checks ARE the multi-selection: a data source has no
    /// <see cref="TableRow"/> objects to carry <c>IsChecked</c>, so the checkbox column paints
    /// from the selected indices and <see cref="SelectedPackages"/> maps those back through the
    /// source. Space toggles a row; Ctrl+A takes all.
    /// </remarks>
    private static TableControl BuildPackageTable(ProjectInfo project, PackageTableDataSource source)
    {
        int outdated = project.Packages.Count(p => p.IsOutdated);
        string title = outdated > 0
            ? $" Packages ({project.Packages.Count}) \u00b7 {outdated} outdated "
            : $" Packages ({project.Packages.Count}) ";

        var table = Controls.Table()
            .WithDataSource(source)
            .WithMargin(1, 1, 1, 0)
            .WithName("packageTable")
            .WithHorizontalAlignment(HorizontalAlignment.Stretch)
            // No Fill and no explicit height: the control measures title + border + header +
            // rows itself. Fill gave two packages twenty-five empty bordered rows, and my own
            // chrome arithmetic was off by one and ate a data row. Let it measure.
            // Chrome: a framed, titled table reads as one object. Borderless with a bare
            // header left it floating in the pane with nothing tying the columns together.
            .Rounded()
            .WithBorderColor(ColorScheme.BorderColor)
            .WithTitle(title, TextJustification.Left)
            .ShowHeader()
            // The header sits one surface step forward from the table body, which in turn sits
            // forward from the pane: three steps so the eye reads depth rather than one flat
            // field of grey.
            .WithHeaderColors(Color.Grey93, ColorScheme.HeaderBackground)
            .WithColors(Color.Grey85, ColorScheme.InsetBackground)
            // Vertical rules between columns, so a wide pane still reads as a grid rather
            // than words adrift in whitespace.
            .WithColumnSeparator('\u2502', ColorScheme.RuleColor, padded: true)
            .Interactive()
            .WithSorting()
            .WithCheckboxMode()
            .WithMultiSelect()
            .Build();

        return table;
    }

    /// <summary>
    /// The packages the user has ticked, read from the multi-selection.
    /// </summary>
    /// <remarks>
    /// <c>GetCheckedRows()</c> cannot serve here: it reads <c>TableRow.IsChecked</c>, and a
    /// table over a data source has no row objects, so it always comes back empty.
    /// </remarks>
    private static List<PackageReference> SelectedPackages(
        TableControl table, PackageTableDataSource source)
    {
        return table.GetSelectedIndices()
            .Select(source.PackageAt)
            .OfType<PackageReference>()
            .ToList();
    }

    private static IWindowControl BuildActionToolbar(
        List<PackageReference> outdatedPackages,
        Action onViewPackages,
        Action onUpdateAll,
        Action onRestore,
        Action? onDeps)
    {
        // "View Packages" said nothing about what it does: the dashboard already SHOWS the
        // packages, so the button appeared to be a no-op. What it actually does is move the
        // sidebar into this project so you can walk its packages one at a time and read each
        // one's details. Name the destination, not the noun.
        var viewBtn = Controls.Button("[cyan1]Browse packages[/] [grey78](Enter)[/]")
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

    public static List<IWindowControl> BuildLegacyDashboard(
        ProjectInfo project,
        Action onViewPackages,
        Action? onMigrate = null)
    {
        var controls = new List<IWindowControl>();

        // Project header with legacy badge
        var frameworkDisplay = project.TargetFrameworks.Any()
            ? string.Join(" | ", project.TargetFrameworks)
            : project.TargetFramework;
        var header = Controls.Markup()
            .AddLine($"[cyan1 bold]Project: {MarkupParser.Escape(project.Name)}[/] [grey50][legacy][/]")
            .AddLine($"[grey70]Path: {MarkupParser.Escape(ShortenPath(project.FilePath))}[/]")
            .AddLine($"[grey70]Framework: {MarkupParser.Escape(frameworkDisplay)}[/]")
            .AddEmptyLine()
            .WithMargin(1, 1, 0, 0)
            .Build();
        controls.Add(header);

        // Package count info
        var statsCard = Controls.Markup()
            .AddLine($"[grey70]📦 {project.Packages.Count} packages (read-only, packages.config)[/]")
            .WithMargin(1, 0, 0, 0)
            .Build();
        controls.Add(statsCard);

        // Separator
        var separator = Controls.Rule("[yellow]Migration Required[/]");
        separator.Margin = new Margin(1, 1, 1, 0);
        controls.Add(separator);

        // Migration notice
        var notice = Controls.Markup()
            .AddLine("[grey70]This project uses [yellow]packages.config[/] (legacy format).[/]")
            .AddLine("[grey70]Package operations require migration to [cyan1]PackageReference[/] first.[/]")
            .AddEmptyLine()
            .WithMargin(1, 0, 1, 0)
            .Build();
        controls.Add(notice);

        // Action toolbar: View Packages + Migrate
        var viewBtn = Controls.Button("[cyan1]View Packages[/] [grey78](Enter)[/]")
            .OnClick((s, e) => onViewPackages())
            .WithMargin(1, 0, 0, 0)
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .Build();

        var migrateBtn = Controls.Button("[yellow]Migrate[/] [grey78](Ctrl+M)[/]")
            .OnClick((s, e) => onMigrate?.Invoke())
            .Enabled(onMigrate != null)
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .Build();

        var toolbar = Controls.Toolbar()
            .AddButton(viewBtn)
            .AddButton(migrateBtn)
            .WithSpacing(2)
            .WithWrap()
            .WithBackgroundColor(ColorScheme.DetailsPanelBackground)
            .WithMargin(1, 0, 1, 0)
            .Build();
        controls.Add(toolbar);

        // Separator after toolbar
        var separatorAfter = Controls.Rule();
        separatorAfter.Margin = new Margin(1, 0, 1, 0);
        controls.Add(separatorAfter);

        // Read-only package list
        if (project.Packages.Count == 0)
        {
            var empty = Controls.Markup()
                .AddLine("[grey50]No packages in packages.config[/]")
                .WithMargin(1, 1, 1, 0)
                .Build();
            controls.Add(empty);
        }
        else
        {
            var listBuilder = ListControl.Create()
                .WithTitle($"Packages ({project.Packages.Count}) — read-only")
                .WithMargin(1, 1, 1, 0)
                .WithName("legacyPackageList")
                .WithAlignment(HorizontalAlignment.Stretch)
                .WithVerticalAlignment(VerticalAlignment.Fill)
                .Selectable(false);

            foreach (var pkg in project.Packages)
            {
                listBuilder.AddItem(new ListItem($"[grey70]{MarkupParser.Escape(pkg.Id)}[/] [grey50]{MarkupParser.Escape(pkg.Version)}[/]"));
            }

            controls.Add(listBuilder.Build());
        }

        return controls;
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
