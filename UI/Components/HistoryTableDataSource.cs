using System.Collections.Specialized;
using LazyNuGet.Models;
using LazyNuGet.UI.Utilities;
using SharpConsoleUI;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Parsing;

namespace LazyNuGet.UI.Components;

/// <summary>
/// Backs the operation history table.
/// </summary>
/// <remarks>
/// The history was a ListControl whose rows packed status, type, description, duration and
/// age into one string, with parentheses and dashes standing in for columns
/// ("<c>(1.2s) - 3 hours ago</c>"). Those are five fields, so they are five columns, and the
/// table sorts them on the model: age by real timestamp, duration by TimeSpan.
/// </remarks>
public sealed class HistoryTableDataSource : ITableDataSource
{
    private const int ColStatus = 0;
    private const int ColType = 1;
    private const int ColDescription = 2;
    private const int ColDuration = 3;
    private const int ColWhen = 4;

    private readonly List<OperationHistoryEntry> _sourceOrder = new();
    private List<OperationHistoryEntry> _rows = new();

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public int RowCount => _rows.Count;

    public int ColumnCount => 5;

    /// <summary>Replaces the rows, for a filter change or a reload.</summary>
    public void SetEntries(IEnumerable<OperationHistoryEntry> entries)
    {
        _sourceOrder.Clear();
        _sourceOrder.AddRange(entries);
        _rows = _sourceOrder.ToList();
        RaiseReset();
    }

    public OperationHistoryEntry? EntryAt(int rowIndex) =>
        rowIndex >= 0 && rowIndex < _rows.Count ? _rows[rowIndex] : null;

    public string GetColumnHeader(int columnIndex) => columnIndex switch
    {
        ColStatus => string.Empty,
        ColType => "Operation",
        ColDescription => "Description",
        ColDuration => "Took",
        ColWhen => "When",
        _ => string.Empty
    };

    public string GetCellValue(int rowIndex, int columnIndex)
    {
        var e = EntryAt(rowIndex);
        if (e == null) return string.Empty;

        return columnIndex switch
        {
            ColStatus => e.Success
                ? $"[{ColorScheme.SuccessMarkup}]✓[/]"
                : $"[{ColorScheme.ErrorMarkup}]✗[/]",
            ColType => $"[grey70]{TypeLabel(e.Type)}[/]",
            // A failure's reason belongs with the failure. It used to be appended as a second
            // line inside the same list item, which a table row cannot carry.
            ColDescription => !e.Success && !string.IsNullOrEmpty(e.ErrorMessage)
                ? $"{MarkupParser.Escape(e.Description)}  [{ColorScheme.ErrorMarkup}]{MarkupParser.Escape(Truncate(e.ErrorMessage, 60))}[/]"
                : MarkupParser.Escape(e.Description),
            ColDuration => $"[grey54]{e.Duration.TotalSeconds:F1}s[/]",
            ColWhen => $"[grey54]{GetTimeAgo(e.Timestamp)}[/]",
            _ => string.Empty
        };
    }

    public TextJustification GetColumnAlignment(int columnIndex) => columnIndex switch
    {
        ColStatus => TextJustification.Center,
        ColDuration => TextJustification.Right,
        _ => TextJustification.Left
    };

    public int? GetColumnWidth(int columnIndex) => columnIndex switch
    {
        ColStatus => 3,
        ColType => 11,
        ColDuration => 8,
        ColWhen => 16,
        // Description stays AUTO: it takes the spare width, and a table whose columns are ALL
        // fixed has nothing to hand the leftover to, so the calculator spreads it across every
        // column instead.
        _ => null
    };

    public int? GetColumnMinWidth(int columnIndex) => columnIndex == ColDescription ? 24 : null;

    public object? GetRowTag(int rowIndex) => EntryAt(rowIndex);

    public bool CanSort(int columnIndex) => true;

    public void Sort(int columnIndex, SortDirection direction)
    {
        if (direction == SortDirection.None) { ClearSort(); return; }
        bool asc = direction == SortDirection.Ascending;

        // Sort on the model: the rendered cells carry markup and a humanized age
        // ("3 hours ago"), neither of which orders correctly as text.
        IEnumerable<OperationHistoryEntry> ordered = columnIndex switch
        {
            ColStatus => asc ? _rows.OrderBy(e => e.Success) : _rows.OrderByDescending(e => e.Success),
            ColType => asc ? _rows.OrderBy(e => e.Type) : _rows.OrderByDescending(e => e.Type),
            ColDuration => asc ? _rows.OrderBy(e => e.Duration) : _rows.OrderByDescending(e => e.Duration),
            ColWhen => asc ? _rows.OrderBy(e => e.Timestamp) : _rows.OrderByDescending(e => e.Timestamp),
            _ => asc
                ? _rows.OrderBy(e => e.Description, StringComparer.OrdinalIgnoreCase)
                : _rows.OrderByDescending(e => e.Description, StringComparer.OrdinalIgnoreCase)
        };

        _rows = ordered.ToList();
        RaiseReset();
    }

    public void ClearSort()
    {
        _rows = _sourceOrder.ToList();
        RaiseReset();
    }

    private static string TypeLabel(OperationType type) => type switch
    {
        OperationType.Restore => "Restore",
        OperationType.Update => "Update",
        OperationType.Add => "Add",
        OperationType.Remove => "Remove",
        _ => type.ToString()
    };

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";

    /// <summary>Humanized age, for reading. Sorting uses the timestamp itself.</summary>
    private static string GetTimeAgo(DateTime timestamp)
    {
        var span = DateTime.Now - timestamp;
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays}d ago";
        return timestamp.ToString("yyyy-MM-dd");
    }

    private void RaiseReset() =>
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
}
