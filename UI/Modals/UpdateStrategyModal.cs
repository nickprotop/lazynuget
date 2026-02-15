using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Core;
using SharpConsoleUI.Drawing;
using Spectre.Console;
using LazyNuGet.Models;
using LazyNuGet.UI.Utilities;
using HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment;

namespace LazyNuGet.UI.Modals;

/// <summary>
/// Modal for selecting update strategy when updating all packages
/// </summary>
public class UpdateStrategyModal : ModalBase<UpdateStrategy?>
{
    // Parameters
    private readonly int _totalOutdatedCount;
    private readonly string _projectName;

    // Controls
    private DropdownControl? _strategyDropdown;
    private MarkupControl? _descriptionLabel;
    private MarkupControl? _examplesLabel;
    private ButtonControl? _continueButton;
    private ButtonControl? _cancelButton;

    // Event handler references for cleanup (CRITICAL for memory leak prevention)
    private EventHandler<int>? _dropdownSelectionHandler;
    private EventHandler<ButtonControl>? _continueButtonClickHandler;
    private EventHandler<ButtonControl>? _cancelButtonClickHandler;

    private UpdateStrategyModal(int totalOutdatedCount, string projectName)
    {
        _totalOutdatedCount = totalOutdatedCount;
        _projectName = projectName;
    }

    public static Task<UpdateStrategy?> ShowAsync(
        ConsoleWindowSystem windowSystem,
        int totalOutdatedCount,
        string projectName,
        Window? parentWindow = null)
    {
        var instance = new UpdateStrategyModal(totalOutdatedCount, projectName);
        return ((ModalBase<UpdateStrategy?>)instance).ShowAsync(windowSystem, parentWindow);
    }

    protected override string GetTitle() => "Update Strategy";
    protected override (int width, int height) GetSize() => (90, 22);
    protected override BorderStyle GetBorderStyle() => BorderStyle.DoubleLine;
    protected override Color GetBorderColor() => ColorScheme.BorderColor;
    protected override UpdateStrategy? GetDefaultResult() => null;

    protected override void BuildContent()
    {
        // ── Header ────────────────────────────────────────────
        var header = Controls.Markup()
            .AddLine($"[{ColorScheme.PrimaryMarkup} bold]Choose Update Strategy[/]")
            .AddLine($"[{ColorScheme.SecondaryMarkup}]Select how to update {_totalOutdatedCount} outdated package(s) in {Markup.Escape(_projectName)}[/]")
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

        // ── Examples section ──────────────────────────────────
        _examplesLabel = Controls.Markup()
            .AddLine($"[{ColorScheme.PrimaryMarkup} bold]Examples:[/]")
            .AddLine($"[{ColorScheme.SecondaryMarkup}]• {UpdateStrategy.UpdateAllToLatest.GetDisplayName()}:[/] [{ColorScheme.MutedMarkup}]1.2.3 → 2.0.0[/] [green]✓[/]")
            .AddLine($"[{ColorScheme.SecondaryMarkup}]• {UpdateStrategy.MinorAndPatchOnly.GetDisplayName()}:[/] [{ColorScheme.MutedMarkup}]1.2.3 → 1.5.0[/] [green]✓[/]  [{ColorScheme.MutedMarkup}]|  1.2.3 → 2.0.0[/] [red]✗[/]")
            .AddLine($"[{ColorScheme.SecondaryMarkup}]• {UpdateStrategy.PatchOnly.GetDisplayName()}:[/] [{ColorScheme.MutedMarkup}]1.2.3 → 1.2.7[/] [green]✓[/]  [{ColorScheme.MutedMarkup}]|  1.2.3 → 1.3.0[/] [red]✗[/]")
            .WithMargin(2, 0, 2, 1)
            .Build();

        // ── Bottom separator ──────────────────────────────────
        var separator2 = Controls.RuleBuilder()
            .WithColor(ColorScheme.RuleColor)
            .StickyBottom()
            .Build();
        separator2.Margin = new Margin(2, 1, 2, 0);

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
        var helpLabel = Controls.Markup()
            .AddLine($"[{ColorScheme.MutedMarkup}]F1-F3:Quick Select  Enter:Continue  Esc:Cancel[/]")
            .WithMargin(2, 1, 2, 0)
            .StickyBottom()
            .Build();

        // ── Assemble modal ────────────────────────────────────
        Modal.AddControl(header);
        Modal.AddControl(separator1);
        Modal.AddControl(dropdownToolbar);
        Modal.AddControl(_descriptionLabel);
        Modal.AddControl(_examplesLabel);
        Modal.AddControl(separator2);
        Modal.AddControl(helpLabel);
        Modal.AddControl(buttonToolbar);
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
}
