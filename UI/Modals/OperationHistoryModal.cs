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
/// Modal for viewing and managing operation history
/// </summary>
public static class OperationHistoryModal
{
    private enum FilterType
    {
        All,
        Restore,
        Update,
        Add,
        Remove
    }

    private enum FilterStatus
    {
        All,
        Success,
        Failed
    }

    public static Task ShowAsync(
        ConsoleWindowSystem windowSystem,
        OperationHistoryService historyService,
        DotNetCliService cliService,
        Window? parentWindow = null)
    {
        var tcs = new TaskCompletionSource<bool>();

        // Filter state
        var filterType = FilterType.All;
        var filterStatus = FilterStatus.All;

        // Controls
        ListControl? historyList = null;
        MarkupControl? statusLabel = null;
        MarkupControl? filterLabel = null;
        ButtonControl? retryButton = null;
        ButtonControl? clearButton = null;
        DropdownControl? typeDropdown = null;
        DropdownControl? statusDropdown = null;

        // Build modal
        var modal = new WindowBuilder(windowSystem)
            .WithTitle("Operation History")
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

        // Helper to refresh the list based on current filters
        void RefreshHistoryList()
        {
            var allHistory = historyService.GetHistory();

            // Apply type filter
            var filtered = filterType switch
            {
                FilterType.Restore => allHistory.Where(e => e.Type == OperationType.Restore),
                FilterType.Update => allHistory.Where(e => e.Type == OperationType.Update),
                FilterType.Add => allHistory.Where(e => e.Type == OperationType.Add),
                FilterType.Remove => allHistory.Where(e => e.Type == OperationType.Remove),
                _ => allHistory
            };

            // Apply status filter
            filtered = filterStatus switch
            {
                FilterStatus.Success => filtered.Where(e => e.Success),
                FilterStatus.Failed => filtered.Where(e => !e.Success),
                _ => filtered
            };

            var items = filtered.ToList();

            // Update status label
            var typeText = filterType == FilterType.All ? "All" : filterType.ToString();
            var statusText = filterStatus == FilterStatus.All ? "All" : filterStatus.ToString();
            statusLabel?.SetContent(new List<string> {
                $"[{ColorScheme.SecondaryMarkup}]Showing:[/] [{ColorScheme.PrimaryMarkup}]{typeText}[/] [{ColorScheme.SecondaryMarkup}]operations with status:[/] [{ColorScheme.PrimaryMarkup}]{statusText}[/] [{ColorScheme.MutedMarkup}]({items.Count} total)[/]"
            });

            // Update filter help
            filterLabel?.SetContent(new List<string> {
                $"[{ColorScheme.MutedMarkup}]F1:All  F2:Restore  F3:Update  F4:Add  F5:Remove  |  F6:All Status  F7:Success  F8:Failed[/]"
            });

            // Populate list
            historyList?.ClearItems();

            if (!items.Any())
            {
                var emptyItem = new ListItem($"[{ColorScheme.MutedMarkup}]No operations found matching current filters[/]");
                emptyItem.Tag = null;
                historyList?.AddItem(emptyItem);
                retryButton!.IsEnabled = false;
                return;
            }

            foreach (var entry in items)
            {
                var icon = entry.Type switch
                {
                    OperationType.Restore => "🔄",
                    OperationType.Update => "⬆",
                    OperationType.Add => "➕",
                    OperationType.Remove => "➖",
                    _ => "•"
                };

                var statusIcon = entry.Success ? "✓" : "✗";
                var statusColor = entry.Success ? ColorScheme.SuccessMarkup : ColorScheme.ErrorMarkup;

                var timeAgo = GetTimeAgo(entry.Timestamp);
                var durationText = $"{entry.Duration.TotalSeconds:F1}s";

                // Highlight batch operations (when PackageId is null and description contains package count)
                var isBatchOperation = entry.PackageId == null &&
                    (entry.Description.Contains("packages in") || entry.Description.Contains("packages"));

                var batchIndicator = isBatchOperation ? "📦 " : "";
                var text = $"[{statusColor}]{statusIcon}[/] {icon} {batchIndicator}[{ColorScheme.PrimaryMarkup}]{Markup.Escape(entry.Description)}[/] [{ColorScheme.MutedMarkup}]({durationText}) - {timeAgo}[/]";

                if (!entry.Success && !string.IsNullOrEmpty(entry.ErrorMessage))
                {
                    text += $"\n    [{ColorScheme.ErrorMarkup}]{Markup.Escape(entry.ErrorMessage)}[/]";
                }

                var listItem = new ListItem(text);
                listItem.Tag = entry;
                historyList?.AddItem(listItem);
            }

            // Enable/disable retry button based on selection
            var selectedEntry = historyList?.SelectedItem?.Tag as OperationHistoryEntry;
            retryButton!.IsEnabled = selectedEntry != null && !selectedEntry.Success;
        }

        // Header
        var header = Controls.Markup()
            .AddLine($"[{ColorScheme.PrimaryMarkup} bold]Operation History[/]")
            .AddLine($"[{ColorScheme.SecondaryMarkup}]View and retry past NuGet operations[/]")
            .WithMargin(2, 2, 2, 0)
            .Build();

        // Filter status label
        statusLabel = Controls.Markup()
            .AddLine($"[{ColorScheme.SecondaryMarkup}]Loading...[/]")
            .WithMargin(2, 1, 2, 0)
            .Build();

        // Separator
        var separator1 = Controls.RuleBuilder()
            .WithColor(ColorScheme.RuleColor)
            .Build();
        separator1.Margin = new Margin(2, 1, 2, 0);

        // Dropdown filters for mouse users
        typeDropdown = new DropdownControl(string.Empty, new[]
        {
            "All (F1)",
            "Restore (F2)",
            "Update (F3)",
            "Add (F4)",
            "Remove (F5)"
        })
        {
            SelectedIndex = 0
        };

        typeDropdown.SelectedIndexChanged += (s, idx) =>
        {
            filterType = idx switch
            {
                0 => FilterType.All,
                1 => FilterType.Restore,
                2 => FilterType.Update,
                3 => FilterType.Add,
                4 => FilterType.Remove,
                _ => FilterType.All
            };
            RefreshHistoryList();
        };

        statusDropdown = new DropdownControl(string.Empty, new[]
        {
            "All (F6)",
            "Success (F7)",
            "Failed (F8)"
        })
        {
            SelectedIndex = 0
        };

        statusDropdown.SelectedIndexChanged += (s, idx) =>
        {
            filterStatus = idx switch
            {
                0 => FilterStatus.All,
                1 => FilterStatus.Success,
                2 => FilterStatus.Failed,
                _ => FilterStatus.All
            };
            RefreshHistoryList();
        };

        // Toolbar containing both dropdowns
        var filterToolbar = Controls.Toolbar()
            .Add(typeDropdown)
            .Add(statusDropdown)
            .WithSpacing(2)
            .WithMargin(2, 0, 2, 1)
            .Build();

        // History list (simple mode - no selection markers)
        historyList = Controls.List()
            .SimpleMode()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithColors(ColorScheme.StatusBarBackground, Color.Grey93)
            .WithFocusedColors(ColorScheme.StatusBarBackground, Color.Grey93)
            .WithHighlightColors(Color.Grey35, Color.White)
            .WithMargin(2, 0, 2, 1)
            .Build();

        historyList.SelectedIndexChanged += (s, idx) =>
        {
            var selectedEntry = historyList.SelectedItem?.Tag as OperationHistoryEntry;
            retryButton!.IsEnabled = selectedEntry != null && !selectedEntry.Success;
        };

        // Filter label
        filterLabel = Controls.Markup()
            .AddLine($"[{ColorScheme.MutedMarkup}]Use dropdown filters above or press F1-F8 for quick filtering[/]")
            .WithMargin(2, 1, 2, 0)
            .StickyBottom()
            .Build();

        // Separator
        var separator2 = Controls.RuleBuilder()
            .WithColor(ColorScheme.RuleColor)
            .StickyBottom()
            .Build();
        separator2.Margin = new Margin(2, 0, 2, 0);

        // Buttons
        retryButton = Controls.Button("[grey93]Retry Failed (R)[/]")
            .WithMargin(1, 0, 0, 0)
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.DarkOrange)
            .WithFocusedForegroundColor(Color.White)
            .Build();

        retryButton.IsEnabled = false;

        async Task HandleRetryAsync()
        {
            var selectedEntry = historyList.SelectedItem?.Tag as OperationHistoryEntry;
            if (selectedEntry == null || selectedEntry.Success) return;

            // Close history modal
            modal.Close();

            // Wait a bit for modal to close
            await Task.Delay(100);

            // Retry the operation
            await RetryOperation(windowSystem, cliService, historyService, selectedEntry, parentWindow);

            // Reopen history modal
            await ShowAsync(windowSystem, historyService, cliService, parentWindow);
        }

        retryButton.Click += async (s, e) => await HandleRetryAsync();

        clearButton = Controls.Button("[grey93]Clear History (C)[/]")
            .WithMargin(1, 0, 0, 0)
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Red)
            .WithFocusedForegroundColor(Color.White)
            .Build();

        async Task HandleClearAsync()
        {
            var confirm = await ConfirmationModal.ShowAsync(
                windowSystem,
                "Clear History",
                "Are you sure you want to clear all operation history?",
                "Clear",
                "Cancel",
                modal);

            if (confirm)
            {
                historyService.ClearHistory();
                RefreshHistoryList();
            }
        }

        clearButton.Click += async (s, e) => await HandleClearAsync();

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
            .Column(col => col.Add(retryButton))
            .Column(col => col.Width(2))
            .Column(col => col.Add(clearButton))
            .Column(col => col.Width(2))
            .Column(col => col.Add(closeButton))
            .Build();
        buttonGrid.Margin = new Margin(0, 0, 0, 0);

        // Assemble modal
        modal.AddControl(header);
        modal.AddControl(statusLabel);
        modal.AddControl(separator1);
        modal.AddControl(filterToolbar);
        modal.AddControl(historyList);
        modal.AddControl(filterLabel);
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
            else if (e.KeyInfo.Key == ConsoleKey.R && retryButton.IsEnabled)
            {
                _ = HandleRetryAsync();
                e.Handled = true;
            }
            else if (e.KeyInfo.Key == ConsoleKey.C)
            {
                _ = HandleClearAsync();
                e.Handled = true;
            }
            // Filter by type
            else if (e.KeyInfo.Key == ConsoleKey.F1)
            {
                filterType = FilterType.All;
                if (typeDropdown != null) typeDropdown.SelectedIndex = 0;
                RefreshHistoryList();
                e.Handled = true;
            }
            else if (e.KeyInfo.Key == ConsoleKey.F2)
            {
                filterType = FilterType.Restore;
                if (typeDropdown != null) typeDropdown.SelectedIndex = 1;
                RefreshHistoryList();
                e.Handled = true;
            }
            else if (e.KeyInfo.Key == ConsoleKey.F3)
            {
                filterType = FilterType.Update;
                if (typeDropdown != null) typeDropdown.SelectedIndex = 2;
                RefreshHistoryList();
                e.Handled = true;
            }
            else if (e.KeyInfo.Key == ConsoleKey.F4)
            {
                filterType = FilterType.Add;
                if (typeDropdown != null) typeDropdown.SelectedIndex = 3;
                RefreshHistoryList();
                e.Handled = true;
            }
            else if (e.KeyInfo.Key == ConsoleKey.F5)
            {
                filterType = FilterType.Remove;
                if (typeDropdown != null) typeDropdown.SelectedIndex = 4;
                RefreshHistoryList();
                e.Handled = true;
            }
            // Filter by status
            else if (e.KeyInfo.Key == ConsoleKey.F6)
            {
                filterStatus = FilterStatus.All;
                if (statusDropdown != null) statusDropdown.SelectedIndex = 0;
                RefreshHistoryList();
                e.Handled = true;
            }
            else if (e.KeyInfo.Key == ConsoleKey.F7)
            {
                filterStatus = FilterStatus.Success;
                if (statusDropdown != null) statusDropdown.SelectedIndex = 1;
                RefreshHistoryList();
                e.Handled = true;
            }
            else if (e.KeyInfo.Key == ConsoleKey.F8)
            {
                filterStatus = FilterStatus.Failed;
                if (statusDropdown != null) statusDropdown.SelectedIndex = 2;
                RefreshHistoryList();
                e.Handled = true;
            }
        };

        modal.OnClosed += (s, e) => tcs.TrySetResult(true);

        // Show modal
        windowSystem.AddWindow(modal);
        windowSystem.SetActiveWindow(modal);

        // Initial load
        RefreshHistoryList();

        return tcs.Task;
    }

    private static async Task RetryOperation(
        ConsoleWindowSystem windowSystem,
        DotNetCliService cliService,
        OperationHistoryService historyService,
        OperationHistoryEntry entry,
        Window? parentWindow)
    {
        var result = await OperationProgressModal.ShowAsync(
            windowSystem,
            entry.Type,
            (ct, progress) => entry.Type switch
            {
                OperationType.Restore => cliService.RestorePackagesAsync(entry.ProjectPath, ct, progress),
                OperationType.Update => cliService.UpdatePackageAsync(
                    entry.ProjectPath, entry.PackageId!, entry.PackageVersion, ct, progress),
                OperationType.Add => cliService.AddPackageAsync(
                    entry.ProjectPath, entry.PackageId!, entry.PackageVersion, ct, progress),
                OperationType.Remove => cliService.RemovePackageAsync(
                    entry.ProjectPath, entry.PackageId!, ct, progress),
                _ => throw new InvalidOperationException($"Unknown operation type: {entry.Type}")
            },
            $"Retrying {entry.Type}",
            $"Retrying: {entry.Description}",
            historyService,
            entry.ProjectPath,
            entry.ProjectName,
            entry.PackageId,
            entry.PackageVersion,
            parentWindow);

        // Show result notification
        if (result.Success)
        {
            windowSystem.NotificationStateService.ShowNotification(
                "Retry Successful",
                $"Successfully retried: {entry.Description}",
                NotificationSeverity.Success,
                timeout: 3000,
                parentWindow: parentWindow);
        }
    }

    private static string GetTimeAgo(DateTime timestamp)
    {
        var elapsed = DateTime.Now - timestamp;

        if (elapsed.TotalSeconds < 60)
            return $"{(int)elapsed.TotalSeconds}s ago";
        if (elapsed.TotalMinutes < 60)
            return $"{(int)elapsed.TotalMinutes}m ago";
        if (elapsed.TotalHours < 24)
            return $"{(int)elapsed.TotalHours}h ago";
        if (elapsed.TotalDays < 7)
            return $"{(int)elapsed.TotalDays}d ago";

        return timestamp.ToString("MMM dd, HH:mm");
    }
}
