using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Core;
using SharpConsoleUI.Drawing;
using Spectre.Console;
using LazyNuGet.Models;
using LazyNuGet.UI.Utilities;
using HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment;
using VerticalAlignment = SharpConsoleUI.Layout.VerticalAlignment;

namespace LazyNuGet.UI.Modals;

/// <summary>
/// Modal for selecting a specific package version to install
/// </summary>
public static class VersionSelectorModal
{
    private enum FilterVersionType
    {
        All,
        StableOnly,
        PreReleaseOnly
    }
    public static async Task<string?> ShowAsync(
        ConsoleWindowSystem windowSystem,
        PackageReference package,
        List<string> availableVersions,
        Window? parentWindow = null)
    {
        var tcs = new TaskCompletionSource<string?>();
        string? selectedVersion = null;

        // Filter state
        var filterType = FilterVersionType.All;

        // Controls
        ListControl? versionList = null;
        MarkupControl? statusLabel = null;
        MarkupControl? filterLabel = null;
        ButtonControl? selectButton = null;
        ButtonControl? clearFilterButton = null;
        DropdownControl? versionTypeDropdown = null;

        var currentVersions = new List<string>();
        var allVersions = availableVersions;

        var modal = new WindowBuilder(windowSystem)
            .WithTitle($"Select Version - {package.Id}")
            .Centered()
            .WithSize(100, 30)
            .AsModal()
            .WithBorderStyle(BorderStyle.DoubleLine)
            .WithBorderColor(ColorScheme.BorderColor)
            .Resizable(true)
            .Movable(true)
            .Minimizable(false)
            .WithColors(ColorScheme.WindowBackground, Color.Grey93)
            .Build();

        // ── Helper: Refresh versions list based on filters ────────────
        void RefreshVersionsList()
        {
            // Apply version type filtering
            var filtered = filterType switch
            {
                FilterVersionType.StableOnly => allVersions.Where(v => !v.Contains('-')),
                FilterVersionType.PreReleaseOnly => allVersions.Where(v => v.Contains('-')),
                _ => allVersions
            };

            currentVersions = filtered.ToList();

            // Update status label
            UpdateStatusLabel();

            // Populate list
            versionList?.ClearItems();

            if (!currentVersions.Any())
            {
                var emptyItem = new ListItem($"[{ColorScheme.MutedMarkup}]No versions found matching current filters[/]");
                emptyItem.Tag = null;
                versionList?.AddItem(emptyItem);
                if (selectButton != null) selectButton.IsEnabled = false;
                return;
            }

            foreach (var version in currentVersions)
            {
                var displayText = BuildEnhancedVersionDisplay(version, package.Version, currentVersions);
                var listItem = new ListItem(displayText);
                listItem.Tag = version;
                versionList?.AddItem(listItem);
            }

            // Set initial selection to current version if found
            var currentIndex = currentVersions.FindIndex(v =>
                string.Equals(v, package.Version, StringComparison.OrdinalIgnoreCase));
            if (currentIndex >= 0 && versionList != null)
                versionList.SelectedIndex = currentIndex;

            // Enable/disable select button based on selection
            var selectedVer = versionList?.SelectedItem?.Tag as string;
            if (selectButton != null) selectButton.IsEnabled = selectedVer != null;
        }

        void UpdateStatusLabel()
        {
            var stableCount = currentVersions.Count(v => !v.Contains('-'));
            var preReleaseCount = currentVersions.Count(v => v.Contains('-'));
            var filterText = filterType switch
            {
                FilterVersionType.StableOnly => "Stable Only",
                FilterVersionType.PreReleaseOnly => "Pre-release Only",
                _ => "All Versions"
            };

            statusLabel?.SetContent(new List<string> {
                $"[{ColorScheme.SecondaryMarkup}]Showing:[/] [{ColorScheme.PrimaryMarkup}]{filterText}[/] " +
                $"[{ColorScheme.MutedMarkup}]({currentVersions.Count} versions - " +
                $"{stableCount} stable, {preReleaseCount} pre-release)[/]"
            });
        }

        string BuildEnhancedVersionDisplay(string version, string currentVersion, List<string> versions)
        {
            var isCurrent = string.Equals(version, currentVersion, StringComparison.OrdinalIgnoreCase);
            var isLatest = version == versions.FirstOrDefault();
            var isPreRelease = version.Contains('-');

            var badges = new List<string>();
            if (isLatest)
                badges.Add("[green]✓ Latest[/]");
            if (isCurrent)
                badges.Add("[cyan1]● Current[/]");
            if (isPreRelease)
            {
                // Extract pre-release tag (beta, rc, preview, etc.)
                var dashIndex = version.IndexOf('-');
                if (dashIndex > 0 && dashIndex < version.Length - 1)
                {
                    var preReleaseTag = version.Substring(dashIndex + 1);
                    // Take only the tag part (e.g., "beta1" -> "beta")
                    var tagParts = preReleaseTag.Split('.', '-');
                    var tag = tagParts[0];
                    badges.Add($"[yellow]● {tag}[/]");
                }
            }

            var badgeText = badges.Any() ? " " + string.Join(" ", badges) : "";
            var icon = isPreRelease ? "🔶" : "📦";

            return $"{icon} [{ColorScheme.PrimaryMarkup}]{Markup.Escape(version)}[/]{badgeText}";
        }

        void HandleSelect()
        {
            if (versionList?.SelectedItem?.Tag is string ver)
            {
                selectedVersion = ver;
                modal.Close();
            }
        }

        void HandleClearFilter()
        {
            filterType = FilterVersionType.All;
            if (versionTypeDropdown != null) versionTypeDropdown.SelectedIndex = 0;
            RefreshVersionsList();
        }

        // ── Header ────────────────────────────────────────────
        var header = Controls.Markup()
            .AddLine($"[{ColorScheme.PrimaryMarkup} bold]Select Version[/]")
            .AddLine($"[{ColorScheme.SecondaryMarkup}]Choose a version for {Markup.Escape(package.Id)}[/]")
            .AddLine($"[{ColorScheme.MutedMarkup}]Current version: {Markup.Escape(package.Version)}[/]")
            .WithMargin(2, 2, 2, 0)
            .Build();

        // ── Status label ──────────────────────────────────────
        statusLabel = Controls.Markup($"[{ColorScheme.MutedMarkup}]Loading versions...[/]")
            .WithMargin(2, 1, 2, 0)
            .Build();

        // ── Separator ─────────────────────────────────────────
        var separator1 = Controls.RuleBuilder()
            .WithColor(ColorScheme.RuleColor)
            .Build();
        separator1.Margin = new Margin(2, 1, 2, 0);

        // ── Version filter dropdown ───────────────────────────
        versionTypeDropdown = new DropdownControl(string.Empty, new[]
        {
            "All Versions (F1)",
            "Stable Only (F2)",
            "Pre-release Only (F3)"
        })
        {
            SelectedIndex = 0
        };

        versionTypeDropdown.SelectedIndexChanged += (s, idx) =>
        {
            filterType = idx switch
            {
                0 => FilterVersionType.All,
                1 => FilterVersionType.StableOnly,
                2 => FilterVersionType.PreReleaseOnly,
                _ => FilterVersionType.All
            };
            RefreshVersionsList();
        };

        // ── Filter toolbar ────────────────────────────────────
        var filterToolbar = Controls.Toolbar()
            .Add(versionTypeDropdown)
            .WithSpacing(2)
            .WithMargin(2, 0, 2, 1)
            .Build();

        // ── Version list ──────────────────────────────────────
        versionList = Controls.List()
            .SimpleMode()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithColors(ColorScheme.StatusBarBackground, Color.Grey93)
            .WithFocusedColors(ColorScheme.StatusBarBackground, Color.Grey93)
            .WithHighlightColors(Color.Grey35, Color.White)
            .WithMargin(2, 0, 2, 1)
            .Build();

        versionList.SelectedIndexChanged += (s, idx) =>
        {
            var selectedVer = versionList.SelectedItem?.Tag as string;
            if (selectButton != null) selectButton.IsEnabled = selectedVer != null;
        };

        // ── Filter help label ─────────────────────────────────
        filterLabel = Controls.Markup()
            .AddLine($"[{ColorScheme.MutedMarkup}]F1:All  F2:Stable  F3:Pre-release  |  S:Select  C:Clear Filter  Esc:Close[/]")
            .WithMargin(2, 1, 2, 0)
            .StickyBottom()
            .Build();

        // ── Bottom separator ──────────────────────────────────
        var separator2 = Controls.RuleBuilder()
            .WithColor(ColorScheme.RuleColor)
            .StickyBottom()
            .Build();
        separator2.Margin = new Margin(2, 0, 2, 0);

        // ── Action buttons ────────────────────────────────────
        selectButton = Controls.Button("[grey93]Select (S)[/]")
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.DarkGreen)
            .WithFocusedForegroundColor(Color.White)
            .Build();
        selectButton.IsEnabled = false;
        selectButton.Click += (s, e) => HandleSelect();

        clearFilterButton = Controls.Button("[grey93]Clear Filter (C)[/]")
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.DarkOrange)
            .WithFocusedForegroundColor(Color.White)
            .Build();
        clearFilterButton.Click += (s, e) => HandleClearFilter();

        var closeButton = Controls.Button("[grey93]Close (Esc)[/]")
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .Build();
        closeButton.Click += (s, e) => modal.Close();

        var buttonToolbar = Controls.HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .Column(col => col.Add(selectButton))
            .Column(col => col.Width(2))
            .Column(col => col.Add(clearFilterButton))
            .Column(col => col.Width(2))
            .Column(col => col.Add(closeButton))
            .Build();
        buttonToolbar.Margin = new Margin(0, 0, 0, 0);

        // ── Assemble modal ────────────────────────────────────
        modal.AddControl(header);
        modal.AddControl(statusLabel);
        modal.AddControl(separator1);
        modal.AddControl(filterToolbar);
        modal.AddControl(versionList);
        modal.AddControl(filterLabel);
        modal.AddControl(separator2);
        modal.AddControl(buttonToolbar);

        // ── Item activation (double-click or Enter on list) ──
        versionList.ItemActivated += (sender, item) =>
        {
            if (item?.Tag is string ver)
            {
                selectedVersion = ver;
                modal.Close();
            }
        };

        // ── Keyboard handling ─────────────────────────────────
        modal.KeyPressed += (sender, e) =>
        {
            if (e.KeyInfo.Key == ConsoleKey.Escape)
            {
                selectedVersion = null;
                modal.Close();
                e.Handled = true;
                return;
            }

            if (e.AlreadyHandled) { e.Handled = true; return; }

            // F-key filtering
            if (e.KeyInfo.Key == ConsoleKey.F1)
            {
                filterType = FilterVersionType.All;
                if (versionTypeDropdown != null) versionTypeDropdown.SelectedIndex = 0;
                RefreshVersionsList();
                e.Handled = true;
            }
            else if (e.KeyInfo.Key == ConsoleKey.F2)
            {
                filterType = FilterVersionType.StableOnly;
                if (versionTypeDropdown != null) versionTypeDropdown.SelectedIndex = 1;
                RefreshVersionsList();
                e.Handled = true;
            }
            else if (e.KeyInfo.Key == ConsoleKey.F3)
            {
                filterType = FilterVersionType.PreReleaseOnly;
                if (versionTypeDropdown != null) versionTypeDropdown.SelectedIndex = 2;
                RefreshVersionsList();
                e.Handled = true;
            }
            // Action shortcuts
            else if (e.KeyInfo.Key == ConsoleKey.S && selectButton != null && selectButton.IsEnabled)
            {
                HandleSelect();
                e.Handled = true;
            }
            else if (e.KeyInfo.Key == ConsoleKey.C)
            {
                HandleClearFilter();
                e.Handled = true;
            }
            else if (e.KeyInfo.Key == ConsoleKey.Enter)
            {
                // Select from version list
                if (versionList.SelectedItem?.Tag is string ver)
                {
                    selectedVersion = ver;
                    modal.Close();
                    e.Handled = true;
                }
            }
        };

        modal.OnClosed += (s, e) => tcs.TrySetResult(selectedVersion);

        // Show modal
        windowSystem.AddWindow(modal);
        windowSystem.SetActiveWindow(modal);

        // Initial load
        RefreshVersionsList();

        versionList.SetFocus(true, FocusReason.Programmatic);

        return await tcs.Task;
    }
}
