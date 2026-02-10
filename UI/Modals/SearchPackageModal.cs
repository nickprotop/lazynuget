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
        ListControl? resultsList = null;
        MarkupControl? statusRight = null;
        ProgressBarControl? searchProgress = null;
        var currentResults = new List<NuGetPackage>();

        var modal = new WindowBuilder(windowSystem)
            .WithTitle("Search NuGet.org")
            .Centered()
            .WithSize(65, 22)
            .AsModal()
            .WithBorderStyle(BorderStyle.Single)
            .WithBorderColor(Color.Grey35)
            .Resizable(true)
            .Movable(true)
            .Minimizable(false)
            .WithColors(ColorScheme.StatusBarBackground, Color.Grey93)
            .Build();

        // ── Search input + status ────────────────────────────
        var searchInput = Controls.Prompt("Search: ")
            .WithInput("")
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithMargin(2, 0, 2, 0)
            .StickyTop()
            .Build();
        modal.AddControl(searchInput);

        statusRight = Controls.Markup($"[{ColorScheme.MutedMarkup}]Type to search[/]")
            .WithAlignment(HorizontalAlignment.Right)
            .WithMargin(0, 0, 2, 0)
            .StickyTop()
            .Build();
        modal.AddControl(statusRight);

        // ── Search progress (indeterminate, hidden by default) ──
        searchProgress = Controls.ProgressBar()
            .Indeterminate(true)
            .WithMargin(2, 0, 2, 0)
            .StickyTop()
            .Build();
        searchProgress.Visible = false;
        modal.AddControl(searchProgress);

        // ── Results list ────────────────────────────────────
        resultsList = Controls.List()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithColors(ColorScheme.StatusBarBackground, Color.Grey93)
            .WithFocusedColors(ColorScheme.StatusBarBackground, Color.Grey93)
            .WithHighlightColors(Color.Grey35, Color.White)
            .SimpleMode()
            .Build();
        modal.AddControl(resultsList);

        // ── Bottom bar ──────────────────────────────────────
        modal.AddControl(Controls.RuleBuilder()
            .StickyBottom()
            .WithColor(ColorScheme.RuleColor)
            .Build());

        modal.AddControl(Controls.Markup()
            .AddLine($"[{ColorScheme.MutedMarkup}]↑↓:Navigate  Enter:Select  Esc:Cancel[/]")
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .Build());

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
                currentResults.Clear();
                searchProgress.Visible = false;
                statusRight.SetContent(new List<string>
                {
                    query.Length == 0
                        ? $"[{ColorScheme.MutedMarkup}]Type to search[/]"
                        : $"[{ColorScheme.MutedMarkup}]Type at least 2 characters[/]"
                });
                return;
            }

            searchProgress.Visible = true;
            statusRight.SetContent(new List<string> { $"[{ColorScheme.MutedMarkup}]Searching...[/]" });

            _ = Task.Run(async () =>
            {
                try
                {
                    // Debounce: wait 400ms before searching
                    await Task.Delay(400, ct);
                    if (ct.IsCancellationRequested) return;

                    var results = await nugetService.SearchPackagesAsync(query, 15, ct);
                    if (ct.IsCancellationRequested) return;

                    currentResults = results;

                    resultsList.ClearItems();
                    foreach (var pkg in results)
                    {
                        var downloads = FormatDownloads(pkg.TotalDownloads);
                        var displayText = $"[cyan1]{Markup.Escape(pkg.Id)}[/]\n" +
                                        $"[grey70]  {pkg.Version}  ·  {downloads} downloads[/]";
                        resultsList.AddItem(new ListItem(displayText) { Tag = pkg });
                    }

                    searchProgress.Visible = false;
                    statusRight.SetContent(new List<string>
                    {
                        results.Count > 0
                            ? $"[{ColorScheme.SecondaryMarkup}]{results.Count} results[/]"
                            : $"[{ColorScheme.MutedMarkup}]No results found[/]"
                    });
                }
                catch (OperationCanceledException)
                {
                    // Search was cancelled, ignore
                }
                catch
                {
                    searchProgress.Visible = false;
                    statusRight.SetContent(new List<string> { $"[{ColorScheme.ErrorMarkup}]Search failed[/]" });
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

            if (e.KeyInfo.Key == ConsoleKey.Enter)
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
                // Toggle focus between search input and results
                if (searchInput.HasFocus)
                {
                    resultsList.SetFocus(true, FocusReason.Programmatic);
                }
                else
                {
                    searchInput.SetFocus(true, FocusReason.Programmatic);
                }
                e.Handled = true;
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
