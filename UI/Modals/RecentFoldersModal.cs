using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Core;
using SharpConsoleUI.Drawing;
using Spectre.Console;
using LazyNuGet.UI.Utilities;
using HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment;
using VerticalAlignment = SharpConsoleUI.Layout.VerticalAlignment;

namespace LazyNuGet.UI.Modals;

/// <summary>
/// Modal for quickly switching between recently opened folders.
/// Returns the selected folder path, BrowseSentinel for "Browse...", or null on cancel.
/// </summary>
public class RecentFoldersModal : ModalBase<string?>
{
    /// <summary>Returned when the user selects "Browse..." to open the OS folder picker.</summary>
    public const string BrowseSentinel = "__browse__";

    private readonly List<string> _recentFolders;
    private readonly string? _currentFolder;

    // Controls
    private ListControl? _folderList;

    // Event handler references for cleanup
    private EventHandler<ListItem>? _itemActivatedHandler;

    private RecentFoldersModal(List<string> recentFolders, string? currentFolder)
    {
        _recentFolders = recentFolders;
        _currentFolder = currentFolder;
    }

    public static Task<string?> ShowAsync(
        ConsoleWindowSystem windowSystem,
        List<string> recentFolders,
        string? currentFolder = null,
        Window? parentWindow = null)
    {
        var instance = new RecentFoldersModal(recentFolders, currentFolder);
        return ((ModalBase<string?>)instance).ShowAsync(windowSystem, parentWindow);
    }

    protected override string GetTitle() => "Recent Folders";
    protected override (int width, int height) GetSize()
    {
        var desktopHeight = WindowSystem.DesktopDimensions.Height;
        // List items: recent folders + separator line + "Browse..." item
        var listItems = _recentFolders.Count + 1 + 1;
        // Chrome: top/bottom border (2) + header 2 lines + header margin (1)
        //       + list top margin (1) + hint line + hint margin (1)
        var chrome = 2 + 2 + 1 + 1 + 1 + 1 + 1;
        var contentHeight = listItems + chrome;
        var maxHeight = Math.Max(desktopHeight - 4, 12);
        return (80, Math.Min(contentHeight, maxHeight));
    }
    protected override BorderStyle GetBorderStyle() => BorderStyle.DoubleLine;
    protected override Color GetBorderColor() => ColorScheme.BorderColor;
    protected override string? GetDefaultResult() => null;

    protected override void BuildContent()
    {
        // ── Header ────────────────────────────────────────────
        var header = Controls.Markup()
            .AddLine($"[{ColorScheme.PrimaryMarkup} bold]Recent Folders[/]")
            .AddLine($"[{ColorScheme.MutedMarkup}]Select a folder or browse for a new one[/]")
            .WithMargin(2, 1, 2, 0)
            .Build();

        // ── Folder list ───────────────────────────────────────
        _folderList = Controls.List()
            .WithTitle(string.Empty)
            .SimpleMode()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithColors(Color.Grey93, ColorScheme.StatusBarBackground)
            .WithFocusedColors(Color.Grey93, ColorScheme.StatusBarBackground)
            .WithHighlightColors(Color.White, Color.Grey35)
            .WithMargin(2, 1, 2, 0)
            .Build();

        _itemActivatedHandler = (sender, item) =>
        {
            if (item?.Tag is string value)
                CloseWithResult(value);
        };
        _folderList.ItemActivated += _itemActivatedHandler;

        PopulateFolderList();

        // ── Hint line ─────────────────────────────────────────
        var hint = Controls.Markup($"[{ColorScheme.MutedMarkup}]Enter:Select  Esc:Cancel[/]")
            .WithMargin(2, 1, 2, 0)
            .StickyBottom()
            .Build();

        // ── Assemble ──────────────────────────────────────────
        Modal.AddControl(header);
        Modal.AddControl(_folderList);
        Modal.AddControl(hint);
    }

    protected override void SetInitialFocus()
    {
        _folderList?.SetFocus(true, FocusReason.Programmatic);
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.AlreadyHandled)
        {
            e.Handled = true;
            return;
        }

        if (e.KeyInfo.Key == ConsoleKey.Enter)
        {
            if (_folderList?.SelectedItem?.Tag is string value)
            {
                CloseWithResult(value);
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
        if (_folderList != null && _itemActivatedHandler != null)
            _folderList.ItemActivated -= _itemActivatedHandler;
    }

    private void PopulateFolderList()
    {
        foreach (var folder in _recentFolders)
        {
            var isCurrent = string.Equals(folder, _currentFolder, StringComparison.OrdinalIgnoreCase);
            var badge = isCurrent ? $" [{ColorScheme.SecondaryMarkup}]● current[/]" : "";
            var displayName = ShortenPath(folder);
            var item = new ListItem($"[{ColorScheme.PrimaryMarkup}]{Markup.Escape(displayName)}[/]{badge}");
            item.Tag = folder;
            _folderList?.AddItem(item);
        }

        // ── "Browse..." separator + item ──────────────────────
        var separator = new ListItem($"[{ColorScheme.MutedMarkup}]───────────────────────────────[/]");
        separator.Tag = null;
        _folderList?.AddItem(separator);

        var browseItem = new ListItem($"[{ColorScheme.SecondaryMarkup}]📂 Browse...[/]");
        browseItem.Tag = BrowseSentinel;
        _folderList?.AddItem(browseItem);
    }

    /// <summary>Shorten home-relative paths for display (e.g. ~/source/project).</summary>
    private static string ShortenPath(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home) && path.StartsWith(home, StringComparison.OrdinalIgnoreCase))
            return "~" + path[home.Length..];
        return path;
    }
}
