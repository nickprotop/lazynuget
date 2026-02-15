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
/// Generic progress modal for long-running NuGet operations with cancellation support
/// </summary>
public class OperationProgressModal : ModalBase<OperationResult>
{
    private readonly OperationType _operationType;
    private readonly Func<CancellationToken, IProgress<string>, Task<OperationResult>> _operation;
    private readonly string _title;
    private readonly string _description;
    private readonly OperationHistoryService? _historyService;
    private readonly string? _projectPath;
    private readonly string? _projectName;
    private readonly string? _packageId;
    private readonly string? _packageVersion;

    private MarkupControl? _statusLabel;
    private ProgressBarControl? _progressBar;
    private ButtonControl? _cancelButton;
    private MarkupControl? _logContent;
    private readonly List<string> _logLines = new();
    private readonly object _logLock = new();
    private DateTime _startTime;
    private OperationResult? _finalResult;

    private OperationProgressModal(
        OperationType operationType,
        Func<CancellationToken, IProgress<string>, Task<OperationResult>> operation,
        string title,
        string description,
        OperationHistoryService? historyService,
        string? projectPath,
        string? projectName,
        string? packageId,
        string? packageVersion)
    {
        _operationType = operationType;
        _operation = operation;
        _title = title;
        _description = description;
        _historyService = historyService;
        _projectPath = projectPath;
        _projectName = projectName;
        _packageId = packageId;
        _packageVersion = packageVersion;
    }

    public static Task<OperationResult> ShowAsync(
        ConsoleWindowSystem windowSystem,
        OperationType operationType,
        Func<CancellationToken, IProgress<string>, Task<OperationResult>> operation,
        string title,
        string description,
        OperationHistoryService? historyService = null,
        string? projectPath = null,
        string? projectName = null,
        string? packageId = null,
        string? packageVersion = null,
        Window? parentWindow = null)
    {
        var instance = new OperationProgressModal(
            operationType,
            operation,
            title,
            description,
            historyService,
            projectPath,
            projectName,
            packageId,
            packageVersion);
        return ((ModalBase<OperationResult>)instance).ShowAsync(windowSystem, parentWindow);
    }

    protected override string GetTitle() => _title;
    protected override (int width, int height) GetSize() => (90, 26);
    protected override bool GetResizable() => true;
    protected override OperationResult GetDefaultResult() => OperationResult.FromError("Cancelled", "The operation was cancelled.");

    protected override Window CreateModal()
    {
        var modal = base.CreateModal();
        modal.IsClosable = false;  // Start non-closable during operation
        return modal;
    }

    protected override void BuildContent()
    {
        // Status message (compact for log viewer)
        _statusLabel = Controls.Markup()
            .AddLine($"[{ColorScheme.PrimaryMarkup}]{Markup.Escape(_title)}[/]")
            .AddLine($"[{ColorScheme.SecondaryMarkup}]{Markup.Escape(_description)}[/]")
            .WithMargin(2, 2, 2, 0)
            .Build();

        // Indeterminate progress bar (compact)
        _progressBar = Controls.ProgressBar()
            .Indeterminate(true)
            .WithAnimationInterval(100)
            .ShowPercentage(false)
            .WithMargin(2, 1, 2, 1)
            .Build();

        // Separator
        var separator1 = Controls.RuleBuilder()
            .WithColor(ColorScheme.RuleColor)
            .Build();
        separator1.Margin = new Margin(2, 1, 2, 0);

        // Real-time log viewer (scrollable, fills remaining space)
        var logPanel = Controls.ScrollablePanel()
            .WithVerticalScroll(ScrollMode.Scroll)
            .WithScrollbar(true)
            .WithMouseWheel(true)
            .WithAutoScroll(true)  // Auto-scroll to bottom as logs arrive
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithColors(Color.Grey93, ColorScheme.StatusBarBackground)
            .WithMargin(2, 0, 2, 1)
            .Build();

        _logContent = Controls.Markup()
            .AddLine($"[{ColorScheme.MutedMarkup}]Waiting for output...[/]")
            .Build();
        logPanel.AddControl(_logContent);

        // Separator
        var separator2 = Controls.RuleBuilder()
            .WithColor(ColorScheme.RuleColor)
            .StickyBottom()
            .Build();
        separator2.Margin = new Margin(2, 1, 2, 0);

        // Cancel button
        _cancelButton = Controls.Button("[grey93]Cancel[/] [grey78](Esc)[/]")
            .WithMargin(2, 0, 0, 0)
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .Build();

        // Attach click handler
        _cancelButton.Click += OnCancelButtonClick;

        // Assemble modal layout
        Modal.AddControl(_statusLabel);
        Modal.AddControl(_progressBar);
        Modal.AddControl(separator1);
        Modal.AddControl(logPanel);
        Modal.AddControl(separator2);
        Modal.AddControl(_cancelButton);

        // Start async operation
        _startTime = DateTime.Now;
        AsyncHelper.FireAndForget(
            () => RunOperationAsync(),
            ex =>
            {
                _statusLabel?.SetContent(new List<string> {
                    $"[{ColorScheme.ErrorMarkup} bold]Unexpected error[/]",
                    $"[{ColorScheme.MutedMarkup}]{Markup.Escape(ex.Message)}[/]"
                });
            });
    }

    private async Task RunOperationAsync()
    {
        OperationResult? result = null;

        try
        {
            // Progress reporter that updates log viewer in real-time
            var progress = new Progress<string>(line =>
            {
                if (string.IsNullOrWhiteSpace(line)) return;

                lock (_logLock)
                {
                    // Add timestamped log line
                    var elapsed = DateTime.Now - _startTime;
                    var timestamp = $"[grey50]{elapsed.TotalSeconds:F1}s[/]";
                    var logLine = $"{timestamp} [grey70]{Markup.Escape(line)}[/]";

                    _logLines.Add(logLine);

                    // Keep last 500 lines
                    if (_logLines.Count > 500)
                    {
                        _logLines.RemoveAt(0);
                    }

                    // Update log display
                    var logDisplay = string.Join("\n", _logLines);
                    _logContent?.SetContent(new List<string> { logDisplay });

                    // Update status with current action
                    if (line.Contains("Restoring packages") || line.Contains("Determining projects"))
                    {
                        _statusLabel?.SetContent(new List<string> {
                            $"[{ColorScheme.PrimaryMarkup}]{Markup.Escape(_title)}[/]",
                            $"[{ColorScheme.SecondaryMarkup}]📦 Restoring...[/]"
                        });
                    }
                    else if (line.Contains("Downloading") || line.Contains("GET"))
                    {
                        _statusLabel?.SetContent(new List<string> {
                            $"[{ColorScheme.PrimaryMarkup}]{Markup.Escape(_title)}[/]",
                            $"[{ColorScheme.SecondaryMarkup}]📥 Downloading...[/]"
                        });
                    }
                    else if (line.Contains("Installing") || line.Contains("Writing"))
                    {
                        _statusLabel?.SetContent(new List<string> {
                            $"[{ColorScheme.PrimaryMarkup}]{Markup.Escape(_title)}[/]",
                            $"[{ColorScheme.SecondaryMarkup}]📦 Installing...[/]"
                        });
                    }
                    else if (line.Contains("Generating") || line.Contains("Writing assets"))
                    {
                        _statusLabel?.SetContent(new List<string> {
                            $"[{ColorScheme.PrimaryMarkup}]{Markup.Escape(_title)}[/]",
                            $"[{ColorScheme.SecondaryMarkup}]📝 Finalizing...[/]"
                        });
                    }
                }
            });

            // Perform operation with progress reporting
            // Note: We're not passing a CancellationToken here since we handle cancellation via window close
            result = await _operation(CancellationToken.None, progress);

            var duration = DateTime.Now - _startTime;

            // Update UI on completion
            if (_progressBar != null)
            {
                _progressBar.IsIndeterminate = false;
                _progressBar.Value = 100;
                _progressBar.MaxValue = 100;
            }

            lock (_logLock)
            {
                if (result.Success)
                {
                    _statusLabel?.SetContent(new List<string> {
                        $"[{ColorScheme.SuccessMarkup} bold]✓ Completed in {duration.TotalSeconds:F1}s[/]",
                        $"[{ColorScheme.MutedMarkup}]Press Escape or click Close[/]"
                    });

                    // Add success summary to log
                    _logLines.Add($"[{ColorScheme.SuccessMarkup} bold]✓ Operation completed successfully in {duration.TotalSeconds:F1}s[/]");
                    _logContent?.SetContent(new List<string> { string.Join("\n", _logLines) });
                }
                else
                {
                    _statusLabel?.SetContent(new List<string> {
                        $"[{ColorScheme.ErrorMarkup} bold]✗ Failed[/]",
                        $"[{ColorScheme.MutedMarkup}]{Markup.Escape(result.Message ?? "Unknown error")}[/]"
                    });

                    // Add error to log
                    _logLines.Add($"[{ColorScheme.ErrorMarkup} bold]✗ Operation failed: {Markup.Escape(result.Message ?? "Unknown error")}[/]");
                    if (!string.IsNullOrEmpty(result.ErrorDetails))
                    {
                        foreach (var errorLine in result.ErrorDetails.Split('\n'))
                        {
                            _logLines.Add($"[{ColorScheme.ErrorMarkup}]{Markup.Escape(errorLine)}[/]");
                        }
                    }
                    _logContent?.SetContent(new List<string> { string.Join("\n", _logLines) });
                }
            }

            // Record to history if service provided
            RecordToHistory(result.Success, result.Success ? null : result.Message, duration);

            // Update button to "Close" and make window closable
            if (_cancelButton != null)
            {
                _cancelButton.Text = "[grey93]Close[/] [grey78](Esc)[/]";
                _cancelButton.IsEnabled = true;
            }
            if (Modal != null)
            {
                Modal.IsClosable = true;
            }

            // Don't auto-close - let user review results and close manually
            // Store result for when user closes the modal
            _finalResult = result;
        }
        catch (OperationCanceledException)
        {
            // User cancelled the operation
            var duration = DateTime.Now - _startTime;
            var cancelResult = OperationResult.FromError("Cancelled", "The operation was cancelled by the user.");

            lock (_logLock)
            {
                _statusLabel?.SetContent(new List<string> {
                    $"[{ColorScheme.WarningMarkup} bold]✗ Cancelled after {duration.TotalSeconds:F1}s[/]",
                    $"[{ColorScheme.MutedMarkup}]Press Escape or click Close[/]"
                });

                // Add cancellation to log
                _logLines.Add($"[{ColorScheme.WarningMarkup} bold]✗ Operation cancelled by user after {duration.TotalSeconds:F1}s[/]");
                _logContent?.SetContent(new List<string> { string.Join("\n", _logLines) });
            }

            if (_cancelButton != null)
            {
                _cancelButton.Text = "[grey93]Close[/] [grey78](Esc)[/]";
                _cancelButton.IsEnabled = true;
            }
            if (Modal != null)
            {
                Modal.IsClosable = true;
            }
            if (_progressBar != null)
            {
                _progressBar.IsIndeterminate = false;
            }

            // Record cancellation to history
            RecordToHistory(false, "Cancelled by user", duration);

            // Don't auto-close - let user review cancellation and close manually
            _finalResult = cancelResult;
        }
        catch (Exception ex)
        {
            var duration = DateTime.Now - _startTime;
            var errorResult = OperationResult.FromError("Unexpected error", ex.Message);

            lock (_logLock)
            {
                _statusLabel?.SetContent(new List<string> {
                    $"[{ColorScheme.ErrorMarkup} bold]✗ Error after {duration.TotalSeconds:F1}s[/]",
                    $"[{ColorScheme.MutedMarkup}]{Markup.Escape(ex.Message)}[/]"
                });

                // Add error to log
                _logLines.Add($"[{ColorScheme.ErrorMarkup} bold]✗ Unexpected error: {Markup.Escape(ex.Message)}[/]");
                if (ex.StackTrace != null)
                {
                    foreach (var stackLine in ex.StackTrace.Split('\n'))
                    {
                        _logLines.Add($"[{ColorScheme.MutedMarkup}]{Markup.Escape(stackLine)}[/]");
                    }
                }
                _logContent?.SetContent(new List<string> { string.Join("\n", _logLines) });
            }

            if (_cancelButton != null)
            {
                _cancelButton.Text = "[grey93]Close[/] [grey78](Esc)[/]";
                _cancelButton.IsEnabled = true;
            }
            if (Modal != null)
            {
                Modal.IsClosable = true;
            }
            if (_progressBar != null)
            {
                _progressBar.IsIndeterminate = false;
            }

            // Record error to history
            RecordToHistory(false, ex.Message, duration);

            // Don't auto-close - let user review error and close manually
            _finalResult = errorResult;
        }
    }

    private void OnCancelButtonClick(object? sender, ButtonControl e)
    {
        // If operation is still running, cancel it
        if (_finalResult == null)
        {
            if (_statusLabel != null && _cancelButton != null)
            {
                _statusLabel.SetContent(new List<string> {
                    $"[{ColorScheme.WarningMarkup} bold]Cancelling...[/]",
                    $"[{ColorScheme.MutedMarkup}]Please wait for the operation to stop[/]"
                });
                _cancelButton.IsEnabled = false;
            }
            if (Modal != null)
            {
                Modal.IsClosable = true;
                Modal.Close();
            }
        }
        else
        {
            // Operation completed, close with stored result
            CloseWithResult(_finalResult);
        }
    }

    private void RecordToHistory(bool success, string? errorMessage, TimeSpan duration)
    {
        if (_historyService != null && _projectName != null)
        {
            var historyEntry = new OperationHistoryEntry
            {
                Type = _operationType,
                ProjectName = _projectName,
                Description = _description,
                Success = success,
                ErrorMessage = errorMessage,
                Duration = duration,
                ProjectPath = _projectPath ?? "",
                PackageId = _packageId,
                PackageVersion = _packageVersion
            };
            _historyService.AddEntry(historyEntry);
        }
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape)
        {
            // If operation completed, close with result
            if (_finalResult != null && Modal?.IsClosable == true)
            {
                CloseWithResult(_finalResult);
                e.Handled = true;
            }
            // If still running and cancel button is enabled, cancel
            else if (_cancelButton?.IsEnabled == true)
            {
                OnCancelButtonClick(sender, _cancelButton);
                e.Handled = true;
            }
        }
        else
        {
            base.OnKeyPressed(sender, e);
        }
    }

    protected override void OnCleanup()
    {
        if (_cancelButton != null)
        {
            _cancelButton.Click -= OnCancelButtonClick;
        }
    }
}
