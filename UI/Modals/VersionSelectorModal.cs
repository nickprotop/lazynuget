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
public class VersionSelectorModal : ModalBase<string?>
{
    private enum FilterVersionType
    {
        All,
        StableOnly,
        PreReleaseOnly
    }

    // Parameters
    private readonly PackageReference _package;
    private readonly List<string> _availableVersions;

    // Filter state
    private FilterVersionType _filterType = FilterVersionType.All;
    private List<string> _currentVersions = new();

    // Controls
    private ListControl? _versionList;
    private MarkupControl? _statusLabel;
    private MarkupControl? _filterLabel;
    private ButtonControl? _selectButton;
    private ButtonControl? _clearFilterButton;
    private ButtonControl? _closeButton;
    private DropdownControl? _versionTypeDropdown;

    // Event handler references for cleanup (CRITICAL for memory leak prevention)
    private EventHandler<int>? _versionTypeDropdownHandler;
    private EventHandler<int>? _versionListSelectedIndexChangedHandler;
    private EventHandler<ListItem>? _versionListItemActivatedHandler;
    private EventHandler<ButtonControl>? _selectButtonClickHandler;
    private EventHandler<ButtonControl>? _clearFilterButtonClickHandler;
    private EventHandler<ButtonControl>? _closeButtonClickHandler;

    private VersionSelectorModal(PackageReference package, List<string> availableVersions)
    {
        _package = package;
        _availableVersions = availableVersions;
    }

    public static Task<string?> ShowAsync(
        ConsoleWindowSystem windowSystem,
        PackageReference package,
        List<string> availableVersions,
        Window? parentWindow = null)
    {
        var instance = new VersionSelectorModal(package, availableVersions);
        return ((ModalBase<string?>)instance).ShowAsync(windowSystem, parentWindow);
    }

    protected override string GetTitle() => $"Select Version - {_package.Id}";
    protected override (int width, int height) GetSize() => (100, 30);
    protected override BorderStyle GetBorderStyle() => BorderStyle.DoubleLine;
    protected override Color GetBorderColor() => ColorScheme.BorderColor;
    protected override string? GetDefaultResult() => null;

    protected override void BuildContent()
    {
        // ── Header ────────────────────────────────────────────
        var header = Controls.Markup()
            .AddLine($"[{ColorScheme.PrimaryMarkup} bold]Select Version[/]")
            .AddLine($"[{ColorScheme.SecondaryMarkup}]Choose a version for {Markup.Escape(_package.Id)}[/]")
            .AddLine($"[{ColorScheme.MutedMarkup}]Current version: {Markup.Escape(_package.Version)}[/]")
            .WithMargin(2, 2, 2, 0)
            .Build();

        // ── Status label ──────────────────────────────────────
        _statusLabel = Controls.Markup($"[{ColorScheme.MutedMarkup}]Loading versions...[/]")
            .WithMargin(2, 1, 2, 0)
            .Build();

        // ── Separator ─────────────────────────────────────────
        var separator1 = Controls.RuleBuilder()
            .WithColor(ColorScheme.RuleColor)
            .Build();
        separator1.Margin = new Margin(2, 1, 2, 0);

        // ── Version filter dropdown ───────────────────────────
        _versionTypeDropdown = new DropdownControl(string.Empty, new[]
        {
            "All Versions (F1)",
            "Stable Only (F2)",
            "Pre-release Only (F3)"
        })
        {
            SelectedIndex = 0
        };

        _versionTypeDropdownHandler = (s, idx) =>
        {
            _filterType = idx switch
            {
                0 => FilterVersionType.All,
                1 => FilterVersionType.StableOnly,
                2 => FilterVersionType.PreReleaseOnly,
                _ => FilterVersionType.All
            };
            RefreshVersionsList();
        };
        _versionTypeDropdown.SelectedIndexChanged += _versionTypeDropdownHandler;

        // ── Filter toolbar ────────────────────────────────────
        var filterToolbar = Controls.Toolbar()
            .Add(_versionTypeDropdown)
            .WithSpacing(2)
            .WithMargin(2, 0, 2, 1)
            .Build();

        // ── Version list ──────────────────────────────────────
        _versionList = Controls.List()
            .SimpleMode()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithColors(Color.Grey93, ColorScheme.StatusBarBackground)
            .WithFocusedColors(Color.Grey93, ColorScheme.StatusBarBackground)
            .WithHighlightColors(Color.White, Color.Grey35)
            .WithMargin(2, 0, 2, 1)
            .Build();

        _versionListSelectedIndexChangedHandler = (s, idx) =>
        {
            var selectedVer = _versionList?.SelectedItem?.Tag as string;
            if (_selectButton != null) _selectButton.IsEnabled = selectedVer != null;
        };
        _versionList.SelectedIndexChanged += _versionListSelectedIndexChangedHandler;

        _versionListItemActivatedHandler = (sender, item) =>
        {
            if (item?.Tag is string ver)
            {
                CloseWithResult(ver);
            }
        };
        _versionList.ItemActivated += _versionListItemActivatedHandler;

        // ── Filter help label ─────────────────────────────────
        _filterLabel = Controls.Markup()
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
        _selectButton = Controls.Button("[grey93]Select (S)[/]")
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.DarkGreen)
            .WithFocusedForegroundColor(Color.White)
            .Build();
        _selectButton.IsEnabled = false;
        _selectButtonClickHandler = (s, e) => HandleSelect();
        _selectButton.Click += _selectButtonClickHandler;

        _clearFilterButton = Controls.Button("[grey93]Clear Filter (C)[/]")
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.DarkOrange)
            .WithFocusedForegroundColor(Color.White)
            .Build();
        _clearFilterButtonClickHandler = (s, e) => HandleClearFilter();
        _clearFilterButton.Click += _clearFilterButtonClickHandler;

        _closeButton = Controls.Button("[grey93]Close (Esc)[/]")
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .Build();
        _closeButtonClickHandler = (s, e) => CloseWithResult(null);
        _closeButton.Click += _closeButtonClickHandler;

        var buttonToolbar = Controls.HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .Column(col => col.Add(_selectButton))
            .Column(col => col.Width(2))
            .Column(col => col.Add(_clearFilterButton))
            .Column(col => col.Width(2))
            .Column(col => col.Add(_closeButton))
            .Build();
        buttonToolbar.Margin = new Margin(0, 0, 0, 0);

        // ── Assemble modal ────────────────────────────────────
        Modal.AddControl(header);
        Modal.AddControl(_statusLabel);
        Modal.AddControl(separator1);
        Modal.AddControl(filterToolbar);
        Modal.AddControl(_versionList);
        Modal.AddControl(_filterLabel);
        Modal.AddControl(separator2);
        Modal.AddControl(buttonToolbar);

        // Initial load
        RefreshVersionsList();
    }

    protected override void SetInitialFocus()
    {
        _versionList?.SetFocus(true, FocusReason.Programmatic);
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.AlreadyHandled)
        {
            e.Handled = true;
            return;
        }

        // F-key filtering
        if (e.KeyInfo.Key == ConsoleKey.F1)
        {
            _filterType = FilterVersionType.All;
            if (_versionTypeDropdown != null) _versionTypeDropdown.SelectedIndex = 0;
            RefreshVersionsList();
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.F2)
        {
            _filterType = FilterVersionType.StableOnly;
            if (_versionTypeDropdown != null) _versionTypeDropdown.SelectedIndex = 1;
            RefreshVersionsList();
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.F3)
        {
            _filterType = FilterVersionType.PreReleaseOnly;
            if (_versionTypeDropdown != null) _versionTypeDropdown.SelectedIndex = 2;
            RefreshVersionsList();
            e.Handled = true;
        }
        // Action shortcuts
        else if (e.KeyInfo.Key == ConsoleKey.S && _selectButton != null && _selectButton.IsEnabled)
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
            if (_versionList?.SelectedItem?.Tag is string ver)
            {
                CloseWithResult(ver);
                e.Handled = true;
            }
        }
        else
        {
            base.OnKeyPressed(sender, e); // Handle Escape and other default keys
        }
    }

    /// <summary>
    /// CRITICAL: Cleanup event handlers to prevent memory leaks
    /// </summary>
    protected override void OnCleanup()
    {
        // Unsubscribe from all event handlers
        if (_versionTypeDropdown != null && _versionTypeDropdownHandler != null)
        {
            _versionTypeDropdown.SelectedIndexChanged -= _versionTypeDropdownHandler;
        }

        if (_versionList != null)
        {
            if (_versionListSelectedIndexChangedHandler != null)
                _versionList.SelectedIndexChanged -= _versionListSelectedIndexChangedHandler;
            if (_versionListItemActivatedHandler != null)
                _versionList.ItemActivated -= _versionListItemActivatedHandler;
        }

        if (_selectButton != null && _selectButtonClickHandler != null)
        {
            _selectButton.Click -= _selectButtonClickHandler;
        }

        if (_clearFilterButton != null && _clearFilterButtonClickHandler != null)
        {
            _clearFilterButton.Click -= _clearFilterButtonClickHandler;
        }

        if (_closeButton != null && _closeButtonClickHandler != null)
        {
            _closeButton.Click -= _closeButtonClickHandler;
        }
    }

    // ── Helper Methods ────────────────────────────────────────

    private void RefreshVersionsList()
    {
        // Apply version type filtering
        var filtered = _filterType switch
        {
            FilterVersionType.StableOnly => _availableVersions.Where(v => !v.Contains('-')),
            FilterVersionType.PreReleaseOnly => _availableVersions.Where(v => v.Contains('-')),
            _ => _availableVersions
        };

        _currentVersions = filtered.ToList();

        // Update status label
        UpdateStatusLabel();

        // Populate list
        _versionList?.ClearItems();

        if (!_currentVersions.Any())
        {
            var emptyItem = new ListItem($"[{ColorScheme.MutedMarkup}]No versions found matching current filters[/]");
            emptyItem.Tag = null;
            _versionList?.AddItem(emptyItem);
            if (_selectButton != null) _selectButton.IsEnabled = false;
            return;
        }

        foreach (var version in _currentVersions)
        {
            var displayText = BuildEnhancedVersionDisplay(version, _package.Version, _currentVersions);
            var listItem = new ListItem(displayText);
            listItem.Tag = version;
            _versionList?.AddItem(listItem);
        }

        // Set initial selection to current version if found
        var currentIndex = _currentVersions.FindIndex(v =>
            string.Equals(v, _package.Version, StringComparison.OrdinalIgnoreCase));
        if (currentIndex >= 0 && _versionList != null)
            _versionList.SelectedIndex = currentIndex;

        // Enable/disable select button based on selection
        var selectedVer = _versionList?.SelectedItem?.Tag as string;
        if (_selectButton != null) _selectButton.IsEnabled = selectedVer != null;
    }

    private void UpdateStatusLabel()
    {
        var stableCount = _currentVersions.Count(v => !v.Contains('-'));
        var preReleaseCount = _currentVersions.Count(v => v.Contains('-'));
        var filterText = _filterType switch
        {
            FilterVersionType.StableOnly => "Stable Only",
            FilterVersionType.PreReleaseOnly => "Pre-release Only",
            _ => "All Versions"
        };

        _statusLabel?.SetContent(new List<string> {
            $"[{ColorScheme.SecondaryMarkup}]Showing:[/] [{ColorScheme.PrimaryMarkup}]{filterText}[/] " +
            $"[{ColorScheme.MutedMarkup}]({_currentVersions.Count} versions - " +
            $"{stableCount} stable, {preReleaseCount} pre-release)[/]"
        });
    }

    private string BuildEnhancedVersionDisplay(string version, string currentVersion, List<string> versions)
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
                var tag = tagParts.Length > 0 ? tagParts[0] : preReleaseTag;
                badges.Add($"[yellow]● {tag}[/]");
            }
        }

        var badgeText = badges.Any() ? " " + string.Join(" ", badges) : "";
        var icon = isPreRelease ? "🔶" : "📦";

        return $"{icon} [{ColorScheme.PrimaryMarkup}]{Markup.Escape(version)}[/]{badgeText}";
    }

    private void HandleSelect()
    {
        if (_versionList?.SelectedItem?.Tag is string ver)
        {
            CloseWithResult(ver);
        }
    }

    private void HandleClearFilter()
    {
        _filterType = FilterVersionType.All;
        if (_versionTypeDropdown != null) _versionTypeDropdown.SelectedIndex = 0;
        RefreshVersionsList();
    }
}
