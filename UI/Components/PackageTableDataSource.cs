using System.Collections.Specialized;
using LazyNuGet.Models;
using SharpConsoleUI;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Parsing;

namespace LazyNuGet.UI.Components;

/// <summary>
/// Backs the dashboard package table with the project's own package list.
/// </summary>
/// <remarks>
/// A data source rather than materialized <see cref="TableRow"/> objects, so a package is
/// formatted when it is about to be painted and the model stays the single copy of the data.
/// Sorting happens here, over <see cref="PackageReference"/> fields, which is why the status
/// and version columns can sort by what they MEAN (outdated-first, real version order) rather
/// than by the markup they render as.
///
/// This replaced a <c>ListControl.ItemFormatter</c> that hand-rolled the same three columns
/// with <c>PadRight</c>, a magic overhead constant, manual ellipsis truncation and
/// <c>string.Length</c> as a display width.
/// </remarks>
public sealed class PackageTableDataSource : ITableDataSource
{
    /// <summary>Column order, also the index contract for the methods below.</summary>
    private const int ColName = 0;
    private const int ColVersion = 1;

    private readonly List<PackageReference> _packages;
    private readonly List<PackageReference> _sourceOrder;

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    private readonly int _nameColumnWidth;

    public PackageTableDataSource(IEnumerable<PackageReference> packages)
    {
        _sourceOrder = packages.ToList();
        _packages = _sourceOrder.ToList();

        // The FLOOR for the auto name column: wide enough for the longest id, bounded so one
        // very long id cannot push the version columns off a narrow pane (the table pans
        // horizontally instead).
        int longest = _sourceOrder.Count > 0 ? _sourceOrder.Max(p => p.Id.Length) : 0;
        _nameColumnWidth = Math.Clamp(Math.Max(longest, "Package".Length) + 2, 18, 48);
    }

    public int RowCount => _packages.Count;

    public int ColumnCount => 2;

    /// <summary>The package a row stands for, or null when the index is stale.</summary>
    public PackageReference? PackageAt(int rowIndex) =>
        rowIndex >= 0 && rowIndex < _packages.Count ? _packages[rowIndex] : null;

    public string GetColumnHeader(int columnIndex) => columnIndex switch
    {
        ColName => "Package",
        ColVersion => "Version",
        _ => string.Empty
    };

    public string GetCellValue(int rowIndex, int columnIndex)
    {
        var pkg = PackageAt(rowIndex);
        if (pkg == null) return string.Empty;

        return columnIndex switch
        {
            // The status mark leads the name rather than owning a column of its own: a
            // dedicated column spent three cells and a header on one glyph, and the header
            // cell above it could only ever be blank.
            ColName => pkg.IsOutdated
                ? $"[yellow]↑[/] {MarkupParser.Escape(pkg.Id)}"
                : $"[grey50]·[/] {MarkupParser.Escape(pkg.Id)}",
            // One column, because "0.55.2 -> 0.57.2" is how the upgrade is actually read.
            // Two columns spent a vertical rule and 14 cells to split one fact in half.
            ColVersion => pkg.IsOutdated && !string.IsNullOrEmpty(pkg.LatestVersion)
                ? $"[grey70]{MarkupParser.Escape(pkg.Version)}[/] [yellow]→ {MarkupParser.Escape(pkg.LatestVersion)}[/]"
                : $"[grey70]{MarkupParser.Escape(pkg.Version)}[/]",
            _ => string.Empty
        };
    }

    public TextJustification GetColumnAlignment(int columnIndex) => TextJustification.Left;

    /// <summary>
    /// Column widths. The name column stays AUTO on purpose: it is the one column that should
    /// absorb the spare width of a wide pane.
    /// </summary>
    /// <remarks>
    /// It must also stay auto for the table to lay out at all. When every column is fixed the
    /// width calculator has no auto column to give the leftover to, and falls back to spreading
    /// it across all of them, which centers each value in a column several times its stated
    /// width. One auto column keeps the fixed ones honest.
    /// </remarks>
    public int? GetColumnWidth(int columnIndex) => columnIndex switch
    {
        // Room for "0.55.2 -> 0.57.2" plus the longest real-world version pair.
        ColVersion => 26,
        _ => null
    };

    /// <summary>
    /// Keeps the package name readable when the pane is narrow. The table pans horizontally
    /// rather than shrinking a name to nothing.
    /// </summary>
    public int? GetColumnMinWidth(int columnIndex) => columnIndex == ColName ? _nameColumnWidth : null;

    public object? GetRowTag(int rowIndex) => PackageAt(rowIndex);

    public bool CanSort(int columnIndex) => true;

    public void Sort(int columnIndex, SortDirection direction)
    {
        if (direction == SortDirection.None)
        {
            ClearSort();
            return;
        }

        bool asc = direction == SortDirection.Ascending;

        // Sort on the MODEL, not the rendered markup: "[yellow]..." would otherwise order by
        // colour name, and "0.57.2" against "0.9.0" would order lexically.
        IEnumerable<PackageReference> ordered = columnIndex switch
        {
            ColVersion => Order(_packages, p => p.Version, asc),
            // The name column leads with the status mark, so sorting it puts what needs
            // attention first and then orders by name inside each group.
            ColName => asc
                ? _packages.OrderByDescending(p => p.IsOutdated).ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                : _packages.OrderBy(p => p.IsOutdated).ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase),
            _ => asc
                ? _packages.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                : _packages.OrderByDescending(p => p.Id, StringComparer.OrdinalIgnoreCase)
        };

        _packages.Clear();
        _packages.AddRange(ordered);
        RaiseReset();
    }

    public void ClearSort()
    {
        _packages.Clear();
        _packages.AddRange(_sourceOrder);
        RaiseReset();
    }

    /// <summary>
    /// Orders by a version string using real version comparison, falling back to text for
    /// anything that is not a version (a floating range, an empty latest).
    /// </summary>
    private static IEnumerable<PackageReference> Order(
        List<PackageReference> packages, Func<PackageReference, string?> field, bool ascending)
    {
        var keyed = packages.Select(p =>
        {
            string raw = field(p) ?? string.Empty;
            return (pkg: p, version: NuGet.Versioning.NuGetVersion.TryParse(raw, out var v) ? v : null, raw);
        });

        // Unparseable versions sort last in both directions: they are not "smallest", they are
        // "unknown", and burying them keeps the real versions contiguous.
        var parsed = keyed.Where(k => k.version != null);
        var unparsed = keyed.Where(k => k.version == null).OrderBy(k => k.raw, StringComparer.OrdinalIgnoreCase);

        parsed = ascending
            ? parsed.OrderBy(k => k.version)
            : parsed.OrderByDescending(k => k.version);

        return parsed.Concat(unparsed).Select(k => k.pkg);
    }

    private void RaiseReset() =>
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
}
