using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
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
/// Search NuGet.org modal with text input and results list.
/// Returns the selected NuGetPackage or null if cancelled.
/// </summary>
public static class SearchPackageModal
{
    private enum FilterVersionType
    {
        All,
        StableOnly,
        PreReleaseOnly
    }
    /// <summary>
    /// Show a search modal and return the selected package (or null if cancelled)
    /// </summary>
    public static Task<NuGetPackage?> ShowAsync(
        ConsoleWindowSystem windowSystem,
        NuGetClientService nugetService,
        Window? parentWindow = null)
    {
        var tcs = new TaskCompletionSource<NuGetPackage?>();
        NuGetPackage? selectedPackage = null;
        CancellationTokenSource? searchCts = null;

        // Filter state
        var filterType = FilterVersionType.All;

        // Controls
        PromptControl? searchInput = null;
        ListControl? resultsList = null;
        MarkupControl? statusLabel = null;
        MarkupControl? filterLabel = null;
        ProgressBarControl? searchProgress = null;
        ButtonControl? installButton = null;
        ButtonControl? clearButton = null;
        DropdownControl? versionTypeDropdown = null;

        var currentResults = new List<NuGetPackage>();
        var allResults = new List<NuGetPackage>();

        var modal = new WindowBuilder(windowSystem)
            .WithTitle("Search NuGet.org")
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

        // ── Helper: Refresh results list based on filters ────────────
        void RefreshResultsList()
        {
            // Apply version type filtering
            var filtered = filterType switch
            {
                FilterVersionType.StableOnly => allResults.Where(p => !p.Version.Contains('-')),
                FilterVersionType.PreReleaseOnly => allResults.Where(p => p.Version.Contains('-')),
                _ => allResults
            };

            currentResults = filtered.ToList();

            // Update status label
            UpdateStatusLabel();

            // Populate list
            resultsList?.ClearItems();

            if (!currentResults.Any())
            {
                var emptyItem = new ListItem($"[{ColorScheme.MutedMarkup}]No packages found matching current filters[/]");
                emptyItem.Tag = null;
                resultsList?.AddItem(emptyItem);
                if (installButton != null) installButton.IsEnabled = false;
                return;
            }

            foreach (var pkg in currentResults)
            {
                var displayText = BuildEnhancedPackageDisplay(pkg);
                var listItem = new ListItem(displayText);
                listItem.Tag = pkg;
                resultsList?.AddItem(listItem);
            }

            // Enable/disable install button based on selection
            var selectedPkg = resultsList?.SelectedItem?.Tag as NuGetPackage;
            if (installButton != null) installButton.IsEnabled = selectedPkg != null;
        }

        void UpdateStatusLabel()
        {
            var verifiedCount = currentResults.Count(p => p.IsVerified);
            var deprecatedCount = currentResults.Count(p => p.IsDeprecated);
            var vulnerableCount = currentResults.Count(p => p.VulnerabilityCount > 0);
            var filterText = filterType switch
            {
                FilterVersionType.StableOnly => "Stable Only",
                FilterVersionType.PreReleaseOnly => "Pre-release Only",
                _ => "All Versions"
            };

            statusLabel?.SetContent(new List<string> {
                $"[{ColorScheme.SecondaryMarkup}]Showing:[/] [{ColorScheme.PrimaryMarkup}]{filterText}[/] " +
                $"[{ColorScheme.MutedMarkup}]({currentResults.Count} packages - " +
                $"{verifiedCount} verified, {deprecatedCount} deprecated, {vulnerableCount} vulnerable)[/]"
            });
        }

        string BuildEnhancedPackageDisplay(NuGetPackage pkg)
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
                   $"[grey70]v{pkg.Version}[/]{badgeText}\n" +
                   $"    [{ColorScheme.MutedMarkup}]{Markup.Escape(authors)} · {downloads} downloads[/]\n" +
                   $"    [{ColorScheme.MutedMarkup}]{Markup.Escape(description)}[/]";
        }

        void HandleInstall()
        {
            if (resultsList?.SelectedItem?.Tag is NuGetPackage pkg)
            {
                selectedPackage = pkg;
                modal.Close();
            }
        }

        void HandleClear()
        {
            searchInput?.SetInput(string.Empty);
            resultsList?.ClearItems();
            allResults.Clear();
            currentResults.Clear();
            searchProgress!.Visible = false;
            statusLabel?.SetContent(new List<string> { $"[{ColorScheme.MutedMarkup}]Type to search[/]" });
            if (installButton != null) installButton.IsEnabled = false;
        }

        // ── Header ────────────────────────────────────────────
        var header = Controls.Markup()
            .AddLine($"[{ColorScheme.PrimaryMarkup} bold]Search NuGet.org[/]")
            .AddLine($"[{ColorScheme.SecondaryMarkup}]Find and install packages from NuGet.org[/]")
            .WithMargin(2, 2, 2, 0)
            .Build();

        // ── Status label ──────────────────────────────────────
        statusLabel = Controls.Markup($"[{ColorScheme.MutedMarkup}]Type to search[/]")
            .WithMargin(2, 1, 2, 0)
            .Build();

        // ── Separator ─────────────────────────────────────────
        var separator1 = Controls.RuleBuilder()
            .WithColor(ColorScheme.RuleColor)
            .Build();
        separator1.Margin = new Margin(2, 1, 2, 0);

        // ── Search input ──────────────────────────────────────
        searchInput = Controls.Prompt("Search: ")
            .WithInput("")
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithMargin(2, 0, 2, 0)
            .Build();

        // ── Search progress (indeterminate, hidden by default) ──
        searchProgress = Controls.ProgressBar()
            .Indeterminate(true)
            .WithMargin(2, 0, 2, 0)
            .Build();
        searchProgress.Visible = false;

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
            RefreshResultsList();
        };

        // ── Filter toolbar ────────────────────────────────────
        var filterToolbar = Controls.Toolbar()
            .Add(versionTypeDropdown)
            .WithSpacing(2)
            .WithMargin(2, 0, 2, 1)
            .Build();

        // ── Results list ────────────────────────────────────
        resultsList = Controls.List()
            .SimpleMode()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithColors(ColorScheme.StatusBarBackground, Color.Grey93)
            .WithFocusedColors(ColorScheme.StatusBarBackground, Color.Grey93)
            .WithHighlightColors(Color.Grey35, Color.White)
            .WithMargin(2, 0, 2, 1)
            .Build();

        resultsList.SelectedIndexChanged += (s, idx) =>
        {
            var selectedPkg = resultsList.SelectedItem?.Tag as NuGetPackage;
            if (installButton != null) installButton.IsEnabled = selectedPkg != null;
        };

        // ── Filter help label ─────────────────────────────────
        filterLabel = Controls.Markup()
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
        installButton = Controls.Button("[grey93]Install (I)[/]")
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.DarkGreen)
            .WithFocusedForegroundColor(Color.White)
            .Build();
        installButton.IsEnabled = false;
        installButton.Click += (s, e) => HandleInstall();

        clearButton = Controls.Button("[grey93]Clear (C)[/]")
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.DarkOrange)
            .WithFocusedForegroundColor(Color.White)
            .Build();
        clearButton.Click += (s, e) => HandleClear();

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
            .Column(col => col.Add(installButton))
            .Column(col => col.Width(2))
            .Column(col => col.Add(clearButton))
            .Column(col => col.Width(2))
            .Column(col => col.Add(closeButton))
            .Build();
        buttonToolbar.Margin = new Margin(0, 0, 0, 0);

        // ── Assemble modal ────────────────────────────────────
        modal.AddControl(header);
        modal.AddControl(statusLabel);
        modal.AddControl(separator1);
        modal.AddControl(searchInput);
        modal.AddControl(searchProgress);
        modal.AddControl(filterToolbar);
        modal.AddControl(resultsList);
        modal.AddControl(filterLabel);
        modal.AddControl(separator2);
        modal.AddControl(buttonToolbar);

        // ── Debounced search on text change ─────────────────
        searchInput.InputChanged += (sender, e) =>
        {
            var query = searchInput.Input?.Trim() ?? "";

            // Cancel any pending search
            searchCts?.Cancel();
            searchCts = new CancellationTokenSource();
            var ct = searchCts.Token;

            if (query.Length < 2)
            {
                resultsList.ClearItems();
                allResults.Clear();
                currentResults.Clear();
                searchProgress.Visible = false;
                statusLabel.SetContent(new List<string>
                {
                    query.Length == 0
                        ? $"[{ColorScheme.MutedMarkup}]Type to search[/]"
                        : $"[{ColorScheme.MutedMarkup}]Type at least 2 characters[/]"
                });
                if (installButton != null) installButton.IsEnabled = false;
                return;
            }

            searchProgress.Visible = true;
            statusLabel.SetContent(new List<string> { $"[{ColorScheme.MutedMarkup}]Searching...[/]" });

            _ = Task.Run(async () =>
            {
                try
                {
                    // Debounce: wait 400ms before searching
                    await Task.Delay(400, ct);
                    if (ct.IsCancellationRequested) return;

                    var results = await nugetService.SearchPackagesAsync(query, 15, ct);
                    if (ct.IsCancellationRequested) return;

                    allResults = results;

                    searchProgress.Visible = false;

                    if (!results.Any())
                    {
                        statusLabel.SetContent(new List<string> { $"[{ColorScheme.MutedMarkup}]No results found[/]" });
                        resultsList.ClearItems();
                        currentResults.Clear();
                        if (installButton != null) installButton.IsEnabled = false;
                        return;
                    }

                    RefreshResultsList();
                }
                catch (OperationCanceledException)
                {
                    // Search was cancelled, ignore
                }
                catch
                {
                    searchProgress.Visible = false;
                    statusLabel.SetContent(new List<string> { $"[{ColorScheme.ErrorMarkup}]Search failed[/]" });
                }
            });
        };

        // ── Enter on results list to select ─────────────────
        resultsList.ItemActivated += (sender, item) =>
        {
            if (item?.Tag is NuGetPackage pkg)
            {
                selectedPackage = pkg;
                modal.Close();
            }
        };

        // ── Keyboard handling ───────────────────────────────
        modal.KeyPressed += (sender, e) =>
        {
            // Redirect Up/Down to results list when it doesn't have focus (e.g. search
            // input is focused). Must run BEFORE AlreadyHandled — the dispatcher
            // consumes arrows for window scrolling. When the list HAS focus, its own
            // ProcessKey already handled the move — don't double-move.
            if (e.KeyInfo.Key == ConsoleKey.UpArrow && !resultsList.HasFocus && resultsList.Items.Count > 0)
            {
                if (resultsList.SelectedIndex > 0)
                    resultsList.SelectedIndex--;
                e.Handled = true;
                return;
            }
            if (e.KeyInfo.Key == ConsoleKey.DownArrow && !resultsList.HasFocus && resultsList.Items.Count > 0)
            {
                if (resultsList.SelectedIndex < resultsList.Items.Count - 1)
                    resultsList.SelectedIndex++;
                e.Handled = true;
                return;
            }

            // Escape must run BEFORE AlreadyHandled — dispatcher consumes
            // the first Escape to unfocus the search input control.
            if (e.KeyInfo.Key == ConsoleKey.Escape)
            {
                selectedPackage = null;
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
                RefreshResultsList();
                e.Handled = true;
            }
            else if (e.KeyInfo.Key == ConsoleKey.F2)
            {
                filterType = FilterVersionType.StableOnly;
                if (versionTypeDropdown != null) versionTypeDropdown.SelectedIndex = 1;
                RefreshResultsList();
                e.Handled = true;
            }
            else if (e.KeyInfo.Key == ConsoleKey.F3)
            {
                filterType = FilterVersionType.PreReleaseOnly;
                if (versionTypeDropdown != null) versionTypeDropdown.SelectedIndex = 2;
                RefreshResultsList();
                e.Handled = true;
            }
            // Action shortcuts
            else if (e.KeyInfo.Key == ConsoleKey.I && installButton != null && installButton.IsEnabled)
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
                if (resultsList.SelectedItem?.Tag is NuGetPackage pkg)
                {
                    selectedPackage = pkg;
                    modal.Close();
                    e.Handled = true;
                }
            }
            else if (e.KeyInfo.Key == ConsoleKey.Tab)
            {
                // Tab cycles through: search input → dropdown → results → buttons
                // The default tab handling should work with the new controls
                // No custom handling needed - let the framework handle it
            }
        };

        modal.OnClosed += (s, e) =>
        {
            searchCts?.Cancel();
            tcs.TrySetResult(selectedPackage);
        };

        windowSystem.AddWindow(modal);
        windowSystem.SetActiveWindow(modal);
        searchInput.SetFocus(true, FocusReason.Programmatic);

        return tcs.Task;
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
