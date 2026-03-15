using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drawing;
using SharpConsoleUI.Layout;
using LazyNuGet.Models;
using LazyNuGet.Services;
using LazyNuGet.UI.Components;
using LazyNuGet.UI.Utilities;
using SharpConsoleUI.Parsing;
using AsyncHelper = LazyNuGet.Services.AsyncHelper;

namespace LazyNuGet.UI.Modals;

public enum PackageDetailsAction { Close, Install }

public class PackageDetailsModal : ModalBase<PackageDetailsAction>
{
    private readonly NuGetPackage _package;
    private readonly NuGetClientService _nugetService;

    private MarkupControl? _loadingLabel;
    private TabControl? _tabControl;
    private ButtonControl? _installButton;
    private ButtonControl? _closeButton;

    private EventHandler<ButtonControl>? _installClickHandler;
    private EventHandler<ButtonControl>? _closeClickHandler;

    private PackageDetailsModal(NuGetPackage package, NuGetClientService nugetService)
    {
        _package = package;
        _nugetService = nugetService;
    }

    public static Task<PackageDetailsAction> ShowAsync(
        ConsoleWindowSystem windowSystem,
        NuGetPackage package,
        NuGetClientService nugetService,
        Window? parentWindow = null)
    {
        var instance = new PackageDetailsModal(package, nugetService);
        return ((ModalBase<PackageDetailsAction>)instance).ShowAsync(windowSystem, parentWindow);
    }

    protected override string GetTitle() => $"Package Details — {_package.Id}";
    protected override (int width, int height) GetSize() => (95, 30);
    protected override PackageDetailsAction GetDefaultResult() => PackageDetailsAction.Close;

    protected override void BuildContent()
    {
        // Header with badges
        var headerBuilder = Controls.Markup()
            .AddLine($"[cyan1 bold]{MarkupParser.Escape(_package.Id)}[/] [grey70]v{MarkupParser.Escape(_package.Version)}[/]");

        var badges = new List<string>();
        if (_package.IsVerified)
            badges.Add("[green]✓ Verified[/]");
        if (_package.IsDeprecated)
            badges.Add("[yellow]⚠ Deprecated[/]");
        if (_package.VulnerabilityCount > 0)
            badges.Add($"[red]⚠ {_package.VulnerabilityCount} Vulnerabilities[/]");
        if (badges.Any())
            headerBuilder.AddLine(string.Join("  ", badges));

        var header = headerBuilder.WithMargin(2, 1, 2, 0).Build();
        Modal.AddControl(header);

        // Loading label (replaced by tabs when data loads)
        _loadingLabel = Controls.Markup()
            .AddLine($"[{ColorScheme.MutedMarkup} italic]Loading details from NuGet.org...[/]")
            .WithMargin(2, 1, 2, 0)
            .Build();
        Modal.AddControl(_loadingLabel);

        // Hint bar
        var hintBar = Controls.Markup()
            .AddLine($"[{ColorScheme.MutedMarkup}]F1:Overview  F2:Deps  F3:Versions  F4:Security  |  I:Install  Esc:Close[/]")
            .WithMargin(2, 1, 2, 0)
            .StickyBottom()
            .Build();
        Modal.AddControl(hintBar);

        // Separator
        var separator = Controls.RuleBuilder()
            .WithColor(ColorScheme.RuleColor)
            .StickyBottom()
            .Build();
        separator.Margin = new Margin(2, 0, 2, 0);
        Modal.AddControl(separator);

        // Buttons
        _installButton = Controls.Button("[grey93]Install (I)[/]")
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.DarkGreen)
            .WithFocusedForegroundColor(Color.White)
            .Build();
        _installClickHandler = (s, e) => CloseWithResult(PackageDetailsAction.Install);
        _installButton.Click += _installClickHandler;

        _closeButton = Controls.Button("[grey93]Close (Esc)[/]")
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .Build();
        _closeClickHandler = (s, e) => CloseWithResult(PackageDetailsAction.Close);
        _closeButton.Click += _closeClickHandler;

        var buttonToolbar = Controls.HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .Column(col => col.Add(_installButton))
            .Column(col => col.Width(2))
            .Column(col => col.Add(_closeButton))
            .Build();
        buttonToolbar.Margin = new Margin(0, 0, 0, 0);
        Modal.AddControl(buttonToolbar);

        // Load catalog data async
        AsyncHelper.FireAndForget(async () =>
        {
            var source = _nugetService.Sources.FirstOrDefault(s => s.IsEnabled);
            if (source != null)
                await _nugetService.EnrichPackageWithCatalogDataAsync(source, _package);
            BuildAndShowTabs();
        }, ex =>
        {
            _loadingLabel?.SetContent(new List<string>
            {
                $"[{ColorScheme.ErrorMarkup}]Failed to load package details: {MarkupParser.Escape(ex.Message)}[/]"
            });
        });
    }

    private void BuildAndShowTabs()
    {
        // Hide loading label by clearing its content
        _loadingLabel?.SetContent(new List<string>());
        if (_loadingLabel != null)
            _loadingLabel.Visible = false;

        _tabControl = Controls.TabControl()
            .AddTab("Overview", InteractivePackageDetailsBuilder.BuildOverviewPanel(_package))
            .AddTab("Deps", InteractivePackageDetailsBuilder.BuildDependenciesPanel(_package))
            .AddTab("Versions", InteractivePackageDetailsBuilder.BuildVersionsPanel(_package))
            .AddTab("Security", InteractivePackageDetailsBuilder.BuildSecurityPanel(_package))
            .WithActiveTab(0)
            .WithBackgroundColor(ColorScheme.DetailsPanelBackground)
            .WithHeaderStyle(TabHeaderStyle.Separator)
            .WithMargin(2, 1, 2, 0)
            .WithName("detailsTabs")
            .Build();

        Modal.AddControl(_tabControl);
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape)
        {
            CloseWithResult(PackageDetailsAction.Close);
            e.Handled = true;
            return;
        }

        if (e.AlreadyHandled) { e.Handled = true; return; }

        if (e.KeyInfo.Key == ConsoleKey.F1 && _tabControl != null)
        {
            _tabControl.ActiveTabIndex = 0;
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.F2 && _tabControl != null)
        {
            _tabControl.ActiveTabIndex = 1;
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.F3 && _tabControl != null)
        {
            _tabControl.ActiveTabIndex = 2;
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.F4 && _tabControl != null)
        {
            _tabControl.ActiveTabIndex = 3;
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.I)
        {
            CloseWithResult(PackageDetailsAction.Install);
            e.Handled = true;
        }
    }

    protected override void OnCleanup()
    {
        if (_installButton != null && _installClickHandler != null)
            _installButton.Click -= _installClickHandler;
        if (_closeButton != null && _closeClickHandler != null)
            _closeButton.Click -= _closeClickHandler;
    }
}
