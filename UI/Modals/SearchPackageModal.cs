using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
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
/// Search NuGet.org modal with text input and results list.
/// Returns the selected NuGetPackage or null if cancelled.
/// </summary>
public class SearchPackageModal : ModalBase<NuGetPackage?>
{
    private enum FilterVersionType
    {
        All,
        StableOnly,
        PreReleaseOnly
    }

    // Dependencies
    private readonly NuGetClientService _nugetService;

    // Controls
    private PromptControl? _searchInput;
    private ListControl? _resultsList;
    private MarkupControl? _statusLabel;
    private MarkupControl? _filterLabel;
    private ProgressBarControl? _searchProgress;
    private ButtonControl? _installButton;
    private ButtonControl? _clearButton;
    private DropdownControl? _versionTypeDropdown;

    // State
    private CancellationTokenSource? _searchCts;
    private FilterVersionType _filterType = FilterVersionType.All;
    private List<NuGetPackage> _currentResults = new();
    private List<NuGetPackage> _allResults = new();

    // Event handlers for cleanup
    private EventHandler<string>? _inputChangedHandler;
    private EventHandler<ListItem>? _itemActivatedHandler;
    private EventHandler<int>? _listSelectionHandler;
    private EventHandler<int>? _dropdownHandler;
    private EventHandler<ButtonControl>? _installClickHandler;
    private EventHandler<ButtonControl>? _clearClickHandler;
    private EventHandler<ButtonControl>? _closeClickHandler;

    private SearchPackageModal(NuGetClientService nugetService)
    {
        _nugetService = nugetService;
    }

    /// <summary>
    /// Show a search modal and return the selected package (or null if cancelled)
    /// </summary>
    public static Task<NuGetPackage?> ShowAsync(
        ConsoleWindowSystem windowSystem,
        NuGetClientService nugetService,
        Window? parentWindow = null)
    {
        var instance = new SearchPackageModal(nugetService);
        return ((ModalBase<NuGetPackage?>)instance).ShowAsync(windowSystem, parentWindow);
    }

    protected override string GetTitle() => "Search NuGet.org";
    protected override (int width, int height) GetSize() => (100, 30);
    protected override BorderStyle GetBorderStyle() => BorderStyle.DoubleLine;
    protected override Color GetBorderColor() => ColorScheme.BorderColor;
    protected override NuGetPackage? GetDefaultResult() => null;

    protected override void BuildContent()
    {
        // ── Header ────────────────────────────────────────────
        var header = Controls.Markup()
            .AddLine($"[{ColorScheme.PrimaryMarkup} bold]Search NuGet.org[/]")
            .AddLine($"[{ColorScheme.SecondaryMarkup}]Find and install packages from NuGet.org[/]")
            .WithMargin(2, 2, 2, 0)
            .Build();

        // ── Status label ──────────────────────────────────────
        _statusLabel = Controls.Markup($"[{ColorScheme.MutedMarkup}]Type to search[/]")
            .WithMargin(2, 1, 2, 0)
            .Build();

        // ── Separator ─────────────────────────────────────────
        var separator1 = Controls.RuleBuilder()
            .WithColor(ColorScheme.RuleColor)
            .Build();
        separator1.Margin = new Margin(2, 1, 2, 0);

        // ── Search input ──────────────────────────────────────
        _searchInput = Controls.Prompt("Search: ")
            .WithInput("")
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithMargin(2, 0, 2, 0)
            .Build();

        // ── Search progress (indeterminate, hidden by default) ──
        _searchProgress = Controls.ProgressBar()
            .Indeterminate(true)
            .WithMargin(2, 0, 2, 0)
            .Build();
        _searchProgress.Visible = false;

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

        _dropdownHandler = (s, idx) =>
        {
            _filterType = idx switch
            {
                0 => FilterVersionType.All,
                1 => FilterVersionType.StableOnly,
                2 => FilterVersionType.PreReleaseOnly,
                _ => FilterVersionType.All
            };
            RefreshResultsList();
        };
        _versionTypeDropdown.SelectedIndexChanged += _dropdownHandler;

        // ── Filter toolbar ────────────────────────────────────
        var filterToolbar = Controls.Toolbar()
            .Add(_versionTypeDropdown)
            .WithSpacing(2)
            .WithMargin(2, 0, 2, 1)
            .Build();

        // ── Results list ────────────────────────────────────
        _resultsList = Controls.List()
            .SimpleMode()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithColors(Color.Grey93, ColorScheme.StatusBarBackground)
            .WithFocusedColors(Color.Grey93, ColorScheme.StatusBarBackground)
            .WithHighlightColors(Color.White, Color.Grey35)
            .WithMargin(2, 0, 2, 1)
            .Build();

        _listSelectionHandler = (s, idx) =>
        {
            var selectedPkg = _resultsList?.SelectedItem?.Tag as NuGetPackage;
            if (_installButton != null) _installButton.IsEnabled = selectedPkg != null;
        };
        _resultsList.SelectedIndexChanged += _listSelectionHandler;

        // ── Filter help label ─────────────────────────────────
        _filterLabel = Controls.Markup()
            .AddLine($"[{ColorScheme.MutedMarkup}]F1:All  F2:Stable  F3:Pre-release  |  I:Install  C:Clear  Esc:Close[/]")
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
        _installButton = Controls.Button("[grey93]Install (I)[/]")
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.DarkGreen)
            .WithFocusedForegroundColor(Color.White)
            .Build();
        _installButton.IsEnabled = false;
        _installClickHandler = (s, e) => HandleInstall();
        _installButton.Click += _installClickHandler;

        _clearButton = Controls.Button("[grey93]Clear (C)[/]")
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.DarkOrange)
            .WithFocusedForegroundColor(Color.White)
            .Build();
        _clearClickHandler = (s, e) => HandleClear();
        _clearButton.Click += _clearClickHandler;

        var closeButton = Controls.Button("[grey93]Close (Esc)[/]")
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .Build();
        _closeClickHandler = (s, e) => CloseWithResult(null);
        closeButton.Click += _closeClickHandler;

        var buttonToolbar = Controls.HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .Column(col => col.Add(_installButton))
            .Column(col => col.Width(2))
            .Column(col => col.Add(_clearButton))
            .Column(col => col.Width(2))
            .Column(col => col.Add(closeButton))
            .Build();
        buttonToolbar.Margin = new Margin(0, 0, 0, 0);

        // ── Assemble modal ────────────────────────────────────
        Modal.AddControl(header);
        Modal.AddControl(_statusLabel);
        Modal.AddControl(separator1);
        Modal.AddControl(_searchInput);
        Modal.AddControl(_searchProgress);
        Modal.AddControl(filterToolbar);
        Modal.AddControl(_resultsList);
        Modal.AddControl(_filterLabel);
        Modal.AddControl(separator2);
        Modal.AddControl(buttonToolbar);

        // ── Debounced search on text change ─────────────────
        _inputChangedHandler = (sender, e) =>
        {
            var query = _searchInput?.Input?.Trim() ?? "";

            // Cancel any pending search
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var ct = _searchCts.Token;

            if (query.Length < 2)
            {
                _resultsList?.ClearItems();
                _allResults.Clear();
                _currentResults.Clear();
                if (_searchProgress != null) _searchProgress.Visible = false;
                _statusLabel?.SetContent(new List<string>
                {
                    query.Length == 0
                        ? $"[{ColorScheme.MutedMarkup}]Type to search[/]"
                        : $"[{ColorScheme.MutedMarkup}]Type at least 2 characters[/]"
                });
                if (_installButton != null) _installButton.IsEnabled = false;
                return;
            }

            if (_searchProgress != null) _searchProgress.Visible = true;
            _statusLabel?.SetContent(new List<string> { $"[{ColorScheme.MutedMarkup}]Searching...[/]" });

            AsyncHelper.FireAndForget(async () =>
            {
                // Debounce: wait 400ms before searching
                await Task.Delay(400, ct);
                if (ct.IsCancellationRequested) return;

                var results = await _nugetService.SearchPackagesAsync(query, 15, ct);
                if (ct.IsCancellationRequested) return;

                _allResults = results;

                if (_searchProgress != null) _searchProgress.Visible = false;

                if (!results.Any())
                {
                    _statusLabel?.SetContent(new List<string> { $"[{ColorScheme.MutedMarkup}]No results found[/]" });
                    _resultsList?.ClearItems();
                    _currentResults.Clear();
                    if (_installButton != null) _installButton.IsEnabled = false;
                    return;
                }

                RefreshResultsList();
            },
            ex =>
            {
                if (_searchProgress != null) _searchProgress.Visible = false;
                _statusLabel?.SetContent(new List<string> { $"[{ColorScheme.ErrorMarkup}]Search failed[/]" });
            });
        };
        _searchInput.InputChanged += _inputChangedHandler;

        // ── Enter on results list to select ─────────────────
        _itemActivatedHandler = (sender, item) =>
        {
            if (item?.Tag is NuGetPackage pkg)
            {
                CloseWithResult(pkg);
            }
        };
        _resultsList.ItemActivated += _itemActivatedHandler;
    }

    protected override void SetInitialFocus()
    {
        _searchInput?.SetFocus(true, FocusReason.Programmatic);
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        // Redirect Up/Down to results list when it doesn't have focus (e.g. search
        // input is focused). Must run BEFORE AlreadyHandled — the dispatcher
        // consumes arrows for window scrolling. When the list HAS focus, its own
        // ProcessKey already handled the move — don't double-move.
        if (e.KeyInfo.Key == ConsoleKey.UpArrow && _resultsList != null && !_resultsList.HasFocus && _resultsList.Items.Count > 0)
        {
            if (_resultsList.SelectedIndex > 0)
                _resultsList.SelectedIndex--;
            e.Handled = true;
            return;
        }
        if (e.KeyInfo.Key == ConsoleKey.DownArrow && _resultsList != null && !_resultsList.HasFocus && _resultsList.Items.Count > 0)
        {
            if (_resultsList.SelectedIndex < _resultsList.Items.Count - 1)
                _resultsList.SelectedIndex++;
            e.Handled = true;
            return;
        }

        // Escape must run BEFORE AlreadyHandled — dispatcher consumes
        // the first Escape to unfocus the search input control.
        if (e.KeyInfo.Key == ConsoleKey.Escape)
        {
            CloseWithResult(null);
            e.Handled = true;
            return;
        }

        if (e.AlreadyHandled) { e.Handled = true; return; }

        // F-key filtering
        if (e.KeyInfo.Key == ConsoleKey.F1)
        {
            _filterType = FilterVersionType.All;
            if (_versionTypeDropdown != null) _versionTypeDropdown.SelectedIndex = 0;
            RefreshResultsList();
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.F2)
        {
            _filterType = FilterVersionType.StableOnly;
            if (_versionTypeDropdown != null) _versionTypeDropdown.SelectedIndex = 1;
            RefreshResultsList();
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.F3)
        {
            _filterType = FilterVersionType.PreReleaseOnly;
            if (_versionTypeDropdown != null) _versionTypeDropdown.SelectedIndex = 2;
            RefreshResultsList();
            e.Handled = true;
        }
        // Action shortcuts
        else if (e.KeyInfo.Key == ConsoleKey.I && _installButton != null && _installButton.IsEnabled)
        {
            HandleInstall();
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.C)
        {
            HandleClear();
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.Enter)
        {
            // Select from results list (works regardless of which control has focus)
            if (_resultsList?.SelectedItem?.Tag is NuGetPackage pkg)
            {
                CloseWithResult(pkg);
                e.Handled = true;
            }
        }
        else if (e.KeyInfo.Key == ConsoleKey.Tab)
        {
            // Tab cycles through: search input → dropdown → results → buttons
            // The default tab handling should work with the new controls
            // No custom handling needed - let the framework handle it
        }
    }

    protected override void OnCleanup()
    {
        // Cancel any pending search operations
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;

        // Unsubscribe from event handlers to prevent memory leaks
        if (_searchInput != null && _inputChangedHandler != null)
            _searchInput.InputChanged -= _inputChangedHandler;

        if (_resultsList != null)
        {
            if (_listSelectionHandler != null)
                _resultsList.SelectedIndexChanged -= _listSelectionHandler;
            if (_itemActivatedHandler != null)
                _resultsList.ItemActivated -= _itemActivatedHandler;
        }

        if (_versionTypeDropdown != null && _dropdownHandler != null)
            _versionTypeDropdown.SelectedIndexChanged -= _dropdownHandler;

        if (_installButton != null && _installClickHandler != null)
            _installButton.Click -= _installClickHandler;

        if (_clearButton != null && _clearClickHandler != null)
            _clearButton.Click -= _clearClickHandler;
    }

    // ── Helper: Refresh results list based on filters ────────────
    private void RefreshResultsList()
    {
        // Apply version type filtering
        var filtered = _filterType switch
        {
            FilterVersionType.StableOnly => _allResults.Where(p => !p.Version.Contains('-')),
            FilterVersionType.PreReleaseOnly => _allResults.Where(p => p.Version.Contains('-')),
            _ => _allResults
        };

        _currentResults = filtered.ToList();

        // Update status label
        UpdateStatusLabel();

        // Populate list
        _resultsList?.ClearItems();

        if (!_currentResults.Any())
        {
            var emptyItem = new ListItem($"[{ColorScheme.MutedMarkup}]No packages found matching current filters[/]");
            emptyItem.Tag = null;
            _resultsList?.AddItem(emptyItem);
            if (_installButton != null) _installButton.IsEnabled = false;
            return;
        }

        foreach (var pkg in _currentResults)
        {
            var displayText = BuildEnhancedPackageDisplay(pkg);
            var listItem = new ListItem(displayText);
            listItem.Tag = pkg;
            _resultsList?.AddItem(listItem);
        }

        // Enable/disable install button based on selection
        var selectedPkg = _resultsList?.SelectedItem?.Tag as NuGetPackage;
        if (_installButton != null) _installButton.IsEnabled = selectedPkg != null;
    }

    private void UpdateStatusLabel()
    {
        var verifiedCount = _currentResults.Count(p => p.IsVerified);
        var deprecatedCount = _currentResults.Count(p => p.IsDeprecated);
        var vulnerableCount = _currentResults.Count(p => p.VulnerabilityCount > 0);
        var filterText = _filterType switch
        {
            FilterVersionType.StableOnly => "Stable Only",
            FilterVersionType.PreReleaseOnly => "Pre-release Only",
            _ => "All Versions"
        };

        _statusLabel?.SetContent(new List<string> {
            $"[{ColorScheme.SecondaryMarkup}]Showing:[/] [{ColorScheme.PrimaryMarkup}]{filterText}[/] " +
            $"[{ColorScheme.MutedMarkup}]({_currentResults.Count} packages - " +
            $"{verifiedCount} verified, {deprecatedCount} deprecated, {vulnerableCount} vulnerable)[/]"
        });
    }

    private string BuildEnhancedPackageDisplay(NuGetPackage pkg)
    {
        var badges = new List<string>();
        if (pkg.IsVerified)
            badges.Add("[green]✓ Verified[/]");
        if (pkg.VulnerabilityCount > 0)
            badges.Add($"[red]⚠ {pkg.VulnerabilityCount} Vulnerabilities[/]");
        if (pkg.IsDeprecated)
            badges.Add("[yellow]⚠ Deprecated[/]");

        var badgeText = badges.Any() ? " " + string.Join(" ", badges) : "";
        var authors = pkg.Authors.Any() ? string.Join(", ", pkg.Authors.Take(2)) : "Unknown";
        if (pkg.Authors.Count > 2)
            authors += $" +{pkg.Authors.Count - 2} more";

        var description = pkg.Description;
        if (description.Length > 80)
            description = description.Substring(0, 77) + "...";

        var downloads = FormatDownloads(pkg.TotalDownloads);

        return $"📦 [{ColorScheme.PrimaryMarkup}]{Markup.Escape(pkg.Id)}[/] " +
               $"[grey70]v{Markup.Escape(pkg.Version)}[/]{badgeText}\n" +
               $"    [{ColorScheme.MutedMarkup}]{Markup.Escape(authors)} · {downloads} downloads[/]\n" +
               $"    [{ColorScheme.MutedMarkup}]{Markup.Escape(description)}[/]";
    }

    private void HandleInstall()
    {
        if (_resultsList?.SelectedItem?.Tag is NuGetPackage pkg)
        {
            CloseWithResult(pkg);
        }
    }

    private void HandleClear()
    {
        _searchInput?.SetInput(string.Empty);
        _resultsList?.ClearItems();
        _allResults.Clear();
        _currentResults.Clear();
        if (_searchProgress != null) _searchProgress.Visible = false;
        _statusLabel?.SetContent(new List<string> { $"[{ColorScheme.MutedMarkup}]Type to search[/]" });
        if (_installButton != null) _installButton.IsEnabled = false;
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
