using System.Text;

namespace LazyNuGet.Orchestrators;

/// <summary>
/// Owns both rendering and click hit-testing for the bottom help bar.
/// Items are added via a fluent API; Render() produces Spectre markup
/// and simultaneously records each item's plain-text column range so
/// HandleClick() can map mouse X → action without fragile position math.
/// </summary>
public class HelpBar : ClickableBar
{
    private readonly int _marginLeft;

    public HelpBar(int marginLeft = 0)
    {
        _marginLeft = marginLeft;
    }

    public HelpBar Add(string shortcut, string label, Action? onClick = null)
    {
        _items.Add(new BarItem { Shortcut = shortcut, Label = label, OnClick = onClick });
        return this;
    }

    /// <summary>
    /// Produces a Spectre markup string and records StartX/EndX for each item.
    /// Format: "[cyan1]shortcut[/][grey70]:label  [/]" per item.
    /// </summary>
    public override string Render()
    {
        var sb = new StringBuilder();
        int cursor = 0;

        foreach (var item in _items)
        {
            int plainLength = item.Shortcut.Length + 1 + item.Label.Length; // +1 for ':'
            item.StartX = cursor;
            item.EndX   = cursor + plainLength - 1;

            sb.Append($"[cyan1]{item.Shortcut}[/][grey70]:{item.Label}  [/]");

            cursor += plainLength + 2; // +2 for trailing two-space separator
        }

        TotalRenderedLength = cursor;
        return sb.ToString();
    }

    /// <summary>
    /// Maps a raw mouse X position to an item and invokes its action.
    /// Returns true if a clickable item was found and invoked.
    /// </summary>
    public bool HandleClick(int x) => HandleClickAt(x - _marginLeft);
}
