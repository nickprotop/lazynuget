using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Core;
using SharpConsoleUI.Drawing;
using Spectre.Console;
using LazyNuGet.Models;
using LazyNuGet.Services;
using LazyNuGet.UI.Utilities;
using NuGet.Versioning;
using HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment;
using VerticalAlignment = SharpConsoleUI.Layout.VerticalAlignment;

namespace LazyNuGet.UI.Modals;

/// <summary>
/// Modal for selecting update strategy when updating all packages
/// </summary>
public class UpdateStrategyModal : ModalBase<UpdateStrategy?>
{
    // Parameters
    private readonly List<PackageReference> _outdatedPackages;
    private readonly string _projectName;

    // Controls
    private DropdownControl? _strategyDropdown;
    private MarkupControl? _descriptionLabel;
    private ScrollablePanelControl? _packageScrollPanel;
    private MarkupControl? _includedPackagesContent;
    private MarkupControl? _skippedPackagesContent;
    private ButtonControl? _continueButton;
    private ButtonControl? _cancelButton;

    // Event handler references for cleanup (CRITICAL for memory leak prevention)
    private EventHandler<int>? _dropdownSelectionHandler;
    private EventHandler<ButtonControl>? _continueButtonClickHandler;
    private EventHandler<ButtonControl>? _cancelButtonClickHandler;

    private UpdateStrategyModal(List<PackageReference> outdatedPackages, string projectName)
    {
        _outdatedPackages = outdatedPackages;
        _projectName = projectName;
    }

    public static Task<UpdateStrategy?> ShowAsync(
        ConsoleWindowSystem windowSystem,
        List<PackageReference> outdatedPackages,
        string projectName,
        Window? parentWindow = null)
    {
        var instance = new UpdateStrategyModal(outdatedPackages, projectName);
        return ((ModalBase<UpdateStrategy?>)instance).ShowAsync(windowSystem, parentWindow);
    }

    protected override string GetTitle() => "Update Strategy";
    protected override (int width, int height) GetSize() => (90, 32);
    protected override BorderStyle GetBorderStyle() => BorderStyle.DoubleLine;
    protected override Color GetBorderColor() => ColorScheme.BorderColor;
    protected override UpdateStrategy? GetDefaultResult() => null;

    protected override void BuildContent()
    {
        // ── Header ────────────────────────────────────────────
        var header = Controls.Markup()
            .AddLine($"[{ColorScheme.PrimaryMarkup} bold]Choose Update Strategy[/]")
            .AddLine($"[{ColorScheme.SecondaryMarkup}]Select how to update {_outdatedPackages.Count} outdated package(s) in {Markup.Escape(_projectName)}[/]")
            .WithMargin(2, 2, 2, 0)
            .Build();

        // ── Separator ─────────────────────────────────────────
        var separator1 = Controls.RuleBuilder()
            .WithColor(ColorScheme.RuleColor)
            .Build();
        separator1.Margin = new Margin(2, 1, 2, 1);

        // ── Strategy dropdown ─────────────────────────────────
        var strategies = new[]
        {
            $"{UpdateStrategy.UpdateAllToLatest.GetDisplayName()} ({UpdateStrategy.UpdateAllToLatest.GetShortcutKey()})",
            $"{UpdateStrategy.MinorAndPatchOnly.GetDisplayName()} ({UpdateStrategy.MinorAndPatchOnly.GetShortcutKey()})",
            $"{UpdateStrategy.PatchOnly.GetDisplayName()} ({UpdateStrategy.PatchOnly.GetShortcutKey()})"
        };

        _strategyDropdown = new DropdownControl("Update Strategy: ", strategies)
        {
            SelectedIndex = 0
        };

        _dropdownSelectionHandler = (s, idx) => UpdateDescription();
        _strategyDropdown.SelectedIndexChanged += _dropdownSelectionHandler;

        var dropdownToolbar = Controls.Toolbar()
            .Add(_strategyDropdown)
            .WithSpacing(2)
            .WithMargin(2, 0, 2, 1)
            .Build();

        // ── Description label ─────────────────────────────────
        _descriptionLabel = Controls.Markup()
            .AddLine($"[{ColorScheme.SecondaryMarkup}]{UpdateStrategy.UpdateAllToLatest.GetDescription()}[/]")
            .WithMargin(2, 0, 2, 1)
            .Build();

        // ── Package preview section ──────────────────────────────
        // Create scrollable panel for package list
        _packageScrollPanel = Controls.ScrollablePanel()
            .WithVerticalScroll(ScrollMode.Scroll)
            .WithScrollbar(true)
            .WithMouseWheel(true)
            .WithAutoScroll(false)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithColors(Color.Grey93, ColorScheme.WindowBackground)
            .WithMargin(2, 0, 2, 1)
            .Build();

        // Create initial package content (will be updated when strategy changes)
        _includedPackagesContent = Controls.Markup().Build();
        _skippedPackagesContent = Controls.Markup().Build();

        // Initial population
        UpdatePackageDisplay();

        // ── Bottom separator ──────────────────────────────────
        var separator2 = Controls.RuleBuilder()
            .WithColor(ColorScheme.RuleColor)
            .StickyBottom()
            .Build();
        separator2.Margin = new Margin(2, 0, 2, 0);

        // ── Action buttons ────────────────────────────────────
        _continueButton = Controls.Button("[grey93]Continue (Enter)[/]")
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.DarkGreen)
            .WithFocusedForegroundColor(Color.White)
            .Build();
        _continueButtonClickHandler = (s, e) => HandleContinue();
        _continueButton.Click += _continueButtonClickHandler;

        _cancelButton = Controls.Button("[grey93]Cancel (Esc)[/]")
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .Build();
        _cancelButtonClickHandler = (s, e) => CloseWithResult(null);
        _cancelButton.Click += _cancelButtonClickHandler;

        var buttonToolbar = Controls.HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .Column(col => col.Add(_continueButton))
            .Column(col => col.Width(2))
            .Column(col => col.Add(_cancelButton))
            .Build();
        buttonToolbar.Margin = new Margin(0, 0, 0, 0);

        // ── Help label ────────────────────────────────────────
        var separator3 = Controls.RuleBuilder()
            .WithColor(ColorScheme.RuleColor)
            .StickyBottom()
            .Build();
        separator3.Margin = new Margin(2, 0, 2, 0);

        var helpLabel = Controls.Markup()
            .AddLine($"[{ColorScheme.MutedMarkup}]F1-F3:Quick Select  PgUp/PgDn:Scroll  Enter:Continue  Esc:Cancel[/]")
            .WithMargin(2, 0, 2, 0)
            .StickyBottom()
            .Build();

        // ── Assemble modal ────────────────────────────────────
        Modal.AddControl(header);
        Modal.AddControl(separator1);
        Modal.AddControl(dropdownToolbar);
        Modal.AddControl(_descriptionLabel);
        Modal.AddControl(_packageScrollPanel);
        Modal.AddControl(separator2);
        Modal.AddControl(buttonToolbar);
        Modal.AddControl(separator3);
        Modal.AddControl(helpLabel);
    }

    protected override void SetInitialFocus()
    {
        _strategyDropdown?.SetFocus(true, FocusReason.Programmatic);
    }

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.AlreadyHandled)
        {
            e.Handled = true;
            return;
        }

        // PageUp/PageDown for scrolling package list
        if (e.KeyInfo.Key == ConsoleKey.PageUp)
        {
            _packageScrollPanel?.ScrollVerticalBy(-10);
            e.Handled = true;
            return;
        }
        else if (e.KeyInfo.Key == ConsoleKey.PageDown)
        {
            _packageScrollPanel?.ScrollVerticalBy(10);
            e.Handled = true;
            return;
        }

        // F-key shortcuts
        if (e.KeyInfo.Key == ConsoleKey.F1)
        {
            if (_strategyDropdown != null) _strategyDropdown.SelectedIndex = 0;
            UpdateDescription();
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.F2)
        {
            if (_strategyDropdown != null) _strategyDropdown.SelectedIndex = 1;
            UpdateDescription();
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.F3)
        {
            if (_strategyDropdown != null) _strategyDropdown.SelectedIndex = 2;
            UpdateDescription();
            e.Handled = true;
        }
        // Enter to continue
        else if (e.KeyInfo.Key == ConsoleKey.Enter)
        {
            HandleContinue();
            e.Handled = true;
        }
        else
        {
            base.OnKeyPressed(sender, e); // Handle Escape and other default keys
        }
    }

    /// <summary>
    /// CRITICAL: Cleanup event handlers to prevent memory leaks
    /// </summary>
    protected override void OnCleanup()
    {
        if (_strategyDropdown != null && _dropdownSelectionHandler != null)
        {
            _strategyDropdown.SelectedIndexChanged -= _dropdownSelectionHandler;
        }

        if (_continueButton != null && _continueButtonClickHandler != null)
        {
            _continueButton.Click -= _continueButtonClickHandler;
        }

        if (_cancelButton != null && _cancelButtonClickHandler != null)
        {
            _cancelButton.Click -= _cancelButtonClickHandler;
        }
    }

    // ── Helper Methods ────────────────────────────────────────

    private void UpdateDescription()
    {
        if (_strategyDropdown == null || _descriptionLabel == null)
            return;

        var strategy = (UpdateStrategy)_strategyDropdown.SelectedIndex;
        _descriptionLabel.SetContent(new List<string>
        {
            $"[{ColorScheme.SecondaryMarkup}]{strategy.GetDescription()}[/]"
        });

        // Update package display
        UpdatePackageDisplay();
    }

    private void HandleContinue()
    {
        if (_strategyDropdown == null)
        {
            CloseWithResult(null);
            return;
        }

        var strategy = (UpdateStrategy)_strategyDropdown.SelectedIndex;
        CloseWithResult(strategy);
    }

    private void UpdatePackageDisplay()
    {
        if (_strategyDropdown == null || _packageScrollPanel == null) return;

        var strategy = (UpdateStrategy)_strategyDropdown.SelectedIndex;

        // Filter packages based on strategy
        var included = _outdatedPackages
            .Where(p => VersionComparisonService.IsUpdateAllowed(
                p.Version,
                p.LatestVersion!,
                strategy))
            .ToList();

        var skipped = _outdatedPackages.Except(included).ToList();

        // Clear previous content
        _packageScrollPanel.ClearContents();

        // Build "Will be updated" section
        var includedBuilder = Controls.Markup();
        if (included.Any())
        {
            includedBuilder.AddLine($"[{ColorScheme.PrimaryMarkup} bold]Will be updated ({included.Count}):[/]");
            foreach (var pkg in included)
            {
                var displayName = TruncatePackageName(pkg.Id, 40);
                includedBuilder.AddLine(
                    $"[green]✓[/] [{ColorScheme.PrimaryMarkup}]{Markup.Escape(displayName)}:[/] " +
                    $"[{ColorScheme.MutedMarkup}]{pkg.Version} → {pkg.LatestVersion}[/]");
            }
        }
        else
        {
            includedBuilder.AddLine($"[{ColorScheme.MutedMarkup} italic]No packages match this strategy[/]");
        }

        _includedPackagesContent = includedBuilder.WithMargin(0, 0, 0, 1).Build();
        _packageScrollPanel.AddControl(_includedPackagesContent);

        // Build "Skipped" section (only if there are skipped packages)
        if (skipped.Any())
        {
            var skippedBuilder = Controls.Markup();
            skippedBuilder.AddLine($"[{ColorScheme.SecondaryMarkup} bold]Skipped ({skipped.Count}):[/]");
            foreach (var pkg in skipped)
            {
                var displayName = TruncatePackageName(pkg.Id, 40);
                var reason = GetSkipReason(pkg, strategy);
                skippedBuilder.AddLine(
                    $"[red]✗[/] [{ColorScheme.MutedMarkup}]{Markup.Escape(displayName)}:[/] " +
                    $"[{ColorScheme.MutedMarkup}]{pkg.Version} → {pkg.LatestVersion}[/] " +
                    $"[grey50]({reason})[/]");
            }
            _skippedPackagesContent = skippedBuilder.WithMargin(0, 0, 0, 0).Build();
            _packageScrollPanel.AddControl(_skippedPackagesContent);
        }
        else
        {
            // All packages will be updated - make it clear
            var allUpdatesBuilder = Controls.Markup();
            allUpdatesBuilder.AddLine($"[green bold]✓ All {included.Count} package(s) will be updated[/]");
            var allUpdatesLabel = allUpdatesBuilder.WithMargin(0, 0, 0, 0).Build();
            _packageScrollPanel.AddControl(allUpdatesLabel);
        }

        _packageScrollPanel.ScrollToTop();
    }

    private string TruncatePackageName(string name, int maxLength)
    {
        if (name.Length <= maxLength) return name;
        return name.Substring(0, maxLength - 3) + "...";
    }

    private string GetSkipReason(PackageReference pkg, UpdateStrategy strategy)
    {
        if (!NuGetVersion.TryParse(pkg.Version, out var current) ||
            !NuGetVersion.TryParse(pkg.LatestVersion, out var latest))
            return "invalid version";

        if (latest.Major > current.Major)
            return "major version";
        if (latest.Minor > current.Minor)
            return "minor version";

        return "excluded by strategy";
    }
}
