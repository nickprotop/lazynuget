using System.Text;

namespace LazyNuGet.Orchestrators;

/// <summary>
/// Owns rendering and click hit-testing for the top-right status bar.
/// Supports interleaved non-clickable segments (stats, clock) and clickable hints
/// (e.g. "^R:Refresh" when the cache is warm).
///
/// Renders left-to-right, identical hint format to HelpBar.
/// HandleClick() accounts for right-alignment by using the window width to locate
/// the rendered content's left edge on screen.
/// </summary>
public class StatusBar : ClickableBar
{
    private enum PartKind { Segment, Hint }
    private readonly record struct Part(PartKind Kind, string Markup, int PlainLength, BarItem? Item);

    private readonly List<Part> _parts = new();

    public override void Clear()
    {
        _parts.Clear();
        base.Clear();
    }

    /// <summary>
    /// Add a non-clickable text segment.
    /// <paramref name="markup"/>    is the Spectre markup to render.
    /// <paramref name="plainText"/> is the markup-stripped equivalent, used only for length tracking.
    /// </summary>
    public StatusBar AddSegment(string markup, string plainText)
    {
        _parts.Add(new Part(PartKind.Segment, markup, plainText.Length, null));
        return this;
    }

    /// <summary>
    /// Add a clickable hint using the same "[cyan1]shortcut[/][grey70]:label  [/]" format as HelpBar.
    /// </summary>
    public StatusBar AddHint(string shortcut, string label, Action? onClick = null)
    {
        var item = new BarItem { Shortcut = shortcut, Label = label, OnClick = onClick };
        _items.Add(item);
        _parts.Add(new Part(PartKind.Hint, string.Empty, 0, item));
        return this;
    }

    /// <summary>
    /// Produces a Spectre markup string and records StartX/EndX for each clickable hint.
    /// </summary>
    public override string Render()
    {
        var sb = new StringBuilder();
        int cursor = 0;

        foreach (var part in _parts)
        {
            if (part.Kind == PartKind.Segment)
            {
                sb.Append(part.Markup);
                cursor += part.PlainLength;
            }
            else // Hint
            {
                var item = part.Item!;
                int plainLength = item.Shortcut.Length + 1 + item.Label.Length; // +1 for ':'
                item.StartX = cursor;
                item.EndX   = cursor + plainLength - 1;

                sb.Append($"[cyan1]{item.Shortcut}[/][grey70]:{item.Label}  [/]");

                cursor += plainLength + 2; // +2 for trailing two-space separator
            }
        }

        TotalRenderedLength = cursor;
        return sb.ToString();
    }

    /// <summary>
    /// Maps a raw mouse X position (absolute screen column) to a clickable hint.
    /// Because the bar is right-aligned, the content's left edge =
    /// windowWidth − rightMargin − TotalRenderedLength.
    /// </summary>
    public bool HandleClick(int x, int windowWidth, int rightMargin = 1) =>
        HandleClickAt(x - (windowWidth - rightMargin - TotalRenderedLength));
}
