namespace LazyNuGet.Orchestrators;

/// <summary>
/// A single item in the help bar (shortcut:label pair).
/// </summary>
public class HelpBarItem
{
    public string Shortcut { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public Action? OnClick { get; set; }
    public int StartX { get; set; }
    public int EndX { get; set; }
}

/// <summary>
/// Owns both rendering and click hit-testing for the bottom help bar.
/// Items are added via a fluent API; Render() produces Spectre markup
/// and simultaneously records each item's plain-text column range so
/// HandleClick() can map mouse X → action without fragile position math.
/// </summary>
public class HelpBar
{
    private readonly List<HelpBarItem> _items = new();
    private readonly int _marginLeft;

    public HelpBar(int marginLeft = 0)
    {
        _marginLeft = marginLeft;
    }

    public void Clear()
    {
        _items.Clear();
    }

    public HelpBar Add(string shortcut, string label, Action? onClick = null)
    {
        _items.Add(new HelpBarItem
        {
            Shortcut = shortcut,
            Label = label,
            OnClick = onClick
        });
        return this;
    }

    /// <summary>
    /// Produces a Spectre markup string and records StartX/EndX for each item.
    /// Format: "[cyan1]shortcut[/][grey70]:label  [/]" per item.
    /// </summary>
    public string Render()
    {
        var markup = new System.Text.StringBuilder();
        int cursor = 0; // plain-text column position

        foreach (var item in _items)
        {
            // Plain text: "shortcut:label  "
            int plainLength = item.Shortcut.Length + 1 + item.Label.Length; // +1 for ':'
            item.StartX = cursor;
            item.EndX = cursor + plainLength - 1;

            markup.Append($"[cyan1]{item.Shortcut}[/][grey70]:{item.Label}  [/]");

            cursor += plainLength + 2; // +2 for trailing two-space separator
        }

        return markup.ToString();
    }

    /// <summary>
    /// Maps a raw mouse X position to an item and invokes its action.
    /// Returns true if a clickable item was found and invoked.
    /// </summary>
    public bool HandleClick(int x)
    {
        int col = x - _marginLeft;

        foreach (var item in _items)
        {
            if (item.OnClick != null && col >= item.StartX && col <= item.EndX)
            {
                item.OnClick.Invoke();
                return true;
            }
        }

        return false;
    }
}
