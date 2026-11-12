using System.Collections.Specialized;
using SharpConsoleUI;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Logging;
using SharpConsoleUI.Parsing;

namespace LazyNuGet.UI.Components;

/// <summary>
/// Backs the log viewer with the log buffer.
/// </summary>
/// <remarks>
/// The viewer used to be a single <see cref="MarkupControl"/> holding every entry as one
/// string, with the columns hand-padded (<c>{entry.Level,-11}</c>, <c>{category,-12}</c>) and
/// the WHOLE control rebuilt from the entire buffer on every single arriving entry. A data
/// source formats only the rows about to be painted, and appending one entry costs one
/// notification instead of rebuilding thousands of lines.
/// </remarks>
public sealed class LogTableDataSource : ITableDataSource
{
    private const int ColTime = 0;
    private const int ColLevel = 1;
    private const int ColCategory = 2;
    private const int ColMessage = 3;

    private readonly List<LogEntry> _entries = new();
    private LogLevel _minLevel = LogLevel.Trace;
    private string _filter = string.Empty;
    private List<LogEntry> _view = new();

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public int RowCount => _view.Count;

    public int ColumnCount => 4;

    /// <summary>Replaces the whole buffer, for the initial load.</summary>
    public void Reset(IEnumerable<LogEntry> entries)
    {
        _entries.Clear();
        _entries.AddRange(entries);
        Reproject();
    }

    /// <summary>
    /// Appends one entry. Raises Add rather than Reset when the entry is visible under the
    /// current filter, so the table does not re-measure every row for each arriving line.
    /// </summary>
    public void Append(LogEntry entry)
    {
        _entries.Add(entry);
        if (!Passes(entry)) return;

        _view.Add(entry);
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Add, entry, _view.Count - 1));
    }

    /// <summary>The minimum level shown. Entries below it stay in the buffer, unshown.</summary>
    public LogLevel MinimumLevel
    {
        get => _minLevel;
        set { if (_minLevel == value) return; _minLevel = value; Reproject(); }
    }

    /// <summary>A substring the message or category must contain. Empty shows everything.</summary>
    public string Filter
    {
        get => _filter;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_filter, value, StringComparison.Ordinal)) return;
            _filter = value;
            Reproject();
        }
    }

    public LogEntry? EntryAt(int rowIndex) =>
        rowIndex >= 0 && rowIndex < _view.Count ? _view[rowIndex] : null;

    public string GetColumnHeader(int columnIndex) => columnIndex switch
    {
        ColTime => "Time",
        ColLevel => "Level",
        ColCategory => "Category",
        ColMessage => "Message",
        _ => string.Empty
    };

    public string GetCellValue(int rowIndex, int columnIndex)
    {
        var e = EntryAt(rowIndex);
        if (e == null) return string.Empty;

        return columnIndex switch
        {
            ColTime => $"[grey42]{e.Timestamp:HH:mm:ss.fff}[/]",
            ColLevel => $"[{LevelColor(e.Level)}]{LevelLabel(e.Level)}[/]",
            ColCategory => $"[grey54]{MarkupParser.Escape(e.Category ?? string.Empty)}[/]",
            // The message carries the level's colour only when the level is worth noticing;
            // routine lines stay neutral so a warning actually stands out.
            ColMessage => e.Level >= LogLevel.Warning
                ? $"[{LevelColor(e.Level)}]{MarkupParser.Escape(e.Message)}[/]"
                : MarkupParser.Escape(e.Message),
            _ => string.Empty
        };
    }

    public int? GetColumnWidth(int columnIndex) => columnIndex switch
    {
        ColTime => 12,
        ColLevel => 7,
        ColCategory => 16,
        // Message stays AUTO: it is the column that should take the spare width, and a table
        // whose columns are ALL fixed has nothing to hand the leftover to, so the calculator
        // spreads it across every column and centers each value in a field several times its
        // stated width.
        _ => null
    };

    public Color? GetRowForegroundColor(int rowIndex)
    {
        var e = EntryAt(rowIndex);
        return e != null && e.Level >= LogLevel.Error ? Color.Grey93 : null;
    }

    public object? GetRowTag(int rowIndex) => EntryAt(rowIndex);

    /// <summary>Short, fixed-width level labels so the column never reflows.</summary>
    private static string LevelLabel(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Information => "INFO",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "ERROR",
        LogLevel.Critical => "CRIT",
        _ => level.ToString().ToUpperInvariant()
    };

    private static string LevelColor(LogLevel level) => level switch
    {
        LogLevel.Trace => "grey35",
        LogLevel.Debug => "grey54",
        LogLevel.Information => "grey70",
        LogLevel.Warning => "yellow",
        LogLevel.Error => "red",
        LogLevel.Critical => "red bold",
        _ => "grey70"
    };

    private bool Passes(LogEntry e)
    {
        if (e.Level < _minLevel) return false;
        if (_filter.Length == 0) return true;

        return e.Message.Contains(_filter, StringComparison.OrdinalIgnoreCase)
            || (e.Category?.Contains(_filter, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void Reproject()
    {
        _view = _entries.Where(Passes).ToList();
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Reset));
    }
}
