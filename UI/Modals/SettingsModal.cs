using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;
using SharpConsoleUI.Drawing;
using SharpConsoleUI.Layout;
using Spectre.Console;
using LazyNuGet.Models;
using LazyNuGet.Services;
using LazyNuGet.UI.Utilities;
using HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment;
using VerticalAlignment = SharpConsoleUI.Layout.VerticalAlignment;

namespace LazyNuGet.UI.Modals;

/// <summary>
/// Settings modal with Sources and General sections
/// </summary>
public static class SettingsModal
{
    private enum Section
    {
        Sources,
        General
    }

    public static Task<bool> ShowAsync(
        ConsoleWindowSystem windowSystem,
        ConfigurationService configService,
        NuGetConfigService nugetConfigService,
        NuGetClientService nugetClientService,
        string projectDirectory,
        Window? parentWindow = null)
    {
        var tcs = new TaskCompletionSource<bool>();
        var settingsChanged = false;

        // State
        var currentSection = Section.Sources;

        // Controls
        ListControl? contentList = null;
        ButtonControl? sourcesTab = null;
        ButtonControl? generalTab = null;
        ButtonControl? addBtn = null;
        ButtonControl? toggleBtn = null;
        ButtonControl? deleteBtn = null;
        ButtonControl? clearBtn = null;

        // Build modal
        var modal = new WindowBuilder(windowSystem)
            .WithTitle("Settings")
            .Centered()
            .WithSize(100, 30)
            .AsModal()
            .Resizable(true)
            .Movable(true)
            .Minimizable(false)
            .WithColors(ColorScheme.WindowBackground, Color.Grey93)
            .WithBorderStyle(BorderStyle.DoubleLine)
            .WithBorderColor(ColorScheme.BorderColor)
            .Build();

        // Update tab button styling to reflect active section
        void UpdateTabs()
        {
            if (sourcesTab != null)
            {
                var isActive = currentSection == Section.Sources;
                sourcesTab.Text = isActive
                    ? $"[white bold]► Sources[/] [grey78](F1)[/]"
                    : $"[grey78]Sources[/] [grey62](F1)[/]";
                sourcesTab.BackgroundColor = isActive ? Color.DarkGreen : Color.Grey30;
                sourcesTab.FocusedBackgroundColor = isActive ? Color.Green : Color.Grey50;
            }

            if (generalTab != null)
            {
                var isActive = currentSection == Section.General;
                generalTab.Text = isActive
                    ? $"[white bold]► General[/] [grey78](F2)[/]"
                    : $"[grey78]General[/] [grey62](F2)[/]";
                generalTab.BackgroundColor = isActive ? Color.DarkGreen : Color.Grey30;
                generalTab.FocusedBackgroundColor = isActive ? Color.Green : Color.Grey50;
            }
        }

        // Show/hide action buttons based on current section
        void UpdateActionButtons()
        {
            var isSources = currentSection == Section.Sources;
            if (addBtn != null) addBtn.Visible = isSources;
            if (toggleBtn != null) toggleBtn.Visible = isSources;
            if (deleteBtn != null) deleteBtn.Visible = isSources;
            if (clearBtn != null) clearBtn.Visible = !isSources;
        }

        void SwitchSection(Section section)
        {
            currentSection = section;
            UpdateTabs();
            UpdateActionButtons();
            RefreshContent();
        }

        // Helper to refresh content based on current section
        void RefreshContent()
        {
            contentList?.ClearItems();

            if (currentSection == Section.Sources)
            {
                RefreshSourcesSection();
            }
            else
            {
                RefreshGeneralSection();
            }
        }

        void RefreshSourcesSection()
        {
            var settings = configService.Load();

            // Show NuGet.config sources
            foreach (var source in nugetClientService.Sources)
            {
                var enabledBadge = source.IsEnabled
                    ? "[green]Enabled[/]"
                    : "[grey50]Disabled[/]";
                var originLabel = source.Origin == NuGetSourceOrigin.NuGetConfig
                    ? "[grey50]NuGet.config[/]"
                    : $"[{ColorScheme.PrimaryMarkup}]Custom[/]";
                var authBadge = source.RequiresAuth ? " [yellow]Auth[/]" : "";

                var text = $"[cyan1]{Markup.Escape(source.Name)}[/]  {enabledBadge}  {originLabel}{authBadge}\n" +
                          $"[grey50]  {Markup.Escape(source.Url)}[/]";

                var item = new ListItem(text);
                item.Tag = source;
                contentList?.AddItem(item);
            }

            // Show custom sources from settings that might not be in the client yet
            if (settings.CustomSources.Any(cs => !nugetClientService.Sources.Any(s =>
                string.Equals(s.Name, cs.Name, StringComparison.OrdinalIgnoreCase))))
            {
                foreach (var custom in settings.CustomSources.Where(cs =>
                    !nugetClientService.Sources.Any(s =>
                        string.Equals(s.Name, cs.Name, StringComparison.OrdinalIgnoreCase))))
                {
                    var enabledBadge = custom.IsEnabled ? "[green]Enabled[/]" : "[grey50]Disabled[/]";
                    var text = $"[cyan1]{Markup.Escape(custom.Name)}[/]  {enabledBadge}  [{ColorScheme.PrimaryMarkup}]Custom[/]\n" +
                              $"[grey50]  {Markup.Escape(custom.Url)}[/]";
                    var item = new ListItem(text);
                    item.Tag = custom;
                    contentList?.AddItem(item);
                }
            }

            if (contentList?.Items.Count == 0)
            {
                contentList.AddItem(new ListItem($"[{ColorScheme.MutedMarkup}]No NuGet sources configured[/]"));
            }
        }

        void RefreshGeneralSection()
        {
            var settings = configService.Load();

            // App info
            contentList?.AddItem(new ListItem($"[{ColorScheme.PrimaryMarkup} bold]Application Info[/]"));
            contentList?.AddItem(new ListItem($"[grey70]  Version: 1.0.0[/]"));

            var configDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "LazyNuGet");
            contentList?.AddItem(new ListItem($"[grey70]  Config: {Markup.Escape(configDir)}[/]"));
            contentList?.AddItem(new ListItem(""));

            // NuGet.config file locations
            var configPaths = nugetConfigService.GetConfigFilePaths(projectDirectory);
            contentList?.AddItem(new ListItem($"[{ColorScheme.PrimaryMarkup} bold]Detected NuGet.config Files[/]"));
            if (configPaths.Any())
            {
                foreach (var path in configPaths)
                {
                    contentList?.AddItem(new ListItem($"[grey70]  {Markup.Escape(path)}[/]"));
                }
            }
            else
            {
                contentList?.AddItem(new ListItem($"[{ColorScheme.MutedMarkup}]  None found (using defaults)[/]"));
            }
            contentList?.AddItem(new ListItem(""));

            // Recent folders
            contentList?.AddItem(new ListItem($"[{ColorScheme.PrimaryMarkup} bold]Recent Folders[/] [{ColorScheme.MutedMarkup}]({settings.RecentFolders.Count})[/]"));
            if (settings.RecentFolders.Any())
            {
                foreach (var folder in settings.RecentFolders)
                {
                    contentList?.AddItem(new ListItem($"[grey70]  {Markup.Escape(folder)}[/]"));
                }
            }
            else
            {
                contentList?.AddItem(new ListItem($"[{ColorScheme.MutedMarkup}]  No recent folders[/]"));
            }
        }

        // Header
        var header = Controls.Markup()
            .AddLine($"[{ColorScheme.PrimaryMarkup} bold]Settings[/]")
            .AddLine($"[{ColorScheme.SecondaryMarkup}]Configure NuGet sources and preferences[/]")
            .WithMargin(2, 2, 2, 0)
            .Build();

        // ── Tab buttons ──────────────────────────────────────
        sourcesTab = Controls.Button($"[white bold]► Sources[/] [grey78](F1)[/]")
            .OnClick((s, e) => SwitchSection(Section.Sources))
            .WithMargin(1, 0, 0, 0)
            .WithBackgroundColor(Color.DarkGreen)
            .WithForegroundColor(Color.White)
            .WithFocusedBackgroundColor(Color.Green)
            .WithFocusedForegroundColor(Color.White)
            .Build();

        generalTab = Controls.Button($"[grey78]General[/] [grey62](F2)[/]")
            .OnClick((s, e) => SwitchSection(Section.General))
            .WithMargin(1, 0, 0, 0)
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .Build();

        var tabBarTop = Controls.Markup()
            .AddEmptyLine()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithBackgroundColor(Color.Grey15)
            .WithMargin(2, 1, 2, 0)
            .Build();

        var tabBar = Controls.Toolbar()
            .AddButton(sourcesTab)
            .AddButton(generalTab)
            .WithSpacing(2)
            .WithBackgroundColor(Color.Grey15)
            .WithMargin(2, 0, 2, 0)
            .Build();

        var tabBarBottom = Controls.Markup()
            .AddEmptyLine()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithBackgroundColor(Color.Grey15)
            .WithMargin(2, 0, 2, 0)
            .Build();

        // Separator between tabs and actions
        var separator1 = Controls.RuleBuilder()
            .WithColor(ColorScheme.RuleColor)
            .Build();
        separator1.Margin = new Margin(2, 0, 2, 0);

        // ── Action toolbar ───────────────────────────────────
        addBtn = Controls.Button($"[cyan1]Add Source[/] [grey78](A)[/]")
            .WithMargin(1, 0, 0, 0)
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .Build();

        toggleBtn = Controls.Button($"[cyan1]Toggle[/] [grey78](E)[/]")
            .WithMargin(1, 0, 0, 0)
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .Build();

        deleteBtn = Controls.Button($"[cyan1]Delete[/] [grey78](D)[/]")
            .WithMargin(1, 0, 0, 0)
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .Build();

        clearBtn = Controls.Button($"[cyan1]Clear Recent[/] [grey78](C)[/]")
            .WithMargin(1, 0, 0, 0)
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .Build();

        var actionBarTop = Controls.Markup()
            .AddEmptyLine()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithBackgroundColor(Color.Grey15)
            .WithMargin(2, 0, 2, 0)
            .Build();

        var actionToolbar = Controls.Toolbar()
            .AddButton(addBtn)
            .AddButton(toggleBtn)
            .AddButton(deleteBtn)
            .AddButton(clearBtn)
            .WithSpacing(2)
            .WithBackgroundColor(Color.Grey15)
            .WithMargin(2, 0, 2, 0)
            .Build();

        var actionBarBottom = Controls.Markup()
            .AddEmptyLine()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithBackgroundColor(Color.Grey15)
            .WithMargin(2, 0, 2, 0)
            .Build();

        // Content list
        contentList = Controls.List()
            .SimpleMode()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithColors(ColorScheme.StatusBarBackground, Color.Grey93)
            .WithFocusedColors(ColorScheme.StatusBarBackground, Color.Grey93)
            .WithHighlightColors(Color.Grey35, Color.White)
            .WithMargin(2, 1, 2, 1)
            .Build();

        // Bottom separator
        var separator2 = Controls.RuleBuilder()
            .WithColor(ColorScheme.RuleColor)
            .StickyBottom()
            .Build();
        separator2.Margin = new Margin(2, 0, 2, 0);

        // Close button
        var closeButton = Controls.Button("[grey93]Close (Esc)[/]")
            .WithMargin(1, 0, 0, 0)
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .Build();

        closeButton.Click += (s, e) => modal.Close();

        var buttonGrid = Controls.HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .Column(col => col.Add(closeButton))
            .Build();
        buttonGrid.Margin = new Margin(0, 0, 0, 0);

        // ── Assemble modal ───────────────────────────────────
        modal.AddControl(header);
        modal.AddControl(tabBarTop);
        modal.AddControl(tabBar);
        modal.AddControl(tabBarBottom);
        modal.AddControl(separator1);
        modal.AddControl(actionBarTop);
        modal.AddControl(actionToolbar);
        modal.AddControl(actionBarBottom);
        modal.AddControl(contentList);
        modal.AddControl(separator2);
        modal.AddControl(buttonGrid);

        // ── Action handlers ──────────────────────────────────
        async Task HandleAddSource()
        {
            var newSource = await AddSourceModal.ShowAsync(windowSystem, modal);
            if (newSource != null)
            {
                var settings = configService.Load();
                settings.CustomSources.Add(newSource);
                configService.Save(settings);
                settingsChanged = true;
                RefreshContent();
            }
        }

        void HandleToggleSource()
        {
            var selected = contentList?.SelectedItem?.Tag;
            if (selected is NuGetSource source)
            {
                var settings = configService.Load();
                var newEnabled = !source.IsEnabled;
                settings.SourceOverrides[source.Name] = newEnabled;
                configService.Save(settings);
                source.IsEnabled = newEnabled;
                settingsChanged = true;
                RefreshContent();
            }
            else if (selected is CustomNuGetSource custom)
            {
                var settings = configService.Load();
                var existing = settings.CustomSources.FirstOrDefault(cs =>
                    string.Equals(cs.Name, custom.Name, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.IsEnabled = !existing.IsEnabled;
                    configService.Save(settings);
                    settingsChanged = true;
                    RefreshContent();
                }
            }
        }

        async Task HandleDeleteSource()
        {
            var selected = contentList?.SelectedItem?.Tag;

            if (selected is NuGetSource source && source.Origin == NuGetSourceOrigin.NuGetConfig)
            {
                windowSystem.NotificationStateService.ShowNotification(
                    "Cannot Remove",
                    "NuGet.config sources cannot be removed from LazyNuGet. Edit NuGet.config directly.",
                    NotificationSeverity.Warning,
                    timeout: 4000,
                    parentWindow: modal);
                return;
            }

            string? nameToDelete = null;
            if (selected is NuGetSource lazySource && lazySource.Origin == NuGetSourceOrigin.LazyNuGetSettings)
            {
                nameToDelete = lazySource.Name;
            }
            else if (selected is CustomNuGetSource custom)
            {
                nameToDelete = custom.Name;
            }

            if (nameToDelete == null) return;

            var confirm = await ConfirmationModal.ShowAsync(windowSystem,
                "Remove Source",
                $"Remove custom source '{nameToDelete}'?",
                "Remove", "Cancel", modal);

            if (confirm)
            {
                var settings = configService.Load();
                settings.CustomSources.RemoveAll(cs =>
                    string.Equals(cs.Name, nameToDelete, StringComparison.OrdinalIgnoreCase));
                settings.SourceOverrides.Remove(nameToDelete);
                configService.Save(settings);
                settingsChanged = true;
                RefreshContent();
            }
        }

        async Task HandleClearRecent()
        {
            var confirm = await ConfirmationModal.ShowAsync(windowSystem,
                "Clear Recent Folders",
                "Clear all recent folder history?",
                "Clear", "Cancel", modal);

            if (confirm)
            {
                var settings = configService.Load();
                settings.RecentFolders.Clear();
                configService.Save(settings);
                RefreshContent();
            }
        }

        // Wire button clicks to action handlers
        addBtn.Click += (s, e) => _ = HandleAddSource();
        toggleBtn.Click += (s, e) => HandleToggleSource();
        deleteBtn.Click += (s, e) => _ = HandleDeleteSource();
        clearBtn.Click += (s, e) => _ = HandleClearRecent();

        // ── Keyboard handling ────────────────────────────────
        modal.KeyPressed += (s, e) =>
        {
            if (e.KeyInfo.Key == ConsoleKey.Escape)
            {
                modal.Close();
                e.Handled = true;
            }
            else if (e.KeyInfo.Key == ConsoleKey.F1)
            {
                SwitchSection(Section.Sources);
                e.Handled = true;
            }
            else if (e.KeyInfo.Key == ConsoleKey.F2)
            {
                SwitchSection(Section.General);
                e.Handled = true;
            }
            else if (e.KeyInfo.Key == ConsoleKey.A && currentSection == Section.Sources)
            {
                _ = HandleAddSource();
                e.Handled = true;
            }
            else if (e.KeyInfo.Key == ConsoleKey.E && currentSection == Section.Sources)
            {
                HandleToggleSource();
                e.Handled = true;
            }
            else if (e.KeyInfo.Key == ConsoleKey.D && currentSection == Section.Sources)
            {
                _ = HandleDeleteSource();
                e.Handled = true;
            }
            else if (e.KeyInfo.Key == ConsoleKey.C && currentSection == Section.General)
            {
                _ = HandleClearRecent();
                e.Handled = true;
            }
        };

        modal.OnClosed += (s, e) => tcs.TrySetResult(settingsChanged);

        // Show modal
        windowSystem.AddWindow(modal);
        windowSystem.SetActiveWindow(modal);

        // Initial load
        UpdateActionButtons();
        RefreshContent();

        return tcs.Task;
    }
}
