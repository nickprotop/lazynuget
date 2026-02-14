using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Logging;
using Spectre.Console;
using LazyNuGet.UI.Utilities;
using HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment;
using VerticalAlignment = SharpConsoleUI.Layout.VerticalAlignment;

namespace LazyNuGet.UI;

/// <summary>
/// Always-on-top window for viewing application logs in real-time
/// </summary>
public class LogViewerWindow
{
    private readonly ConsoleWindowSystem _windowSystem;
    private Window? _window;
    private ScrollablePanelControl? _logPanel;
    private MarkupControl? _logContent;

    public LogViewerWindow(ConsoleWindowSystem windowSystem)
    {
        _windowSystem = windowSystem;
        BuildUI();
        SubscribeToLogs();
        Show();
    }

    private void BuildUI()
    {
        _window = new Window(_windowSystem)
        {
            Title = "Log Viewer",
            Width = 100,
            Height = 30,
            AlwaysOnTop = true
        };

        // Header
        var header = Controls.Markup()
            .AddLine("[cyan1 bold]Application Logs[/]")
            .AddLine("[grey50]Press Esc or Ctrl+L to close · Auto-scrolls to latest entries[/]")
            .WithMargin(1, 1, 1, 0)
            .WithAlignment(HorizontalAlignment.Stretch)
            .Build();
        _window.AddControl(header);

        // Separator
        var separator = Controls.Rule();
        separator.Color = ColorScheme.RuleColor;
        _window.AddControl(separator);

        // Scrollable log panel
        _logPanel = Controls.ScrollablePanel()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithBackgroundColor(ColorScheme.WindowBackground)
            .Build();

        _logContent = Controls.Markup()
            .AddLine("[grey50 italic]Waiting for log entries...[/]")
            .WithMargin(1, 1, 1, 1)
            .Build();
        _logPanel.AddControl(_logContent);

        _window.AddControl(_logPanel);

        // Setup keyboard handler for Esc and Ctrl+L to close
        _window.KeyPressed += (s, e) =>
        {
            if (e.KeyInfo.Key == ConsoleKey.Escape ||
                (e.KeyInfo.Key == ConsoleKey.L && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control)))
            {
                Close();
                e.Handled = true;
            }
        };
    }

    private void SubscribeToLogs()
    {
        // Load existing log buffer first
        RefreshLogDisplay();
        _windowSystem.LogService.LogAdded += OnLogAdded;
    }

    private void OnLogAdded(object? sender, LogEntry entry)
    {
        // When window is open, refresh display to show new log
        if (_window != null && _logPanel != null)
        {
            RefreshLogDisplay();
        }
    }

    private string FormatLogEntry(LogEntry entry)
    {
        var levelColor = entry.Level switch
        {
            LogLevel.Trace => "grey50",
            LogLevel.Debug => "grey70",
            LogLevel.Information => "cyan1",
            LogLevel.Warning => "yellow",
            LogLevel.Error => "red",
            LogLevel.Critical => "red bold",
            _ => "grey70"
        };

        var timestamp = entry.Timestamp.ToString("HH:mm:ss.fff");
        var category = !string.IsNullOrEmpty(entry.Category) ? $"[{entry.Category}]" : "";
        return $"[grey50]{timestamp}[/] [{levelColor}]{entry.Level,-11}[/] [grey70]{category,-12}[/] [{levelColor}]{Markup.Escape(entry.Message)}[/]";
    }

    private void RefreshLogDisplay()
    {
        if (_logPanel == null) return;

        // Get all logs from LogService buffer
        var allLogs = _windowSystem.LogService.GetAllLogs();

        // Build markup from LogService entries
        var builder = Controls.Markup();
        if (allLogs.Count == 0)
        {
            builder.AddLine("[grey50 italic]No log entries yet...[/]");
        }
        else
        {
            foreach (var entry in allLogs)
            {
                builder.AddLine(FormatLogEntry(entry));
            }
        }

        // Replace content
        _logPanel.ClearContents();
        _logContent = builder.WithMargin(1, 1, 1, 1).Build();
        _logPanel.AddControl(_logContent);

        // Auto-scroll to bottom
        _logPanel.ScrollToBottom();
    }

    private void Show()
    {
        if (_window != null)
        {
            _windowSystem.AddWindow(_window);
        }
    }

    private void Close()
    {
        _windowSystem.LogService.LogAdded -= OnLogAdded;
        _window?.Close();
    }
}
