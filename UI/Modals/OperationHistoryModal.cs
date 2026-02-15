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
/// Modal for viewing and managing operation history
/// </summary>
public class OperationHistoryModal : ModalBase<bool>
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

    private readonly OperationHistoryService _historyService;
    private readonly DotNetCliService _cliService;
    private readonly ConsoleWindowSystem _windowSystem;
    private readonly Window? _parentWindow;

    // Filter state
    private FilterType _filterType = FilterType.All;
    private FilterStatus _filterStatus = FilterStatus.All;

    // Controls
    private ListControl? _historyList;
    private MarkupControl? _statusLabel;
    private MarkupControl? _filterLabel;
    private ButtonControl? _retryButton;
    private ButtonControl? _rollbackButton;
    private ButtonControl? _clearButton;
    private DropdownControl? _typeDropdown;
    private DropdownControl? _statusDropdown;

    // Event handler references for cleanup
    private EventHandler<int>? _typeDropdownHandler;
    private EventHandler<int>? _statusDropdownHandler;
    private EventHandler<int>? _historyListHandler;
    private EventHandler<ButtonControl>? _retryButtonHandler;
    private EventHandler<ButtonControl>? _rollbackButtonHandler;
    private EventHandler<ButtonControl>? _clearButtonHandler;
    private EventHandler<ButtonControl>? _closeButtonHandler;

    private OperationHistoryModal(
        ConsoleWindowSystem windowSystem,
        OperationHistoryService historyService,
        DotNetCliService cliService,
        Window? parentWindow)
    {
        _windowSystem = windowSystem;
        _historyService = historyService;
        _cliService = cliService;
        _parentWindow = parentWindow;
    }

    public static Task<bool> ShowAsync(
        ConsoleWindowSystem windowSystem,
        OperationHistoryService historyService,
        DotNetCliService cliService,
        Window? parentWindow = null)
    {
        var instance = new OperationHistoryModal(windowSystem, historyService, cliService, parentWindow);
        return instance.ShowAsync(windowSystem, parentWindow);
    }

    protected override string GetTitle() => "Operation History";

    protected override (int width, int height) GetSize() => (100, 30);

    protected override BorderStyle GetBorderStyle() => BorderStyle.DoubleLine;

    protected override Color GetBorderColor() => ColorScheme.BorderColor;

    protected override bool GetDefaultResult() => false;

    protected override void BuildContent()
    {
        // Header
        var header = Controls.Markup()
            .AddLine($"[{ColorScheme.PrimaryMarkup} bold]Operation History[/]")
            .AddLine($"[{ColorScheme.SecondaryMarkup}]View and retry past NuGet operations[/]")
            .WithMargin(2, 2, 2, 0)
            .Build();

        // Filter status label
        _statusLabel = Controls.Markup()
            .AddLine($"[{ColorScheme.SecondaryMarkup}]Loading...[/]")
            .WithMargin(2, 1, 2, 0)
            .Build();

        // Separator
        var separator1 = Controls.RuleBuilder()
            .WithColor(ColorScheme.RuleColor)
            .Build();
        separator1.Margin = new Margin(2, 1, 2, 0);

        // Dropdown filters for mouse users
        _typeDropdown = new DropdownControl(string.Empty, new[]
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

        _typeDropdownHandler = (s, idx) =>
        {
            _filterType = idx switch
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
        _typeDropdown.SelectedIndexChanged += _typeDropdownHandler;

        _statusDropdown = new DropdownControl(string.Empty, new[]
        {
            "All (F6)",
            "Success (F7)",
            "Failed (F8)"
        })
        {
            SelectedIndex = 0
        };

        _statusDropdownHandler = (s, idx) =>
        {
            _filterStatus = idx switch
            {
                0 => FilterStatus.All,
                1 => FilterStatus.Success,
                2 => FilterStatus.Failed,
                _ => FilterStatus.All
            };
            RefreshHistoryList();
        };
        _statusDropdown.SelectedIndexChanged += _statusDropdownHandler;

        // Toolbar containing both dropdowns
        var filterToolbar = Controls.Toolbar()
            .Add(_typeDropdown)
            .Add(_statusDropdown)
            .WithSpacing(2)
            .WithMargin(2, 0, 2, 1)
            .Build();

        // History list (simple mode - no selection markers)
        _historyList = Controls.List()
            .SimpleMode()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithColors(ColorScheme.StatusBarBackground, Color.Grey93)
            .WithFocusedColors(ColorScheme.StatusBarBackground, Color.Grey93)
            .WithHighlightColors(Color.Grey35, Color.White)
            .WithMargin(2, 0, 2, 1)
            .Build();

        _historyListHandler = (s, idx) =>
        {
            var selectedEntry = _historyList?.SelectedItem?.Tag as OperationHistoryEntry;
            _retryButton!.IsEnabled = selectedEntry != null && !selectedEntry.Success;
            _rollbackButton!.IsEnabled = selectedEntry != null &&
                                         selectedEntry.Success &&
                                         (selectedEntry.Type == OperationType.Add || selectedEntry.Type == OperationType.Remove) &&
                                         !string.IsNullOrEmpty(selectedEntry.PackageId);
        };
        _historyList.SelectedIndexChanged += _historyListHandler;

        // Filter label
        _filterLabel = Controls.Markup()
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
        _retryButton = Controls.Button("[grey93]Retry Failed (R)[/]")
            .WithMargin(1, 0, 0, 0)
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.DarkOrange)
            .WithFocusedForegroundColor(Color.White)
            .Build();

        _retryButton.IsEnabled = false;

        _retryButtonHandler = async (s, e) => await HandleRetryAsync();
        _retryButton.Click += _retryButtonHandler;

        _rollbackButton = Controls.Button("[grey93]Rollback (R)[/]")
            .WithMargin(1, 0, 0, 0)
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.DarkOrange)
            .WithFocusedForegroundColor(Color.White)
            .Build();

        _rollbackButton.IsEnabled = false;

        _rollbackButtonHandler = async (s, e) => await HandleRollbackAsync();
        _rollbackButton.Click += _rollbackButtonHandler;

        _clearButton = Controls.Button("[grey93]Clear History (C)[/]")
            .WithMargin(1, 0, 0, 0)
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Red)
            .WithFocusedForegroundColor(Color.White)
            .Build();

        _clearButtonHandler = async (s, e) => await HandleClearAsync();
        _clearButton.Click += _clearButtonHandler;

        var closeButton = Controls.Button("[grey93]Close (Esc)[/]")
            .WithMargin(1, 0, 0, 0)
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .Build();

        _closeButtonHandler = (s, e) => CloseWithResult(false);
        closeButton.Click += _closeButtonHandler;

        var buttonGrid = Controls.HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .Column(col => col.Add(_retryButton))
            .Column(col => col.Width(2))
            .Column(col => col.Add(_rollbackButton))
            .Column(col => col.Width(2))
            .Column(col => col.Add(_clearButton))
            .Column(col => col.Width(2))
            .Column(col => col.Add(closeButton))
            .Build();
        buttonGrid.Margin = new Margin(0, 0, 0, 0);

        // Assemble modal
        Modal.AddControl(header);
        Modal.AddControl(_statusLabel);
        Modal.AddControl(separator1);
        Modal.AddControl(filterToolbar);
        Modal.AddControl(_historyList);
        Modal.AddControl(_filterLabel);
        Modal.AddControl(separator2);
        Modal.AddControl(buttonGrid);

        // Initial load
        RefreshHistoryList();
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.R)
        {
            // Prioritize rollback over retry (only one will be enabled at a time)
            if (_rollbackButton?.IsEnabled == true)
            {
                AsyncHelper.FireAndForget(() => HandleRollbackAsync());
                e.Handled = true;
            }
            else if (_retryButton?.IsEnabled == true)
            {
                AsyncHelper.FireAndForget(() => HandleRetryAsync());
                e.Handled = true;
            }
        }
        else if (e.KeyInfo.Key == ConsoleKey.C)
        {
            AsyncHelper.FireAndForget(() => HandleClearAsync());
            e.Handled = true;
        }
        // Filter by type
        else if (e.KeyInfo.Key == ConsoleKey.F1)
        {
            _filterType = FilterType.All;
            if (_typeDropdown != null) _typeDropdown.SelectedIndex = 0;
            RefreshHistoryList();
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.F2)
        {
            _filterType = FilterType.Restore;
            if (_typeDropdown != null) _typeDropdown.SelectedIndex = 1;
            RefreshHistoryList();
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.F3)
        {
            _filterType = FilterType.Update;
            if (_typeDropdown != null) _typeDropdown.SelectedIndex = 2;
            RefreshHistoryList();
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.F4)
        {
            _filterType = FilterType.Add;
            if (_typeDropdown != null) _typeDropdown.SelectedIndex = 3;
            RefreshHistoryList();
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.F5)
        {
            _filterType = FilterType.Remove;
            if (_typeDropdown != null) _typeDropdown.SelectedIndex = 4;
            RefreshHistoryList();
            e.Handled = true;
        }
        // Filter by status
        else if (e.KeyInfo.Key == ConsoleKey.F6)
        {
            _filterStatus = FilterStatus.All;
            if (_statusDropdown != null) _statusDropdown.SelectedIndex = 0;
            RefreshHistoryList();
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.F7)
        {
            _filterStatus = FilterStatus.Success;
            if (_statusDropdown != null) _statusDropdown.SelectedIndex = 1;
            RefreshHistoryList();
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.F8)
        {
            _filterStatus = FilterStatus.Failed;
            if (_statusDropdown != null) _statusDropdown.SelectedIndex = 2;
            RefreshHistoryList();
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
        if (_typeDropdown != null && _typeDropdownHandler != null)
            _typeDropdown.SelectedIndexChanged -= _typeDropdownHandler;

        if (_statusDropdown != null && _statusDropdownHandler != null)
            _statusDropdown.SelectedIndexChanged -= _statusDropdownHandler;

        if (_historyList != null && _historyListHandler != null)
            _historyList.SelectedIndexChanged -= _historyListHandler;

        if (_retryButton != null && _retryButtonHandler != null)
            _retryButton.Click -= _retryButtonHandler;

        if (_rollbackButton != null && _rollbackButtonHandler != null)
            _rollbackButton.Click -= _rollbackButtonHandler;

        if (_clearButton != null && _clearButtonHandler != null)
            _clearButton.Click -= _clearButtonHandler;
    }

    private void RefreshHistoryList()
    {
        var allHistory = _historyService.GetHistory();

        // Apply type filter
        var filtered = _filterType switch
        {
            FilterType.Restore => allHistory.Where(e => e.Type == OperationType.Restore),
            FilterType.Update => allHistory.Where(e => e.Type == OperationType.Update),
            FilterType.Add => allHistory.Where(e => e.Type == OperationType.Add),
            FilterType.Remove => allHistory.Where(e => e.Type == OperationType.Remove),
            _ => allHistory
        };

        // Apply status filter
        filtered = _filterStatus switch
        {
            FilterStatus.Success => filtered.Where(e => e.Success),
            FilterStatus.Failed => filtered.Where(e => !e.Success),
            _ => filtered
        };

        var items = filtered.ToList();

        // Update status label
        var typeText = _filterType == FilterType.All ? "All" : _filterType.ToString();
        var statusText = _filterStatus == FilterStatus.All ? "All" : _filterStatus.ToString();
        _statusLabel?.SetContent(new List<string> {
            $"[{ColorScheme.SecondaryMarkup}]Showing:[/] [{ColorScheme.PrimaryMarkup}]{typeText}[/] [{ColorScheme.SecondaryMarkup}]operations with status:[/] [{ColorScheme.PrimaryMarkup}]{statusText}[/] [{ColorScheme.MutedMarkup}]({items.Count} total)[/]"
        });

        // Update filter help
        _filterLabel?.SetContent(new List<string> {
            $"[{ColorScheme.MutedMarkup}]F1:All  F2:Restore  F3:Update  F4:Add  F5:Remove  |  F6:All Status  F7:Success  F8:Failed[/]"
        });

        // Populate list
        _historyList?.ClearItems();

        if (!items.Any())
        {
            var emptyItem = new ListItem($"[{ColorScheme.MutedMarkup}]No operations found matching current filters[/]");
            emptyItem.Tag = null;
            _historyList?.AddItem(emptyItem);
            _retryButton!.IsEnabled = false;
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
            _historyList?.AddItem(listItem);
        }

        // Enable/disable retry and rollback buttons based on selection
        var selectedEntry = _historyList?.SelectedItem?.Tag as OperationHistoryEntry;
        _retryButton!.IsEnabled = selectedEntry != null && !selectedEntry.Success;
        _rollbackButton!.IsEnabled = selectedEntry != null &&
                                     selectedEntry.Success &&
                                     (selectedEntry.Type == OperationType.Add || selectedEntry.Type == OperationType.Remove) &&
                                     !string.IsNullOrEmpty(selectedEntry.PackageId);
    }

    private async Task HandleRetryAsync()
    {
        var selectedEntry = _historyList?.SelectedItem?.Tag as OperationHistoryEntry;
        if (selectedEntry == null || selectedEntry.Success) return;

        // Close history modal
        CloseWithResult(false);

        // Wait a bit for modal to close
        await Task.Delay(100);

        // Retry the operation
        await RetryOperation(_windowSystem, _cliService, _historyService, selectedEntry, _parentWindow);

        // Reopen history modal
        await ShowAsync(_windowSystem, _historyService, _cliService, _parentWindow);
    }

    private async Task HandleRollbackAsync()
    {
        var selectedEntry = _historyList?.SelectedItem?.Tag as OperationHistoryEntry;
        if (selectedEntry == null || !selectedEntry.Success) return;
        if (selectedEntry.Type != OperationType.Add && selectedEntry.Type != OperationType.Remove) return;
        if (string.IsNullOrEmpty(selectedEntry.PackageId)) return;

        // Determine the reverse operation
        var rollbackType = selectedEntry.Type == OperationType.Add ? OperationType.Remove : OperationType.Add;
        var operationName = rollbackType == OperationType.Remove ? "Uninstall" : "Install";

        // Confirm rollback
        var confirm = await ConfirmationModal.ShowAsync(
            _windowSystem,
            "Rollback Operation",
            $"{operationName} {selectedEntry.PackageId} to reverse {selectedEntry.Type} operation?",
            "Rollback",
            "Cancel",
            Modal);

        if (!confirm) return;

        // Close history modal
        CloseWithResult(false);

        // Wait a bit for modal to close
        await Task.Delay(100);

        // Perform rollback operation
        await RollbackOperation(_windowSystem, _cliService, _historyService, selectedEntry, _parentWindow);

        // Reopen history modal
        await ShowAsync(_windowSystem, _historyService, _cliService, _parentWindow);
    }

    private async Task HandleClearAsync()
    {
        var confirm = await ConfirmationModal.ShowAsync(
            _windowSystem,
            "Clear History",
            "Are you sure you want to clear all operation history?",
            "Clear",
            "Cancel",
            Modal);

        if (confirm)
        {
            _historyService.ClearHistory();
            RefreshHistoryList();
        }
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

    private static async Task RollbackOperation(
        ConsoleWindowSystem windowSystem,
        DotNetCliService cliService,
        OperationHistoryService historyService,
        OperationHistoryEntry entry,
        Window? parentWindow)
    {
        // Determine the reverse operation
        var rollbackType = entry.Type == OperationType.Add ? OperationType.Remove : OperationType.Add;
        var operationName = rollbackType == OperationType.Remove ? "Removing" : "Adding";

        var result = await OperationProgressModal.ShowAsync(
            windowSystem,
            rollbackType,
            (ct, progress) => entry.Type switch
            {
                OperationType.Add => cliService.RemovePackageAsync(
                    entry.ProjectPath, entry.PackageId!, ct, progress),
                OperationType.Remove => cliService.AddPackageAsync(
                    entry.ProjectPath, entry.PackageId!, entry.PackageVersion, ct, progress),
                _ => throw new InvalidOperationException($"Cannot rollback operation type: {entry.Type}")
            },
            $"Rolling Back {entry.Type}",
            $"{operationName} {entry.PackageId} to reverse {entry.Type} operation",
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
                "Rollback Successful",
                $"Successfully rolled back: {entry.Description}",
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
