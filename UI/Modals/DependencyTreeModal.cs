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
using AsyncHelper = LazyNuGet.Services.AsyncHelper;
using HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment;
using VerticalAlignment = SharpConsoleUI.Layout.VerticalAlignment;

namespace LazyNuGet.UI.Modals;

/// <summary>
/// Modal for viewing the dependency tree of a project or a specific package.
/// Mode A (project): Shows direct + transitive dependencies from dotnet list.
/// Mode B (package): Shows the package's declared NuGet dependencies by target framework.
/// Uses a markup-based simulated tree inside a scrollable panel.
/// </summary>
public class DependencyTreeModal : ModalBase<bool>
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

    // Dependencies
    private readonly DotNetCliService _cliService;
    private readonly NuGetClientService _nugetClientService;
    private readonly ProjectInfo _project;
    private readonly PackageReference? _selectedPackage;
    private readonly bool _isPackageMode;

    // State (Mode A only)
    private FilterMode _filterMode = FilterMode.All;
    private List<ProjectDependencyTree> _trees = new();

    // Controls
    private ScrollablePanelControl? _scrollPanel;
    private MarkupControl? _treeContent;
    private MarkupControl? _statusLabel;
    private DropdownControl? _filterDropdown;
    private ToolbarControl? _filterToolbar;

    // Event handler references for cleanup
    private EventHandler<int>? _dropdownHandler;

    private DependencyTreeModal(
        DotNetCliService cliService,
        NuGetClientService nugetClientService,
        ProjectInfo project,
        PackageReference? selectedPackage)
    {
        _cliService = cliService;
        _nugetClientService = nugetClientService;
        _project = project;
        _selectedPackage = selectedPackage;
        _isPackageMode = selectedPackage != null;
    }

    public static Task<bool> ShowAsync(
        ConsoleWindowSystem windowSystem,
        DotNetCliService cliService,
        NuGetClientService nugetClientService,
        ProjectInfo project,
        PackageReference? selectedPackage = null,
        Window? parentWindow = null)
    {
        var instance = new DependencyTreeModal(cliService, nugetClientService, project, selectedPackage);
        return instance.ShowAsync(windowSystem, parentWindow);
    }

    protected override string GetTitle() => _isPackageMode
        ? $"Dependencies - {_selectedPackage!.Id}"
        : $"Dependencies - {_project.Name}";

    protected override (int width, int height) GetSize() => (100, 30);

    protected override BorderStyle GetBorderStyle() => BorderStyle.DoubleLine;

    protected override Color GetBorderColor() => ColorScheme.BorderColor;

    protected override bool GetDefaultResult() => true;

    protected override void BuildContent()
    {
        // Header
        var headerText = _isPackageMode
            ? $"[{ColorScheme.PrimaryMarkup} bold]Package Dependencies[/]"
            : $"[{ColorScheme.PrimaryMarkup} bold]Dependency Tree[/]";
        var subtitleText = _isPackageMode
            ? $"[{ColorScheme.SecondaryMarkup}]{Markup.Escape(_selectedPackage!.Id)} {Markup.Escape(_selectedPackage.Version)} in {Markup.Escape(_project.Name)}[/]"
            : $"[{ColorScheme.SecondaryMarkup}]{Markup.Escape(_project.Name)} ({_project.TargetFramework})[/]";

        var header = Controls.Markup()
            .AddLine(headerText)
            .AddLine(subtitleText)
            .WithMargin(2, 2, 2, 0)
            .Build();

        // Status label
        _statusLabel = Controls.Markup()
            .AddLine($"[{ColorScheme.SecondaryMarkup}]Loading dependencies...[/]")
            .WithMargin(2, 1, 2, 0)
            .Build();

        // Separator
        var separator1 = Controls.RuleBuilder()
            .WithColor(ColorScheme.RuleColor)
            .Build();
        separator1.Margin = new Margin(2, 1, 2, 0);

        // Filter dropdown (Mode A only)
        _filterDropdown = new DropdownControl(string.Empty, new[]
        {
            "All (F1)",
            "Top-Level Only (F2)",
            "Transitive Only (F3)"
        })
        {
            SelectedIndex = 0
        };

        _dropdownHandler = (s, idx) =>
        {
            _filterMode = idx switch
            {
                0 => FilterMode.All,
                1 => FilterMode.TopLevelOnly,
                2 => FilterMode.TransitiveOnly,
                _ => FilterMode.All
            };
            RefreshProjectTree();
        };
        _filterDropdown.SelectedIndexChanged += _dropdownHandler;

        _filterToolbar = Controls.Toolbar()
            .Add(_filterDropdown)
            .WithSpacing(2)
            .WithMargin(2, 0, 2, 1)
            .Build();

        // Hide filter toolbar in package mode
        if (_isPackageMode)
        {
            _filterToolbar.Visible = false;
        }

        // Scrollable panel for tree content
        _scrollPanel = Controls.ScrollablePanel()
            .WithVerticalScroll(ScrollMode.Scroll)
            .WithScrollbar(true)
            .WithMouseWheel(true)
            .WithAutoScroll(false)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithColors(Color.Grey93, ColorScheme.WindowBackground)
            .Build();

        // Initial empty content
        _treeContent = Controls.Markup()
            .AddLine($"[{ColorScheme.MutedMarkup}]Loading...[/]")
            .WithMargin(2, 0, 2, 0)
            .Build();
        _scrollPanel.AddControl(_treeContent);

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

        closeButton.Click += (s, e) => CloseWithResult(true);

        var buttonGrid = Controls.HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .Column(col => col.Add(closeButton))
            .Build();
        buttonGrid.Margin = new Margin(0, 0, 0, 0);

        // Help label
        var helpText = _isPackageMode
            ? $"[{ColorScheme.MutedMarkup}]PgUp/PgDn:Scroll  |  Esc:Close[/]"
            : $"[{ColorScheme.MutedMarkup}]PgUp/PgDn:Scroll  |  F1:All  F2:Top-Level  F3:Transitive  |  Esc:Close[/]";

        var helpLabel = Controls.Markup()
            .AddLine(helpText)
            .WithMargin(2, 1, 2, 0)
            .StickyBottom()
            .Build();

        // Assemble modal
        Modal.AddControl(header);
        Modal.AddControl(_statusLabel);
        Modal.AddControl(separator1);
        Modal.AddControl(_filterToolbar);
        Modal.AddControl(_scrollPanel);
        Modal.AddControl(helpLabel);
        Modal.AddControl(separator2);
        Modal.AddControl(buttonGrid);

        // Load data asynchronously
        AsyncHelper.FireAndForget(async () =>
        {
            if (_isPackageMode)
            {
                // Mode B: Fetch package's declared dependencies from NuGet
                var nugetData = await _nugetClientService.GetPackageDetailsAsync(_selectedPackage!.Id);
                if (nugetData != null)
                {
                    BuildPackageTree(nugetData);
                }
                else
                {
                    SetTreeContent(new List<string> {
                        $"[{ColorScheme.ErrorMarkup}]Could not load package metadata[/]"
                    });
                    _statusLabel?.SetContent(new List<string> {
                        $"[{ColorScheme.ErrorMarkup}]Failed to fetch package details[/]"
                    });
                }
            }
            else
            {
                // Mode A: Project dependencies via dotnet CLI
                _trees = await _cliService.ListTransitiveDependenciesAsync(_project.FilePath);
                RefreshProjectTree();
            }
        },
        ex =>
        {
            _statusLabel?.SetContent(new List<string> {
                $"[{ColorScheme.ErrorMarkup}]Error loading dependencies: {Markup.Escape(ex.Message)}[/]"
            });
        });
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.PageUp)
        {
            _scrollPanel?.ScrollVerticalBy(-10);
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.PageDown)
        {
            _scrollPanel?.ScrollVerticalBy(10);
            e.Handled = true;
        }
        else if (!_isPackageMode && e.KeyInfo.Key == ConsoleKey.F1)
        {
            _filterMode = FilterMode.All;
            if (_filterDropdown != null) _filterDropdown.SelectedIndex = 0;
            RefreshProjectTree();
            e.Handled = true;
        }
        else if (!_isPackageMode && e.KeyInfo.Key == ConsoleKey.F2)
        {
            _filterMode = FilterMode.TopLevelOnly;
            if (_filterDropdown != null) _filterDropdown.SelectedIndex = 1;
            RefreshProjectTree();
            e.Handled = true;
        }
        else if (!_isPackageMode && e.KeyInfo.Key == ConsoleKey.F3)
        {
            _filterMode = FilterMode.TransitiveOnly;
            if (_filterDropdown != null) _filterDropdown.SelectedIndex = 2;
            RefreshProjectTree();
            e.Handled = true;
        }
        else
        {
            // Let base handle Escape
            base.OnKeyPressed(sender, e);
        }
    }

    protected override void OnCleanup()
    {
        // Unsubscribe event handlers to prevent memory leaks
        if (_filterDropdown != null && _dropdownHandler != null)
            _filterDropdown.SelectedIndexChanged -= _dropdownHandler;
    }

    // Helper: update tree content markup
    private void SetTreeContent(List<string> lines)
    {
        if (_scrollPanel == null) return;
        _scrollPanel.ClearContents();
        var builder = Controls.Markup();
        foreach (var line in lines) builder.AddLine(line);
        _treeContent = builder.WithMargin(2, 0, 2, 0).Build();
        _scrollPanel.AddControl(_treeContent);
        _scrollPanel.ScrollToTop();
    }

    // Helper to rebuild the tree for Mode A (project dependencies)
    private void RefreshProjectTree()
    {
        var lines = new List<string>();

        if (!_trees.Any())
        {
            lines.Add($"[{ColorScheme.MutedMarkup}]No dependencies found[/]");
            _statusLabel?.SetContent(new List<string> { $"[{ColorScheme.MutedMarkup}]No dependencies[/]" });
            SetTreeContent(lines);
            return;
        }

        var totalTop = _trees.Sum(t => t.TopLevelPackages.Count);
        var totalTransitive = _trees.Sum(t => t.TransitivePackages.Count);
        var filterText = _filterMode switch
        {
            FilterMode.TopLevelOnly => "Top-Level Only",
            FilterMode.TransitiveOnly => "Transitive Only",
            _ => "All"
        };
        _statusLabel?.SetContent(new List<string> {
            $"[{ColorScheme.SecondaryMarkup}]Filter:[/] [{ColorScheme.PrimaryMarkup}]{filterText}[/]  [{ColorScheme.MutedMarkup}]{totalTop} direct, {totalTransitive} transitive[/]"
        });

        var isMultiTarget = _trees.Count > 1;

        for (var ti = 0; ti < _trees.Count; ti++)
        {
            var tree = _trees[ti];
            var isLastFramework = ti == _trees.Count - 1;

            // Collect sections for this framework
            var sections = new List<(string label, List<DependencyNode> deps, string color, string itemColor)>();

            if (_filterMode != FilterMode.TransitiveOnly && tree.TopLevelPackages.Count > 0)
                sections.Add(($"Direct Dependencies ({tree.TopLevelPackages.Count})", tree.TopLevelPackages, "green", "cyan1"));

            if (_filterMode != FilterMode.TopLevelOnly && tree.TransitivePackages.Count > 0)
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
                        lines.Add($"[grey23]{fwPipe}{secPipe}{depGuide}[/][{itemColor}]{Markup.Escape(dep.PackageId)}[/] [grey70]{Markup.Escape(dep.ResolvedVersion)}[/]{requested}");
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
    private void BuildPackageTree(NuGetPackage nugetData)
    {
        var lines = new List<string>();

        if (nugetData.Dependencies.Count == 0)
        {
            lines.Add($"[{ColorScheme.MutedMarkup}]No dependencies declared[/]");
            _statusLabel?.SetContent(new List<string> { $"[{ColorScheme.MutedMarkup}]No dependencies[/]" });
            SetTreeContent(lines);
            return;
        }

        var totalDeps = nugetData.Dependencies.Sum(g => g.Packages.Count);
        _statusLabel?.SetContent(new List<string> {
            $"[{ColorScheme.SecondaryMarkup}]{Markup.Escape(_selectedPackage!.Id)}[/] [{ColorScheme.MutedMarkup}]{Markup.Escape(nugetData.Version)} · {totalDeps} dependencies across {nugetData.Dependencies.Count} framework(s)[/]"
        });

        // Root: package name + version
        lines.Add($"[cyan1 bold]{Markup.Escape(nugetData.Id)}[/] [grey70]{Markup.Escape(nugetData.Version)}[/]");

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
                    var versionInfo = string.IsNullOrEmpty(dep.VersionRange) ? "" : $" [grey50]({dep.VersionRange})[/]";
                    lines.Add($"[grey23]{groupPipe}{depGuide}[/][grey70]{Markup.Escape(dep.Id)}[/]{versionInfo}");
                }
            }
        }

        SetTreeContent(lines);
    }
}
