using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;
using SharpConsoleUI.Drawing;
using SharpConsoleUI.Layout;
using Spectre.Console;
using LazyNuGet.Models;
using LazyNuGet.Services;
using LazyNuGet.UI.Utilities;
using HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment;
using VerticalAlignment = SharpConsoleUI.Layout.VerticalAlignment;

namespace LazyNuGet.UI.Modals;

/// <summary>
/// Modal for viewing the dependency tree of a project or a specific package.
/// Mode A (project): Shows direct + transitive dependencies from dotnet list.
/// Mode B (package): Shows the package's declared NuGet dependencies by target framework.
/// Uses a markup-based simulated tree inside a scrollable panel.
/// </summary>
public static class DependencyTreeModal
{
    private enum FilterMode
    {
        All,
        TopLevelOnly,
        TransitiveOnly
    }

    // Tree guide characters
    private const string Branch = "├── ";
    private const string Last   = "└── ";
    private const string Pipe   = "│   ";
    private const string Blank  = "    ";

    public static Task ShowAsync(
        ConsoleWindowSystem windowSystem,
        DotNetCliService cliService,
        NuGetClientService nugetClientService,
        ProjectInfo project,
        PackageReference? selectedPackage = null,
        Window? parentWindow = null)
    {
        var tcs = new TaskCompletionSource<bool>();
        var isPackageMode = selectedPackage != null;

        // State (Mode A only)
        var filterMode = FilterMode.All;
        List<ProjectDependencyTree> trees = new();

        // Controls
        ScrollablePanelControl? scrollPanel = null;
        MarkupControl? treeContent = null;
        MarkupControl? statusLabel = null;
        DropdownControl? filterDropdown = null;
        ToolbarControl? filterToolbar = null;

        // Title depends on mode
        var title = isPackageMode
            ? $"Dependencies - {selectedPackage!.Id}"
            : $"Dependencies - {project.Name}";

        // Build modal
        var modal = new WindowBuilder(windowSystem)
            .WithTitle(title)
            .Centered()
            .WithSize(100, 30)
            .AsModal()
            .Resizable(true)
            .Movable(true)
            .Minimizable(false)
            .WithColors(ColorScheme.WindowBackground, Color.Grey93)
            .WithBorderStyle(BorderStyle.DoubleLine)
            .WithBorderColor(ColorScheme.BorderColor)
            .Build();

        // Helper: update tree content markup
        void SetTreeContent(List<string> lines)
        {
            if (scrollPanel == null) return;
            scrollPanel.ClearContents();
            var builder = Controls.Markup();
            foreach (var line in lines) builder.AddLine(line);
            treeContent = builder.WithMargin(2, 0, 2, 0).Build();
            scrollPanel.AddControl(treeContent);
            scrollPanel.ScrollToTop();
        }

        // Helper to rebuild the tree for Mode A (project dependencies)
        void RefreshProjectTree()
        {
            var lines = new List<string>();

            if (!trees.Any())
            {
                lines.Add($"[{ColorScheme.MutedMarkup}]No dependencies found[/]");
                statusLabel?.SetContent(new List<string> { $"[{ColorScheme.MutedMarkup}]No dependencies[/]" });
                SetTreeContent(lines);
                return;
            }

            var totalTop = trees.Sum(t => t.TopLevelPackages.Count);
            var totalTransitive = trees.Sum(t => t.TransitivePackages.Count);
            var filterText = filterMode switch
            {
                FilterMode.TopLevelOnly => "Top-Level Only",
                FilterMode.TransitiveOnly => "Transitive Only",
                _ => "All"
            };
            statusLabel?.SetContent(new List<string> {
                $"[{ColorScheme.SecondaryMarkup}]Filter:[/] [{ColorScheme.PrimaryMarkup}]{filterText}[/]  [{ColorScheme.MutedMarkup}]{totalTop} direct, {totalTransitive} transitive[/]"
            });

            var isMultiTarget = trees.Count > 1;

            for (var ti = 0; ti < trees.Count; ti++)
            {
                var tree = trees[ti];
                var isLastFramework = ti == trees.Count - 1;

                // Collect sections for this framework
                var sections = new List<(string label, List<DependencyNode> deps, string color, string itemColor)>();

                if (filterMode != FilterMode.TransitiveOnly && tree.TopLevelPackages.Count > 0)
                    sections.Add(($"Direct Dependencies ({tree.TopLevelPackages.Count})", tree.TopLevelPackages, "green", "cyan1"));

                if (filterMode != FilterMode.TopLevelOnly && tree.TransitivePackages.Count > 0)
                    sections.Add(($"Transitive Dependencies ({tree.TransitivePackages.Count})", tree.TransitivePackages, "grey50", "grey50"));

                if (isMultiTarget)
                {
                    // Framework root node
                    var fwGuide = isLastFramework ? Last : Branch;
                    var fwPipe = isLastFramework ? Blank : Pipe;
                    lines.Add($"[grey23]{fwGuide}[/][cyan1 bold]{Markup.Escape(tree.TargetFramework)}[/]");

                    for (var si = 0; si < sections.Count; si++)
                    {
                        var (label, deps, color, itemColor) = sections[si];
                        var isLastSection = si == sections.Count - 1;
                        var secGuide = isLastSection ? Last : Branch;
                        var secPipe = isLastSection ? Blank : Pipe;

                        lines.Add($"[grey23]{fwPipe}{secGuide}[/][{color}]{Markup.Escape(label)}[/]");

                        for (var di = 0; di < deps.Count; di++)
                        {
                            var dep = deps[di];
                            var isLastDep = di == deps.Count - 1;
                            var depGuide = isLastDep ? Last : Branch;
                            var requested = dep.RequestedVersion != dep.ResolvedVersion
                                ? $" [grey35](requested {Markup.Escape(dep.RequestedVersion ?? "")})[/]"
                                : "";
                            lines.Add($"[grey23]{fwPipe}{secPipe}{depGuide}[/][{itemColor}]{Markup.Escape(dep.PackageId)}[/] [grey70]{dep.ResolvedVersion}[/]{requested}");
                        }
                    }

                    if (sections.Count == 0)
                    {
                        lines.Add($"[grey23]{fwPipe}{Last}[/][{ColorScheme.MutedMarkup}](no packages match filter)[/]");
                    }
                }
                else
                {
                    // Single-target: sections are root nodes
                    for (var si = 0; si < sections.Count; si++)
                    {
                        var (label, deps, color, itemColor) = sections[si];
                        var isLastSection = si == sections.Count - 1;
                        var secGuide = isLastSection ? Last : Branch;
                        var secPipe = isLastSection ? Blank : Pipe;

                        lines.Add($"[grey23]{secGuide}[/][{color}]{Markup.Escape(label)}[/]");

                        for (var di = 0; di < deps.Count; di++)
                        {
                            var dep = deps[di];
                            var isLastDep = di == deps.Count - 1;
                            var depGuide = isLastDep ? Last : Branch;
                            var requested = dep.RequestedVersion != dep.ResolvedVersion
                                ? $" [grey35](requested {Markup.Escape(dep.RequestedVersion ?? "")})[/]"
                                : "";
                            lines.Add($"[grey23]{secPipe}{depGuide}[/][{itemColor}]{Markup.Escape(dep.PackageId)}[/] [grey70]{dep.ResolvedVersion}[/]{requested}");
                        }
                    }

                    if (sections.Count == 0)
                    {
                        lines.Add($"[{ColorScheme.MutedMarkup}]No packages match the current filter[/]");
                    }
                }
            }

            SetTreeContent(lines);
        }

        // Helper to build the tree for Mode B (package dependencies)
        void BuildPackageTree(NuGetPackage nugetData)
        {
            var lines = new List<string>();

            if (nugetData.Dependencies.Count == 0)
            {
                lines.Add($"[{ColorScheme.MutedMarkup}]No dependencies declared[/]");
                statusLabel?.SetContent(new List<string> { $"[{ColorScheme.MutedMarkup}]No dependencies[/]" });
                SetTreeContent(lines);
                return;
            }

            var totalDeps = nugetData.Dependencies.Sum(g => g.Packages.Count);
            statusLabel?.SetContent(new List<string> {
                $"[{ColorScheme.SecondaryMarkup}]{Markup.Escape(selectedPackage!.Id)}[/] [{ColorScheme.MutedMarkup}]{nugetData.Version} · {totalDeps} dependencies across {nugetData.Dependencies.Count} framework(s)[/]"
            });

            // Root: package name + version
            lines.Add($"[cyan1 bold]{Markup.Escape(nugetData.Id)}[/] [grey70]{nugetData.Version}[/]");

            for (var gi = 0; gi < nugetData.Dependencies.Count; gi++)
            {
                var group = nugetData.Dependencies[gi];
                var isLastGroup = gi == nugetData.Dependencies.Count - 1;
                var groupGuide = isLastGroup ? Last : Branch;
                var groupPipe = isLastGroup ? Blank : Pipe;

                var fwLabel = string.IsNullOrEmpty(group.TargetFramework) ? "(any)" : group.TargetFramework;
                lines.Add($"[grey23]{groupGuide}[/][green]{Markup.Escape(fwLabel)}[/] [grey50]({group.Packages.Count})[/]");

                if (group.Packages.Count == 0)
                {
                    lines.Add($"[grey23]{groupPipe}{Last}[/][{ColorScheme.MutedMarkup}](no dependencies)[/]");
                }
                else
                {
                    for (var di = 0; di < group.Packages.Count; di++)
                    {
                        var dep = group.Packages[di];
                        var isLastDep = di == group.Packages.Count - 1;
                        var depGuide = isLastDep ? Last : Branch;
                        var versionInfo = string.IsNullOrEmpty(dep.VersionRange) ? "" : $" [grey50]({Markup.Escape(dep.VersionRange)})[/]";
                        lines.Add($"[grey23]{groupPipe}{depGuide}[/][grey70]{Markup.Escape(dep.Id)}[/]{versionInfo}");
                    }
                }
            }

            SetTreeContent(lines);
        }

        // Header
        var headerText = isPackageMode
            ? $"[{ColorScheme.PrimaryMarkup} bold]Package Dependencies[/]"
            : $"[{ColorScheme.PrimaryMarkup} bold]Dependency Tree[/]";
        var subtitleText = isPackageMode
            ? $"[{ColorScheme.SecondaryMarkup}]{Markup.Escape(selectedPackage!.Id)} {selectedPackage.Version} in {Markup.Escape(project.Name)}[/]"
            : $"[{ColorScheme.SecondaryMarkup}]{Markup.Escape(project.Name)} ({project.TargetFramework})[/]";

        var header = Controls.Markup()
            .AddLine(headerText)
            .AddLine(subtitleText)
            .WithMargin(2, 2, 2, 0)
            .Build();

        // Status label
        statusLabel = Controls.Markup()
            .AddLine($"[{ColorScheme.SecondaryMarkup}]Loading dependencies...[/]")
            .WithMargin(2, 1, 2, 0)
            .Build();

        // Separator
        var separator1 = Controls.RuleBuilder()
            .WithColor(ColorScheme.RuleColor)
            .Build();
        separator1.Margin = new Margin(2, 1, 2, 0);

        // Filter dropdown (Mode A only)
        filterDropdown = new DropdownControl(string.Empty, new[]
        {
            "All (F1)",
            "Top-Level Only (F2)",
            "Transitive Only (F3)"
        })
        {
            SelectedIndex = 0
        };

        filterDropdown.SelectedIndexChanged += (s, idx) =>
        {
            filterMode = idx switch
            {
                0 => FilterMode.All,
                1 => FilterMode.TopLevelOnly,
                2 => FilterMode.TransitiveOnly,
                _ => FilterMode.All
            };
            RefreshProjectTree();
        };

        filterToolbar = Controls.Toolbar()
            .Add(filterDropdown)
            .WithSpacing(2)
            .WithMargin(2, 0, 2, 1)
            .Build();

        // Hide filter toolbar in package mode
        if (isPackageMode)
        {
            filterToolbar.Visible = false;
        }

        // Scrollable panel for tree content
        scrollPanel = Controls.ScrollablePanel()
            .WithVerticalScroll(ScrollMode.Scroll)
            .WithScrollbar(true)
            .WithMouseWheel(true)
            .WithAutoScroll(false)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithColors(Color.Grey93, ColorScheme.WindowBackground)
            .Build();

        // Initial empty content
        treeContent = Controls.Markup()
            .AddLine($"[{ColorScheme.MutedMarkup}]Loading...[/]")
            .WithMargin(2, 0, 2, 0)
            .Build();
        scrollPanel.AddControl(treeContent);

        // Separator
        var separator2 = Controls.RuleBuilder()
            .WithColor(ColorScheme.RuleColor)
            .StickyBottom()
            .Build();
        separator2.Margin = new Margin(2, 0, 2, 0);

        // Close button
        var closeButton = Controls.Button("[grey93]Close (Esc)[/]")
            .WithMargin(1, 0, 0, 0)
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .Build();

        closeButton.Click += (s, e) => modal.Close();

        var buttonGrid = Controls.HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .Column(col => col.Add(closeButton))
            .Build();
        buttonGrid.Margin = new Margin(0, 0, 0, 0);

        // Help label
        var helpText = isPackageMode
            ? $"[{ColorScheme.MutedMarkup}]PgUp/PgDn:Scroll  |  Esc:Close[/]"
            : $"[{ColorScheme.MutedMarkup}]PgUp/PgDn:Scroll  |  F1:All  F2:Top-Level  F3:Transitive  |  Esc:Close[/]";

        var helpLabel = Controls.Markup()
            .AddLine(helpText)
            .WithMargin(2, 1, 2, 0)
            .StickyBottom()
            .Build();

        // Assemble modal
        modal.AddControl(header);
        modal.AddControl(statusLabel);
        modal.AddControl(separator1);
        modal.AddControl(filterToolbar);
        modal.AddControl(scrollPanel);
        modal.AddControl(helpLabel);
        modal.AddControl(separator2);
        modal.AddControl(buttonGrid);

        // Keyboard handling
        modal.KeyPressed += (s, e) =>
        {
            if (e.KeyInfo.Key == ConsoleKey.Escape)
            {
                modal.Close();
                e.Handled = true;
            }
            else if (e.KeyInfo.Key == ConsoleKey.PageUp)
            {
                scrollPanel?.ScrollVerticalBy(-10);
                e.Handled = true;
            }
            else if (e.KeyInfo.Key == ConsoleKey.PageDown)
            {
                scrollPanel?.ScrollVerticalBy(10);
                e.Handled = true;
            }
            else if (!isPackageMode && e.KeyInfo.Key == ConsoleKey.F1)
            {
                filterMode = FilterMode.All;
                if (filterDropdown != null) filterDropdown.SelectedIndex = 0;
                RefreshProjectTree();
                e.Handled = true;
            }
            else if (!isPackageMode && e.KeyInfo.Key == ConsoleKey.F2)
            {
                filterMode = FilterMode.TopLevelOnly;
                if (filterDropdown != null) filterDropdown.SelectedIndex = 1;
                RefreshProjectTree();
                e.Handled = true;
            }
            else if (!isPackageMode && e.KeyInfo.Key == ConsoleKey.F3)
            {
                filterMode = FilterMode.TransitiveOnly;
                if (filterDropdown != null) filterDropdown.SelectedIndex = 2;
                RefreshProjectTree();
                e.Handled = true;
            }
        };

        modal.OnClosed += (s, e) => tcs.TrySetResult(true);

        // Show modal
        windowSystem.AddWindow(modal);
        windowSystem.SetActiveWindow(modal);

        // Load data asynchronously
        _ = Task.Run(async () =>
        {
            try
            {
                if (isPackageMode)
                {
                    // Mode B: Fetch package's declared dependencies from NuGet
                    var nugetData = await nugetClientService.GetPackageDetailsAsync(selectedPackage!.Id);
                    if (nugetData != null)
                    {
                        BuildPackageTree(nugetData);
                    }
                    else
                    {
                        SetTreeContent(new List<string> {
                            $"[{ColorScheme.ErrorMarkup}]Could not load package metadata[/]"
                        });
                        statusLabel?.SetContent(new List<string> {
                            $"[{ColorScheme.ErrorMarkup}]Failed to fetch package details[/]"
                        });
                    }
                }
                else
                {
                    // Mode A: Project dependencies via dotnet CLI
                    trees = await cliService.ListTransitiveDependenciesAsync(project.FilePath);
                    RefreshProjectTree();
                }
            }
            catch (Exception ex)
            {
                statusLabel?.SetContent(new List<string> {
                    $"[{ColorScheme.ErrorMarkup}]Error loading dependencies: {Markup.Escape(ex.Message)}[/]"
                });
            }
        });

        return tcs.Task;
    }
}
