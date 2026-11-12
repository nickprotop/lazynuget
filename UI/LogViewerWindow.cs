using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Logging;
using LazyNuGet.UI.Components;
using LazyNuGet.UI.Utilities;

namespace LazyNuGet.UI;

/// <summary>
/// Always-on-top window for viewing application logs in real-time.
/// </summary>
/// <remarks>
/// Was a scrollable panel holding one <see cref="MarkupControl"/> built from the whole buffer,
/// with the columns hand-padded and the entire control rebuilt on EVERY arriving entry. It is
/// a table now: the columns are measured, a new entry appends one row, and the level filter
/// works without reformatting anything.
/// </remarks>
public class LogViewerWindow
{
    private readonly ConsoleWindowSystem _windowSystem;
    private readonly LogTableDataSource _source = new();
    private Window? _window;
    private TableControl? _table;
    private MarkupControl? _status;

    /// <summary>Levels the F-key cycle steps through, quietest last.</summary>
    private static readonly LogLevel[] LevelCycle =
    {
        LogLevel.Information, LogLevel.Warning, LogLevel.Error, LogLevel.Trace
    };

    private int _levelIndex;

    public LogViewerWindow(ConsoleWindowSystem windowSystem)
    {
        _windowSystem = windowSystem;
        BuildUI();
        SubscribeToLogs();
        Show();
    }

    private void BuildUI()
    {
        _window = new WindowBuilder(_windowSystem)
            .WithTitle("Logs")
            .WithSize(120, 32)
            .Centered()
            .WithColors(Color.Grey85, ColorScheme.DetailsPanelBackground)
            .WithActiveBorderColor(ColorScheme.AccentBorderColor)
            .Resizable()
            .Build();
        _window.AlwaysOnTop = true;

        _status = Controls.Markup(StatusLine())
            .WithMargin(1, 0, 1, 0)
            .Build();

        _table = Controls.Table()
            .WithDataSource(_source)
            .WithMargin(1, 0, 1, 0)
            .WithName("logTable")
            .WithHorizontalAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .ShowHeader()
            .WithHeaderColors(Color.Grey93, ColorScheme.HeaderBackground)
            .WithColors(Color.Grey85, ColorScheme.InsetBackground)
            .WithBorderColor(ColorScheme.BorderColor)
            .SingleLine()
            .WithColumnSeparator('│', ColorScheme.RuleColor, padded: true)
            .Interactive()
            .Build();

        // One grid: the status line is Auto, the table takes the rest. A stacked panel would
        // have to be told the table's height; a grid row just gives it what is left.
        var grid = Controls.Grid()
            .Rows(GridLength.Auto(), GridLength.Star(1))
            .Columns(GridLength.Star(1))
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .Place(_status, row: 0, col: 0)
            .Place(_table, row: 1, col: 0)
            .Build();

        _window.AddControl(grid);

        _window.KeyPressed += (s, e) =>
        {
            if (e.KeyInfo.Key == ConsoleKey.Escape ||
                (e.KeyInfo.Key == ConsoleKey.L && e.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control)))
            {
                Close();
                e.Handled = true;
                return;
            }

            // F cycles the minimum level, so a noisy buffer can be cut down to warnings and
            // errors without leaving the window.
            if (e.KeyInfo.Key == ConsoleKey.F && e.KeyInfo.Modifiers == 0)
            {
                _levelIndex = (_levelIndex + 1) % LevelCycle.Length;
                _source.MinimumLevel = LevelCycle[_levelIndex];
                _status?.SetContent(new List<string> { StatusLine() });
                e.Handled = true;
            }
        };
    }

    private string StatusLine()
    {
        string level = LevelCycle[_levelIndex] switch
        {
            LogLevel.Trace => "[grey54]all[/]",
            LogLevel.Information => "[grey70]info and above[/]",
            LogLevel.Warning => "[yellow]warnings and above[/]",
            LogLevel.Error => "[red]errors only[/]",
            _ => "[grey70]info and above[/]"
        };

        return $"[grey42]Showing[/] {level}   [grey42]F cycles the level · Esc closes[/]";
    }

    private void SubscribeToLogs()
    {
        _source.Reset(_windowSystem.LogService.GetAllLogs());
        _source.MinimumLevel = LevelCycle[_levelIndex];
        _windowSystem.LogService.LogAdded += OnLogAdded;
        ScrollToLatest();
    }

    private void OnLogAdded(object? sender, LogEntry entry)
    {
        if (_window == null || _table == null) return;

        // The log service raises this from whatever thread logged, so the append has to be
        // marshalled: the render loop reads the table concurrently.
        _windowSystem.EnqueueOnUIThread(() =>
        {
            _source.Append(entry);
            ScrollToLatest();
        });
    }

    /// <summary>Keeps the newest entry in view, the way a tail would.</summary>
    private void ScrollToLatest()
    {
        if (_table == null || _source.RowCount == 0) return;
        _table.SelectedRowIndex = _source.RowCount - 1;
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
