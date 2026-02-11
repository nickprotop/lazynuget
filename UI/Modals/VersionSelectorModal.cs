using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Core;
using Spectre.Console;
using LazyNuGet.Models;
using LazyNuGet.UI.Utilities;
using HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment;

namespace LazyNuGet.UI.Modals;

/// <summary>
/// Modal for selecting a specific package version to install
/// </summary>
public static class VersionSelectorModal
{
    public static async Task<string?> ShowAsync(
        ConsoleWindowSystem windowSystem,
        PackageReference package,
        List<string> availableVersions,
        Window? parentWindow = null)
    {
        var tcs = new TaskCompletionSource<string?>();
        Window? modalWindow = null;

        try
        {
            modalWindow = new WindowBuilder(windowSystem)
                .WithTitle($"Select Version - {package.Id}")
                .WithSize(60, 20)
                .Centered()
                .AsModal()
                .Build();

            // Header
            var header = Controls.Markup()
                .AddLine($"[cyan1 bold]Select Version for {Markup.Escape(package.Id)}[/]")
                .AddLine($"[grey70]Current: {package.Version}[/]")
                .AddEmptyLine()
                .AddLine("[grey70]Available versions (newest first):[/]")
                .WithMargin(1, 1, 0, 0)
                .Build();
            modalWindow.AddControl(header);

            // Version list
            var versionList = Controls.List()
                .WithAlignment(HorizontalAlignment.Stretch)
                .WithVerticalAlignment(SharpConsoleUI.Layout.VerticalAlignment.Fill)
                .WithColors(ColorScheme.StatusBarBackground, Color.Grey93)
                .WithFocusedColors(ColorScheme.StatusBarBackground, Color.Grey93)
                .WithHighlightColors(Color.Grey35, Color.White)
                .SimpleMode()
                .Build();

            foreach (var version in availableVersions)
            {
                var isCurrent = string.Equals(version, package.Version, StringComparison.OrdinalIgnoreCase);
                var isLatest = version == availableVersions.FirstOrDefault();

                var label = version;
                if (isCurrent) label += " [grey50](current)[/]";
                if (isLatest) label += " [green](latest)[/]";

                versionList.AddItem(new ListItem(label) { Tag = version });
            }

            // Set initial selection to current version if found
            var currentIndex = availableVersions.FindIndex(v =>
                string.Equals(v, package.Version, StringComparison.OrdinalIgnoreCase));
            if (currentIndex >= 0)
                versionList.SelectedIndex = currentIndex;

            modalWindow.AddControl(versionList);

            // Help text
            var help = Controls.Markup()
                .AddLine("")
                .AddLine("[grey70]↑↓: Navigate  Enter: Select  Esc: Cancel[/]")
                .WithMargin(1, 0, 1, 1)
                .Build();
            modalWindow.AddControl(help);

            // Event handlers
            modalWindow.KeyPressed += (sender, e) =>
            {
                if (e.KeyInfo.Key == ConsoleKey.Escape)
                {
                    tcs.TrySetResult(null);
                    windowSystem.CloseWindow(modalWindow);
                    e.Handled = true;
                }
                else if (e.KeyInfo.Key == ConsoleKey.Enter)
                {
                    var selected = versionList.SelectedItem?.Tag as string;
                    tcs.TrySetResult(selected);
                    windowSystem.CloseWindow(modalWindow);
                    e.Handled = true;
                }
            };

            windowSystem.AddWindow(modalWindow);
            versionList.SetFocus(true, FocusReason.Programmatic);

            var result = await tcs.Task;
            return result;
        }
        finally
        {
            if (modalWindow != null)
            {
                windowSystem.CloseWindow(modalWindow);
            }
        }
    }
}
