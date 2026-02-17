using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drawing;
using SharpConsoleUI.Layout;
using Spectre.Console;
using LazyNuGet.Models;
using LazyNuGet.UI.Utilities;
using HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment;
using VerticalAlignment = SharpConsoleUI.Layout.VerticalAlignment;

namespace LazyNuGet.UI.Modals;

/// <summary>
/// Install planning modal that allows installing a package to multiple projects at once.
/// Shows grouped project list (Available, Already Installed, Incompatible) with multi-selection.
/// </summary>
public class InstallPlanningModal : ModalBase<List<ProjectInfo>?>
{
    private enum ProjectCategory { Available, AlreadyInstalled, Incompatible }

    private class ProjectEntry
    {
        public ProjectInfo Project { get; set; } = null!;
        public ProjectCategory Category { get; set; }
        public bool IsSelected { get; set; }
        public string? InstalledVersion { get; set; }
        public string? CompatibilityWarning { get; set; }
    }

    private readonly NuGetPackage _package;
    private readonly List<ProjectInfo> _projects;
    private readonly ProjectInfo? _currentProject;
    private readonly List<ProjectEntry> _entries = new();

    private ListControl? _projectList;
    private MarkupControl? _summaryLabel;
    private ButtonControl? _installButton;

    // Event handlers for cleanup
    private EventHandler<int>? _selectionChangedHandler;
    private EventHandler<ButtonControl>? _installClickHandler;
    private EventHandler<ButtonControl>? _cancelClickHandler;

    private InstallPlanningModal(NuGetPackage package, List<ProjectInfo> projects, ProjectInfo? currentProject)
    {
        _package = package;
        _projects = projects;
        _currentProject = currentProject;
        BuildEntries();
    }

    /// <summary>
    /// Show the install planning modal and return the list of selected projects (or null if cancelled).
    /// </summary>
    /// <param name="currentProject">The currently selected project (if in single project view), or null if in all projects view</param>
    public static Task<List<ProjectInfo>?> ShowAsync(
        ConsoleWindowSystem windowSystem,
        NuGetPackage package,
        List<ProjectInfo> projects,
        ProjectInfo? currentProject = null,
        Window? parentWindow = null)
    {
        var modal = new InstallPlanningModal(package, projects, currentProject);
        return modal.ShowAsync(windowSystem, parentWindow);
    }

    protected override string GetTitle() => "Install Package";
    protected override (int width, int height) GetSize()
    {
        // Dynamic height based on project count (each project takes ~2 lines)
        // Add base height for header, buttons, etc. (20 lines)
        // Add project lines (2 per project for name + framework)
        var dynamicHeight = 20 + (_projects.Count * 2);

        // Cap at a reasonable maximum (80% of typical terminal height ~24 lines)
        // This prevents the modal from being too large on small terminals
        var maxHeight = 50;

        return (80, Math.Min(maxHeight, dynamicHeight));
    }
    protected override BorderStyle GetBorderStyle() => BorderStyle.DoubleLine;
    protected override Color GetBorderColor() => ColorScheme.BorderColor;
    protected override List<ProjectInfo>? GetDefaultResult() => null;

    private void BuildEntries()
    {
        _entries.Clear();

        foreach (var project in _projects)
        {
            var entry = new ProjectEntry { Project = project };

            // Check if already installed
            var existing = project.Packages.FirstOrDefault(p =>
                string.Equals(p.Id, _package.Id, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                entry.Category = ProjectCategory.AlreadyInstalled;
                entry.InstalledVersion = existing.Version;
                entry.IsSelected = false;
            }
            else
            {
                // Check compatibility
                var (hasWarning, warningMessage) = CheckCompatibility(_package, project.TargetFramework);
                if (hasWarning && warningMessage != null && warningMessage.Contains("Installation may fail"))
                {
                    entry.Category = ProjectCategory.Incompatible;
                    entry.CompatibilityWarning = warningMessage;
                    entry.IsSelected = false;
                }
                else
                {
                    entry.Category = ProjectCategory.Available;

                    // Pre-selection logic based on context:
                    // - If currentProject is set (single project view): select only that project
                    // - If currentProject is null (all projects view): select all available projects
                    if (_currentProject != null)
                    {
                        entry.IsSelected = project.FilePath == _currentProject.FilePath;
                    }
                    else
                    {
                        entry.IsSelected = true; // Select all available
                    }

                    if (hasWarning)
                    {
                        entry.CompatibilityWarning = warningMessage;
                    }
                }
            }

            _entries.Add(entry);
        }
    }

    protected override void BuildContent()
    {
        // Package header
        var headerBuilder = Controls.Markup()
            .AddLine($"[{ColorScheme.PrimaryMarkup}]{Markup.Escape(_package.Id)}[/] [{ColorScheme.SecondaryMarkup}]v{Markup.Escape(_package.Version)}[/]");

        if (!string.IsNullOrEmpty(_package.Description))
        {
            var desc = _package.Description.Length > 80
                ? _package.Description.Substring(0, 77) + "..."
                : _package.Description;
            headerBuilder.AddLine($"[{ColorScheme.MutedMarkup}]{Markup.Escape(desc)}[/]");
        }

        if (_package.TotalDownloads > 0 || _package.Published.HasValue)
        {
            var metaParts = new List<string>();
            if (_package.TotalDownloads > 0)
                metaParts.Add($"Downloads: {FormatDownloads(_package.TotalDownloads)}");
            if (_package.Published.HasValue)
                metaParts.Add($"Published: {_package.Published.Value:yyyy-MM-dd}");
            headerBuilder.AddLine($"[{ColorScheme.MutedMarkup}]{string.Join(" | ", metaParts)}[/]");
        }

        // Vulnerability warning
        if (_package.VulnerabilityCount > 0)
        {
            var warningColor = _package.VulnerabilityCount >= 3 ? "red" : "orange1";
            headerBuilder.AddLine($"[{warningColor}]WARNING: {_package.VulnerabilityCount} known {(_package.VulnerabilityCount == 1 ? "vulnerability" : "vulnerabilities")}[/]");
        }

        if (_package.IsDeprecated)
        {
            headerBuilder.AddLine($"[orange1]DEPRECATED{(string.IsNullOrEmpty(_package.DeprecationMessage) ? "" : $": {Markup.Escape(_package.DeprecationMessage)}")}[/]");
        }

        Modal.AddControl(headerBuilder.WithMargin(2, 1, 2, 0).Build());

        // Separator
        var sep1 = Controls.RuleBuilder().WithColor(ColorScheme.RuleColor).Build();
        sep1.Margin = new Margin(2, 1, 2, 0);
        Modal.AddControl(sep1);

        // Project list with checkboxes
        _projectList = Controls.List()
            .SimpleMode()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithColors(Color.Grey93, ColorScheme.SidebarBackground)
            .WithFocusedColors(Color.Grey93, ColorScheme.SidebarBackground)
            .WithHighlightColors(Color.White, Color.Grey35)
            .WithMargin(2, 1, 2, 0)
            .Build();

        PopulateProjectList();

        _selectionChangedHandler = (s, idx) => UpdateSummary();
        _projectList.SelectedIndexChanged += _selectionChangedHandler;

        // Add mouse click handler to toggle selection
        _projectList.MouseClick += (s, e) => ToggleCurrentSelection();

        Modal.AddControl(_projectList);

        // Summary label
        _summaryLabel = Controls.Markup()
            .AddLine(GetSummaryText())
            .WithMargin(2, 1, 2, 0)
            .Build();
        Modal.AddControl(_summaryLabel);

        // Separator before buttons
        var sep2 = Controls.RuleBuilder().WithColor(ColorScheme.RuleColor).StickyBottom().Build();
        sep2.Margin = new Margin(2, 0, 2, 0);
        Modal.AddControl(sep2);

        // Action buttons
        _installButton = Controls.Button($"[white]Install ({SelectedCount()})[/]")
            .WithAlignment(HorizontalAlignment.Center)
            .WithBackgroundColor(Color.DarkGreen)
            .WithForegroundColor(Color.White)
            .WithFocusedBackgroundColor(Color.Green)
            .WithFocusedForegroundColor(Color.White)
            .Build();
        _installButton.IsEnabled = SelectedCount() > 0;
        _installClickHandler = (s, e) => HandleInstall();
        _installButton.Click += _installClickHandler;

        var cancelButton = Controls.Button("[grey93]Cancel (Esc)[/]")
            .WithAlignment(HorizontalAlignment.Center)
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .Build();
        _cancelClickHandler = (s, e) => CloseWithResult(null);
        cancelButton.Click += _cancelClickHandler;

        var buttonGrid = Controls.HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .Column(col => col.Add(_installButton))
            .Column(col => col.Width(2))
            .Column(col => col.Add(cancelButton))
            .Build();
        buttonGrid.Margin = new Margin(0, 0, 0, 0);
        Modal.AddControl(buttonGrid);

        // Hint bar
        var sep3 = Controls.RuleBuilder().WithColor(ColorScheme.RuleColor).StickyBottom().Build();
        sep3.Margin = new Margin(2, 0, 2, 0);
        Modal.AddControl(sep3);

        Modal.AddControl(Controls.Markup()
            .AddLine($"[{ColorScheme.MutedMarkup}]Space:Toggle  A:Select All  N:Select None  Enter:Install  Esc:Cancel[/]")
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .Build());
    }

    protected override void SetInitialFocus()
    {
        _projectList?.SetFocus(true, FocusReason.Programmatic);
    }

    private void PopulateProjectList()
    {
        _projectList?.ClearItems();

        var available = _entries.Where(e => e.Category == ProjectCategory.Available).ToList();
        var installed = _entries.Where(e => e.Category == ProjectCategory.AlreadyInstalled).ToList();
        var incompatible = _entries.Where(e => e.Category == ProjectCategory.Incompatible).ToList();

        // Available to Install section
        if (available.Count > 0)
        {
            var headerItem = new ListItem($"[{ColorScheme.SecondaryMarkup}]Available to Install ({available.Count}):[/]");
            headerItem.Tag = "header";
            _projectList?.AddItem(headerItem);

            foreach (var entry in available)
            {
                var checkbox = entry.IsSelected ? "[green][ x ][/]" : "[grey50][   ][/]";
                var warningText = !string.IsNullOrEmpty(entry.CompatibilityWarning)
                    ? $" [yellow]![/]" : "";
                var displayText = $"  {checkbox} [{ColorScheme.PrimaryMarkup}]{Markup.Escape(entry.Project.Name)}[/]{warningText}\n" +
                                  $"        [{ColorScheme.MutedMarkup}]{Markup.Escape(entry.Project.TargetFramework)}[/]";
                var item = new ListItem(displayText);
                item.Tag = entry;
                _projectList?.AddItem(item);
            }
        }

        // Already Installed section
        if (installed.Count > 0)
        {
            var headerItem = new ListItem($"[{ColorScheme.SecondaryMarkup}]Already Installed ({installed.Count}):[/]");
            headerItem.Tag = "header";
            _projectList?.AddItem(headerItem);

            foreach (var entry in installed)
            {
                var displayText = $"  [{ColorScheme.MutedMarkup}]---[/] [{ColorScheme.MutedMarkup}]{Markup.Escape(entry.Project.Name)}[/]\n" +
                                  $"        [{ColorScheme.MutedMarkup}]v{Markup.Escape(entry.InstalledVersion ?? "?")} installed | {Markup.Escape(entry.Project.TargetFramework)}[/]";
                var item = new ListItem(displayText);
                item.Tag = entry;
                _projectList?.AddItem(item);
            }
        }

        // Incompatible section
        if (incompatible.Count > 0)
        {
            var headerItem = new ListItem($"[{ColorScheme.SecondaryMarkup}]Incompatible ({incompatible.Count}):[/]");
            headerItem.Tag = "header";
            _projectList?.AddItem(headerItem);

            foreach (var entry in incompatible)
            {
                var displayText = $"  [{ColorScheme.MutedMarkup}]---[/] [{ColorScheme.MutedMarkup}]{Markup.Escape(entry.Project.Name)}[/]\n" +
                                  $"        [red]{Markup.Escape(entry.Project.TargetFramework)} - incompatible[/]";
                var item = new ListItem(displayText);
                item.Tag = entry;
                _projectList?.AddItem(item);
            }
        }

        if (_entries.Count == 0)
        {
            var emptyItem = new ListItem($"[{ColorScheme.MutedMarkup}]No projects found[/]");
            emptyItem.Tag = "header";
            _projectList?.AddItem(emptyItem);
        }
    }

    private void ToggleCurrentSelection()
    {
        if (_projectList == null) return;

        var selectedItem = _projectList.SelectedItem;
        if (selectedItem?.Tag is ProjectEntry entry && entry.Category == ProjectCategory.Available)
        {
            entry.IsSelected = !entry.IsSelected;
            RefreshList();
        }
    }

    private void SelectAll()
    {
        foreach (var entry in _entries.Where(e => e.Category == ProjectCategory.Available))
        {
            entry.IsSelected = true;
        }
        RefreshList();
    }

    private void SelectNone()
    {
        foreach (var entry in _entries.Where(e => e.Category == ProjectCategory.Available))
        {
            entry.IsSelected = false;
        }
        RefreshList();
    }

    private void RefreshList()
    {
        // Remember current selection index
        var currentIndex = _projectList?.SelectedIndex ?? 0;

        PopulateProjectList();
        UpdateSummary();
        UpdateInstallButton();

        // Restore selection
        if (_projectList != null && currentIndex >= 0 && currentIndex < _projectList.Items.Count)
        {
            _projectList.SelectedIndex = currentIndex;
        }
    }

    private int SelectedCount() => _entries.Count(e => e.IsSelected);

    private string GetSummaryText()
    {
        var selected = SelectedCount();
        var total = _entries.Count;
        var available = _entries.Count(e => e.Category == ProjectCategory.Available);
        var installed = _entries.Count(e => e.Category == ProjectCategory.AlreadyInstalled);

        if (selected == 0)
        {
            return $"[{ColorScheme.MutedMarkup}]No projects selected for installation[/]";
        }

        var summary = $"[{ColorScheme.InfoMarkup}]Install to {selected} of {available} available project{(selected != 1 ? "s" : "")}[/]";

        if (installed > 0)
        {
            summary += $" [{ColorScheme.MutedMarkup}]({installed} already installed)[/]";
        }

        return summary;
    }

    private void UpdateSummary()
    {
        _summaryLabel?.SetContent(new List<string> { GetSummaryText() });
    }

    private void UpdateInstallButton()
    {
        if (_installButton == null) return;

        var count = SelectedCount();
        _installButton.IsEnabled = count > 0;
        _installButton.Text = count > 0
            ? $"[white]Install ({count})[/]"
            : "[grey70]Install (0)[/]";
    }

    private void HandleInstall()
    {
        var selected = _entries
            .Where(e => e.IsSelected)
            .Select(e => e.Project)
            .ToList();

        if (selected.Count > 0)
        {
            CloseWithResult(selected);
        }
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Spacebar)
        {
            ToggleCurrentSelection();
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.A && !e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            SelectAll();
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.N && !e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            SelectNone();
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.Enter)
        {
            HandleInstall();
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.Escape)
        {
            CloseWithResult(null);
            e.Handled = true;
        }
        else
        {
            base.OnKeyPressed(sender, e);
        }
    }

    protected override void OnCleanup()
    {
        if (_projectList != null && _selectionChangedHandler != null)
            _projectList.SelectedIndexChanged -= _selectionChangedHandler;
        if (_installButton != null && _installClickHandler != null)
            _installButton.Click -= _installClickHandler;
    }

    /// <summary>
    /// Check if the package target frameworks are compatible with the project target framework.
    /// Reused from ConfirmInstallModal logic.
    /// </summary>
    private static (bool hasWarning, string? warningMessage) CheckCompatibility(NuGetPackage package, string? projectTargetFramework)
    {
        if (string.IsNullOrEmpty(projectTargetFramework) ||
            package.TargetFrameworks == null ||
            package.TargetFrameworks.Count == 0)
        {
            return (false, null);
        }

        var projectVersion = ParseFrameworkVersion(projectTargetFramework);
        if (projectVersion == null)
        {
            return (false, null);
        }

        var highestPackageVersion = package.TargetFrameworks
            .Select(ParseFrameworkVersion)
            .Where(v => v != null)
            .OrderByDescending(v => v)
            .FirstOrDefault();

        if (highestPackageVersion == null)
        {
            return (false, null);
        }

        if (highestPackageVersion > projectVersion)
        {
            var problematicFramework = package.TargetFrameworks
                .FirstOrDefault(tf => ParseFrameworkVersion(tf) == highestPackageVersion);

            var message = $"Package targets {problematicFramework ?? highestPackageVersion.ToString()} but project uses {projectTargetFramework}. Installation may fail or cause compatibility issues.";
            return (true, message);
        }

        return (false, null);
    }

    private static double? ParseFrameworkVersion(string framework)
    {
        if (string.IsNullOrEmpty(framework))
            return null;

        framework = framework.ToLowerInvariant().Trim();

        if (framework.Length < 4)
            return null;

        if (framework.StartsWith("net") && !framework.StartsWith("netstandard") && !framework.StartsWith("netframework"))
        {
            var versionPart = framework.Substring(3);
            if (double.TryParse(versionPart, out var version))
                return version;
        }

        if (framework.StartsWith("netstandard"))
        {
            var versionPart = framework.Substring(11);
            if (double.TryParse(versionPart, out var version))
                return version + 3.0;
        }

        if (framework.StartsWith("net") && framework.Length > 3 && char.IsDigit(framework[3]))
        {
            var versionPart = framework.Substring(3);
            if (int.TryParse(versionPart, out var intVersion))
            {
                if (intVersion >= 100)
                    return intVersion / 100.0;
            }
        }

        return null;
    }

    private static string FormatDownloads(long downloads)
    {
        if (downloads >= 1_000_000_000)
            return $"{downloads / 1_000_000_000.0:F1}B";
        if (downloads >= 1_000_000)
            return $"{downloads / 1_000_000.0:F1}M";
        if (downloads >= 1_000)
            return $"{downloads / 1_000.0:F1}K";
        return downloads.ToString();
    }
}
