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
/// Generic progress modal for long-running NuGet operations with cancellation support
/// </summary>
public static class OperationProgressModal
{
    public static async Task<OperationResult> ShowAsync(
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
        var tcs = new TaskCompletionSource<OperationResult>();

        // Controls that will be updated from async thread
        MarkupControl? statusLabel = null;
        ProgressBarControl? progressBar = null;
        ButtonControl? cancelButton = null;
        MarkupControl? logContent = null;

        // Log lines collection for display
        var logLines = new List<string>();
        var logLock = new object();

        // Async thread method that runs the operation
        async Task OperationThreadAsync(Window window, CancellationToken ct)
        {
            var startTime = DateTime.Now;
            OperationResult? result = null;

            try
            {
                // Progress reporter that updates log viewer in real-time
                var progress = new Progress<string>(line =>
                {
                    if (string.IsNullOrWhiteSpace(line)) return;

                    lock (logLock)
                    {
                        // Add timestamped log line
                        var elapsed = DateTime.Now - startTime;
                        var timestamp = $"[grey50]{elapsed.TotalSeconds:F1}s[/]";
                        var logLine = $"{timestamp} [grey70]{Markup.Escape(line)}[/]";

                        logLines.Add(logLine);

                        // Keep last 500 lines
                        if (logLines.Count > 500)
                        {
                            logLines.RemoveAt(0);
                        }

                        // Update log display
                        var logDisplay = string.Join("\n", logLines);
                        logContent?.SetContent(new List<string> { logDisplay });

                        // Update status with current action
                        if (line.Contains("Restoring packages") || line.Contains("Determining projects"))
                        {
                            statusLabel?.SetContent(new List<string> {
                                $"[{ColorScheme.PrimaryMarkup}]{Markup.Escape(title)}[/]",
                                $"[{ColorScheme.SecondaryMarkup}]📦 Restoring...[/]"
                            });
                        }
                        else if (line.Contains("Downloading") || line.Contains("GET"))
                        {
                            statusLabel?.SetContent(new List<string> {
                                $"[{ColorScheme.PrimaryMarkup}]{Markup.Escape(title)}[/]",
                                $"[{ColorScheme.SecondaryMarkup}]📥 Downloading...[/]"
                            });
                        }
                        else if (line.Contains("Installing") || line.Contains("Writing"))
                        {
                            statusLabel?.SetContent(new List<string> {
                                $"[{ColorScheme.PrimaryMarkup}]{Markup.Escape(title)}[/]",
                                $"[{ColorScheme.SecondaryMarkup}]📦 Installing...[/]"
                            });
                        }
                        else if (line.Contains("Generating") || line.Contains("Writing assets"))
                        {
                            statusLabel?.SetContent(new List<string> {
                                $"[{ColorScheme.PrimaryMarkup}]{Markup.Escape(title)}[/]",
                                $"[{ColorScheme.SecondaryMarkup}]📝 Finalizing...[/]"
                            });
                        }
                    }
                });

                // Perform operation with progress reporting
                result = await operation(ct, progress);

                var duration = DateTime.Now - startTime;

                // Update UI on completion
                if (progressBar != null)
                {
                    progressBar.IsIndeterminate = false;
                    progressBar.Value = 100;
                    progressBar.MaxValue = 100;
                }

                lock (logLock)
                {
                    if (result.Success)
                    {
                        statusLabel?.SetContent(new List<string> {
                            $"[{ColorScheme.SuccessMarkup} bold]✓ Completed in {duration.TotalSeconds:F1}s[/]",
                            $"[{ColorScheme.MutedMarkup}]Press Escape or click Close[/]"
                        });

                        // Add success summary to log
                        logLines.Add($"[{ColorScheme.SuccessMarkup} bold]✓ Operation completed successfully in {duration.TotalSeconds:F1}s[/]");
                        logContent?.SetContent(new List<string> { string.Join("\n", logLines) });
                    }
                    else
                    {
                        statusLabel?.SetContent(new List<string> {
                            $"[{ColorScheme.ErrorMarkup} bold]✗ Failed[/]",
                            $"[{ColorScheme.MutedMarkup}]{Markup.Escape(result.Message ?? "Unknown error")}[/]"
                        });

                        // Add error to log
                        logLines.Add($"[{ColorScheme.ErrorMarkup} bold]✗ Operation failed: {Markup.Escape(result.Message ?? "Unknown error")}[/]");
                        if (!string.IsNullOrEmpty(result.ErrorDetails))
                        {
                            foreach (var errorLine in result.ErrorDetails.Split('\n'))
                            {
                                logLines.Add($"[{ColorScheme.ErrorMarkup}]{Markup.Escape(errorLine)}[/]");
                            }
                        }
                        logContent?.SetContent(new List<string> { string.Join("\n", logLines) });
                    }
                }

                // Record to history if service provided
                if (historyService != null && projectName != null)
                {
                    var historyEntry = new OperationHistoryEntry
                    {
                        Type = operationType,
                        ProjectName = projectName,
                        Description = description,
                        Success = result.Success,
                        ErrorMessage = result.Success ? null : result.Message,
                        Duration = duration,
                        ProjectPath = projectPath ?? "",
                        PackageId = packageId,
                        PackageVersion = packageVersion
                    };
                    historyService.AddEntry(historyEntry);
                }

                // Update button to "Close" and make window closable
                if (cancelButton != null)
                {
                    cancelButton.Text = "[grey93]Close[/]";
                    cancelButton.IsEnabled = true;
                }
                window.IsClosable = true;

                tcs.SetResult(result);
            }
            catch (OperationCanceledException)
            {
                // User cancelled the operation
                var duration = DateTime.Now - startTime;
                var cancelResult = OperationResult.FromError("Cancelled", "The operation was cancelled by the user.");

                lock (logLock)
                {
                    statusLabel?.SetContent(new List<string> {
                        $"[{ColorScheme.WarningMarkup} bold]✗ Cancelled after {duration.TotalSeconds:F1}s[/]",
                        $"[{ColorScheme.MutedMarkup}]Press Escape or click Close[/]"
                    });

                    // Add cancellation to log
                    logLines.Add($"[{ColorScheme.WarningMarkup} bold]✗ Operation cancelled by user after {duration.TotalSeconds:F1}s[/]");
                    logContent?.SetContent(new List<string> { string.Join("\n", logLines) });
                }

                if (cancelButton != null)
                {
                    cancelButton.Text = "[grey93]Close[/]";
                    cancelButton.IsEnabled = true;
                }
                window.IsClosable = true;
                if (progressBar != null) progressBar.IsIndeterminate = false;

                // Record cancellation to history
                if (historyService != null && projectName != null)
                {
                    var historyEntry = new OperationHistoryEntry
                    {
                        Type = operationType,
                        ProjectName = projectName,
                        Description = description,
                        Success = false,
                        ErrorMessage = "Cancelled by user",
                        Duration = duration,
                        ProjectPath = projectPath ?? "",
                        PackageId = packageId,
                        PackageVersion = packageVersion
                    };
                    historyService.AddEntry(historyEntry);
                }

                tcs.SetResult(cancelResult);
            }
            catch (Exception ex)
            {
                var duration = DateTime.Now - startTime;
                var errorResult = OperationResult.FromError("Unexpected error", ex.Message);

                lock (logLock)
                {
                    statusLabel?.SetContent(new List<string> {
                        $"[{ColorScheme.ErrorMarkup} bold]✗ Error after {duration.TotalSeconds:F1}s[/]",
                        $"[{ColorScheme.MutedMarkup}]{Markup.Escape(ex.Message)}[/]"
                    });

                    // Add error to log
                    logLines.Add($"[{ColorScheme.ErrorMarkup} bold]✗ Unexpected error: {Markup.Escape(ex.Message)}[/]");
                    if (ex.StackTrace != null)
                    {
                        foreach (var stackLine in ex.StackTrace.Split('\n'))
                        {
                            logLines.Add($"[{ColorScheme.MutedMarkup}]{Markup.Escape(stackLine)}[/]");
                        }
                    }
                    logContent?.SetContent(new List<string> { string.Join("\n", logLines) });
                }

                if (cancelButton != null)
                {
                    cancelButton.Text = "[grey93]Close[/]";
                    cancelButton.IsEnabled = true;
                }
                window.IsClosable = true;
                if (progressBar != null) progressBar.IsIndeterminate = false;

                // Record error to history
                if (historyService != null && projectName != null)
                {
                    var historyEntry = new OperationHistoryEntry
                    {
                        Type = operationType,
                        ProjectName = projectName,
                        Description = description,
                        Success = false,
                        ErrorMessage = ex.Message,
                        Duration = duration,
                        ProjectPath = projectPath ?? "",
                        PackageId = packageId,
                        PackageVersion = packageVersion
                    };
                    historyService.AddEntry(historyEntry);
                }

                tcs.SetResult(errorResult);
            }
        }

        // Build modal window with async thread
        var builder = new WindowBuilder(windowSystem)
            .WithTitle(title)
            .Centered()
            .WithSize(90, 26)  // Larger for log viewer
            .AsModal()
            .Closable(false)  // Can't close during operation
            .Resizable(true)   // Allow resizing for better log viewing
            .Minimizable(false)
            .Maximizable(false)
            .Movable(true);

        if (parentWindow != null)
        {
            builder = builder.WithParent(parentWindow);
        }

        var modal = builder
            .WithAsyncWindowThread(OperationThreadAsync)
            .WithColors(ColorScheme.WindowBackground, Color.Grey93)
            .WithBorderStyle(BorderStyle.Single)
            .WithBorderColor(ColorScheme.BorderColor)
            .Build();

        // Status message (compact for log viewer)
        statusLabel = Controls.Markup()
            .AddLine($"[{ColorScheme.PrimaryMarkup}]{Markup.Escape(title)}[/]")
            .AddLine($"[{ColorScheme.SecondaryMarkup}]{Markup.Escape(description)}[/]")
            .WithMargin(2, 2, 2, 0)
            .Build();

        // Indeterminate progress bar (compact)
        progressBar = Controls.ProgressBar()
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

        logContent = Controls.Markup()
            .AddLine($"[{ColorScheme.MutedMarkup}]Waiting for output...[/]")
            .Build();
        logPanel.AddControl(logContent);

        // Separator
        var separator2 = Controls.RuleBuilder()
            .WithColor(ColorScheme.RuleColor)
            .StickyBottom()
            .Build();
        separator2.Margin = new Margin(2, 1, 2, 0);

        // Cancel button
        cancelButton = Controls.Button("[grey93]Cancel[/] [grey78](Esc)[/]")
            .WithMargin(2, 0, 0, 0)
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .Build();

        // Attach click handler
        cancelButton.Click += (s, e) =>
        {
            // Closing window will cancel the async thread via CancellationToken
            statusLabel.SetContent(new List<string> {
                $"[{ColorScheme.WarningMarkup} bold]Cancelling...[/]",
                $"[{ColorScheme.MutedMarkup}]Please wait for the operation to stop[/]"
            });
            cancelButton.IsEnabled = false;
            modal.IsClosable = true;
            modal.Close();
        };

        // Assemble modal layout
        modal.AddControl(statusLabel);
        modal.AddControl(progressBar);
        modal.AddControl(separator1);
        modal.AddControl(logPanel);     // Log viewer fills vertical space
        modal.AddControl(separator2);
        modal.AddControl(cancelButton);

        // Keyboard handling - Escape triggers cancel
        modal.KeyPressed += (s, e) =>
        {
            if (e.KeyInfo.Key == ConsoleKey.Escape && cancelButton.IsEnabled)
            {
                statusLabel.SetContent(new List<string> {
                    $"[{ColorScheme.WarningMarkup} bold]Cancelling...[/]",
                    $"[{ColorScheme.MutedMarkup}]Please wait for the operation to stop[/]"
                });
                cancelButton.IsEnabled = false;
                modal.IsClosable = true;
                modal.Close();
                e.Handled = true;
            }
        };

        // Show modal and set as active
        windowSystem.AddWindow(modal);
        windowSystem.SetActiveWindow(modal);

        // Wait for operation to complete
        return await tcs.Task;
    }
}
