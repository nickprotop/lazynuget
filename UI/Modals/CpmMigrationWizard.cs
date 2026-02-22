using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;
using SharpConsoleUI.Drawing;
using SharpConsoleUI.Events;
using SharpConsoleUI.Layout;
using Spectre.Console;
using LazyNuGet.Services;
using LazyNuGet.UI.Utilities;
using AsyncHelper = LazyNuGet.Services.AsyncHelper;
using HorizontalAlignment = SharpConsoleUI.Layout.HorizontalAlignment;
using VerticalAlignment = SharpConsoleUI.Layout.VerticalAlignment;

namespace LazyNuGet.UI.Modals;

/// <summary>
/// 3-tab wizard that walks the user through Central Package Management migration:
/// Analysis → Migration → Results.
/// </summary>
public class CpmMigrationWizard : ModalBase<CpmMigrationResult?>
{
    private enum WizardState { Analyzing, ScanDone, Migrating, Done }

    private readonly string _folderPath;
    private readonly CpmMigrationService _service = new();

    // State
    private WizardState _state = WizardState.Analyzing;
    private CpmAnalysisResult? _analysis;
    private CpmMigrationResult? _migrationResult;
    private CancellationTokenSource? _migrationCts;
    private int _maxAllowedTab = 0;

    // Controls
    private TabControl?         _tabControl;
    private MarkupControl?      _analysisContent;
    private MarkupControl?      _migrationStatus;
    private ProgressBarControl? _progressBar;
    private MarkupControl?      _migrationLog;
    private MarkupControl?      _resultsContent;
    private ButtonControl?      _migrateBtn;
    private ButtonControl?      _cancelBtn;
    private ButtonControl?      _viewResultsBtn;
    private ButtonControl?      _closeBtn;

    // Log
    private readonly List<string> _logLines = new();
    private readonly object       _logLock  = new();
    private DateTime              _startTime;

    // Event handlers for cleanup
    private EventHandler<ButtonControl>?        _migrateBtnClick;
    private EventHandler<ButtonControl>?        _cancelBtnClick;
    private EventHandler<ButtonControl>?        _viewResultsBtnClick;
    private EventHandler<ButtonControl>?        _closeBtnClick;
    private EventHandler<TabChangingEventArgs>? _tabChanging;

    private CpmMigrationWizard(string folderPath)
    {
        _folderPath = folderPath;
    }

    public static Task<CpmMigrationResult?> ShowAsync(
        ConsoleWindowSystem windowSystem,
        string folderPath,
        Window? parentWindow = null)
    {
        var instance = new CpmMigrationWizard(folderPath);
        return ((ModalBase<CpmMigrationResult?>)instance).ShowAsync(windowSystem, parentWindow);
    }

    protected override string GetTitle() => "Migrate to Central Package Management";
    protected override (int width, int height) GetSize() => (90, 32);
    protected override bool GetResizable() => true;
    protected override bool GetMovable()   => true;
    protected override CpmMigrationResult? GetDefaultResult() => null;

    protected override void BuildContent()
    {
        // ── Header ────────────────────────────────────────────────────────────
        var header = Controls.Markup()
            .AddLine($"[{ColorScheme.PrimaryMarkup} bold]Migrate to Central Package Management[/]")
            .AddLine($"[{ColorScheme.SecondaryMarkup}]Centralize all package versions into Directory.Packages.props[/]")
            .WithMargin(2, 1, 2, 0)
            .Build();

        // ── Tab content panels ────────────────────────────────────────────────

        // Tab 0 — Analysis
        _analysisContent = Controls.Markup()
            .AddLine($"[{ColorScheme.MutedMarkup}]⏳ Scanning projects...[/]")
            .WithMargin(1, 1, 1, 0)
            .Build();

        var analysisPanel = Controls.ScrollablePanel()
            .WithVerticalScroll(ScrollMode.Scroll)
            .WithScrollbar(true)
            .WithMouseWheel(true)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithColors(Color.Grey93, ColorScheme.WindowBackground)
            .WithMargin(1, 0, 1, 0)
            .Build();
        analysisPanel.AddControl(_analysisContent);

        // Tab 1 — Migration
        _migrationStatus = Controls.Markup()
            .AddLine($"[{ColorScheme.MutedMarkup}]Waiting...[/]")
            .WithMargin(1, 1, 1, 0)
            .Build();

        _progressBar = Controls.ProgressBar()
            .Indeterminate(true)
            .WithAnimationInterval(100)
            .ShowPercentage(false)
            .WithMargin(2, 0, 2, 0)
            .Build();

        _migrationLog = Controls.Markup()
            .AddLine($"[{ColorScheme.MutedMarkup}]No output yet.[/]")
            .WithMargin(1, 0, 1, 0)
            .Build();

        var migrationPanel = Controls.ScrollablePanel()
            .WithVerticalScroll(ScrollMode.Scroll)
            .WithScrollbar(true)
            .WithMouseWheel(true)
            .WithAutoScroll(true)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithColors(Color.Grey93, ColorScheme.WindowBackground)
            .WithMargin(1, 0, 1, 0)
            .Build();
        migrationPanel.AddControl(_migrationStatus);
        migrationPanel.AddControl(_progressBar);
        migrationPanel.AddControl(_migrationLog);

        // Tab 2 — Results
        _resultsContent = Controls.Markup()
            .AddLine($"[{ColorScheme.MutedMarkup}]Migration has not run yet.[/]")
            .WithMargin(1, 1, 1, 0)
            .Build();

        var resultsPanel = Controls.ScrollablePanel()
            .WithVerticalScroll(ScrollMode.Scroll)
            .WithScrollbar(true)
            .WithMouseWheel(true)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithColors(Color.Grey93, ColorScheme.WindowBackground)
            .WithMargin(1, 0, 1, 0)
            .Build();
        resultsPanel.AddControl(_resultsContent);

        // ── TabControl ────────────────────────────────────────────────────────
        _tabControl = Controls.TabControl()
            .AddTab("📋 Analysis",  analysisPanel)
            .AddTab("⚙ Migration",  migrationPanel)
            .AddTab("✓ Results",    resultsPanel)
            .WithActiveTab(0)
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithHeaderStyle(TabHeaderStyle.Separator)
            .WithBackgroundColor(ColorScheme.WindowBackground)
            .WithMargin(0, 1, 0, 0)
            .Build();

        // Prevent jumping ahead to locked tabs
        _tabChanging = OnTabChanging;
        _tabControl.TabChanging += _tabChanging;

        // ── Separator + buttons ───────────────────────────────────────────────
        var separator = Controls.RuleBuilder()
            .WithColor(ColorScheme.RuleColor)
            .StickyBottom()
            .Build();
        separator.Margin = new Margin(2, 0, 2, 0);

        _migrateBtn = Controls.Button("[cyan1]Migrate[/] [grey78](Enter)[/]")
            .Disabled()
            .WithMargin(1, 0, 0, 0)
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .Build();

        _cancelBtn = Controls.Button("[grey93]Cancel[/] [grey78](Esc)[/]")
            .WithMargin(1, 0, 0, 0)
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .Build();

        _viewResultsBtn = Controls.Button("[cyan1]View Results →[/]")
            .WithMargin(1, 0, 0, 0)
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .Build();
        _viewResultsBtn.Visible = false;

        _closeBtn = Controls.Button("[grey93]Close[/] [grey78](Esc)[/]")
            .WithMargin(1, 0, 0, 0)
            .WithBackgroundColor(Color.Grey30)
            .WithForegroundColor(Color.Grey93)
            .WithFocusedBackgroundColor(Color.Grey50)
            .WithFocusedForegroundColor(Color.White)
            .Build();
        _closeBtn.Visible = false;

        var buttonToolbar = Controls.Toolbar()
            .AddButton(_migrateBtn)
            .AddButton(_cancelBtn)
            .AddButton(_viewResultsBtn)
            .AddButton(_closeBtn)
            .WithSpacing(2)
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .WithBackgroundColor(ColorScheme.WindowBackground)
            .WithMargin(0, 0, 0, 0)
            .Build();

        // ── Wire click handlers ───────────────────────────────────────────────
        _migrateBtnClick     = (_, _) => AsyncHelper.FireAndForget(
            StartMigrationAsync,
            ex => AppendLog($"[{ColorScheme.ErrorMarkup}]✗ {Markup.Escape(ex.Message)}[/]"));

        _cancelBtnClick      = (_, _) => HandleCancelOrClose();
        _viewResultsBtnClick = (_, _) => SwitchToResultsTab();
        _closeBtnClick       = (_, _) => CloseWizard();

        _migrateBtn.Click     += _migrateBtnClick;
        _cancelBtn.Click      += _cancelBtnClick;
        _viewResultsBtn.Click += _viewResultsBtnClick;
        _closeBtn.Click       += _closeBtnClick;

        // ── Assemble modal ────────────────────────────────────────────────────
        Modal.AddControl(header);
        Modal.AddControl(_tabControl);
        Modal.AddControl(separator);
        Modal.AddControl(buttonToolbar);

        // Kick off analysis in the background
        AsyncHelper.FireAndForget(
            RunAnalysisAsync,
            ex => UpdateAnalysisContent(
                $"[{ColorScheme.ErrorMarkup}]✗ Analysis failed: {Markup.Escape(ex.Message)}[/]"));
    }

    // ── Analysis phase ────────────────────────────────────────────────────────

    private async Task RunAnalysisAsync()
    {
        try
        {
            UpdateAnalysisContent($"[{ColorScheme.MutedMarkup}]⏳ Scanning projects in:[/]\n" +
                $"[grey50]{Markup.Escape(_folderPath)}[/]\n\n" +
                $"[{ColorScheme.MutedMarkup}]Please wait...[/]");

            _analysis = await _service.AnalyzeAsync(_folderPath);

            _state = WizardState.ScanDone;
            UpdateAnalysisContent(BuildAnalysisSummary(_analysis));
            UpdateButtons();
            Modal?.Invalidate(true);
        }
        catch (Exception ex)
        {
            UpdateAnalysisContent(
                $"[{ColorScheme.ErrorMarkup}]✗ Analysis error: {Markup.Escape(ex.Message)}[/]");
        }
    }

    private void UpdateAnalysisContent(string markup)
    {
        _analysisContent?.SetContent(new List<string> { markup });
        Modal?.Invalidate(true);
    }

    private string BuildAnalysisSummary(CpmAnalysisResult analysis)
    {
        var lines = new List<string>
        {
            $"[{ColorScheme.PrimaryMarkup}]Folder:[/] [grey70]{Markup.Escape(_folderPath)}[/]",
            ""
        };

        if (analysis.ProjectsToMigrate.Count == 0)
        {
            lines.Add($"[{ColorScheme.MutedMarkup}]No projects to migrate.[/]");
            lines.Add($"[{ColorScheme.MutedMarkup}]All projects are already using CPM or packages.config.[/]");
        }
        else
        {
            lines.Add($"[green]{analysis.ProjectsToMigrate.Count} project(s)[/] ready to migrate");
            lines.Add($"[cyan1]{analysis.ResolvedVersions.Count} package(s)[/] will be centralized");

            if (analysis.VersionConflictsCount > 0)
                lines.Add($"[yellow]{analysis.VersionConflictsCount} version conflict(s)[/] → highest version wins");

            lines.Add("");
            lines.Add($"[{ColorScheme.PrimaryMarkup}]Projects:[/]");

            foreach (var p in analysis.ProjectsToMigrate)
            {
                var pkgCount = p.InlineRefs.Count;
                lines.Add($"  [green]●[/] [{ColorScheme.PrimaryMarkup}]{Markup.Escape(p.Name)}[/]" +
                    $"  [grey50]({pkgCount} package{(pkgCount == 1 ? "" : "s")})[/]");
            }
        }

        if (analysis.ProjectsSkipped.Count > 0)
        {
            lines.Add("");
            lines.Add($"[{ColorScheme.PrimaryMarkup}]Skipped:[/]");
            foreach (var p in analysis.ProjectsSkipped)
                lines.Add($"  [grey50]○ {Markup.Escape(p.Name)}[/]  [grey35]{Markup.Escape(p.SkipReason ?? "")}[/]");
        }

        return string.Join("\n", lines);
    }

    // ── Migration phase ───────────────────────────────────────────────────────

    private async Task StartMigrationAsync()
    {
        if (_analysis == null) return;

        _state = WizardState.Migrating;
        _maxAllowedTab = 1;
        _tabControl!.ActiveTabIndex = 1;
        _migrationCts = new CancellationTokenSource();
        _startTime    = DateTime.Now;
        _logLines.Clear();

        UpdateButtons();

        _migrationStatus?.SetContent(new List<string>
        {
            $"[{ColorScheme.PrimaryMarkup} bold]Migrating {_analysis.ProjectsToMigrate.Count} project(s)...[/]",
            $"[{ColorScheme.SecondaryMarkup}]{Markup.Escape(_folderPath)}[/]"
        });

        if (_progressBar != null)
        {
            _progressBar.IsIndeterminate = true;
            _progressBar.Visible = true;
        }

        Modal?.Invalidate(true);

        var progress = new Progress<string>(line =>
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            AppendLog($"[grey50]{(DateTime.Now - _startTime).TotalSeconds:F1}s[/] [grey70]{Markup.Escape(line)}[/]");
        });

        _migrationResult = await _service.MigrateAsync(
            _folderPath, _analysis, progress, _migrationCts.Token);

        OnMigrationComplete();
    }

    private void AppendLog(string line)
    {
        lock (_logLock)
        {
            _logLines.Add(line);
            if (_logLines.Count > 500)
                _logLines.RemoveAt(0);

            _migrationLog?.SetContent(new List<string> { string.Join("\n", _logLines) });
            Modal?.Invalidate(true);
        }
    }

    private void OnMigrationComplete()
    {
        _state = WizardState.Done;
        _maxAllowedTab = 2;

        var result = _migrationResult!;
        var elapsed = DateTime.Now - _startTime;

        if (_progressBar != null)
        {
            _progressBar.IsIndeterminate = false;
            _progressBar.Value    = 100;
            _progressBar.MaxValue = 100;
        }

        if (result.Success)
        {
            _migrationStatus?.SetContent(new List<string>
            {
                $"[{ColorScheme.SuccessMarkup} bold]✓ Migration complete in {elapsed.TotalSeconds:F1}s[/]",
                $"[{ColorScheme.MutedMarkup}]Click 'View Results' to see the summary.[/]"
            });

            lock (_logLock)
            {
                _logLines.Add($"[{ColorScheme.SuccessMarkup} bold]✓ Done in {elapsed.TotalSeconds:F1}s[/]");
                _migrationLog?.SetContent(new List<string> { string.Join("\n", _logLines) });
            }
        }
        else
        {
            _migrationStatus?.SetContent(new List<string>
            {
                $"[{ColorScheme.ErrorMarkup} bold]✗ Migration failed[/]",
                $"[{ColorScheme.MutedMarkup}]{Markup.Escape(result.Error ?? "Unknown error")}[/]"
            });

            lock (_logLock)
            {
                _logLines.Add($"[{ColorScheme.ErrorMarkup} bold]✗ {Markup.Escape(result.Error ?? "Unknown error")}[/]");
                _migrationLog?.SetContent(new List<string> { string.Join("\n", _logLines) });
            }
        }

        // Build results tab content
        _resultsContent?.SetContent(new List<string> { BuildResultsSummary(result) });

        UpdateButtons();
        Modal?.Invalidate(true);
    }

    private string BuildResultsSummary(CpmMigrationResult result)
    {
        if (!result.Success)
        {
            var rollbackLines = new List<string>
            {
                $"[{ColorScheme.ErrorMarkup} bold]✗ Migration failed[/]",
                "",
                $"[{ColorScheme.ErrorMarkup}]{Markup.Escape(result.Error ?? "Unknown error")}[/]",
                "",
                $"[{ColorScheme.MutedMarkup}]All changes have been rolled back.[/]",
                $"[{ColorScheme.MutedMarkup}]Backup files (.csproj.bak) remain for manual inspection.[/]"
            };
            return string.Join("\n", rollbackLines);
        }

        var lines = new List<string>
        {
            $"[{ColorScheme.SuccessMarkup} bold]✓ Migration complete![/]",
            "",
            $"  [cyan1]{result.ProjectsMigrated}[/] project(s) migrated",
            $"  [cyan1]{result.PackagesCentralized}[/] package(s) centralized"
        };

        if (result.VersionConflictsResolved > 0)
            lines.Add($"  [yellow]{result.VersionConflictsResolved}[/] version conflict(s) resolved (highest version used)");

        if (result.PropsFilePath != null)
        {
            lines.Add("");
            lines.Add($"  [{ColorScheme.PrimaryMarkup}]Directory.Packages.props:[/]");
            lines.Add($"  [grey70]{Markup.Escape(result.PropsFilePath)}[/]");
        }

        if (result.ModifiedProjectPaths.Count > 0)
        {
            lines.Add("");
            lines.Add($"  [{ColorScheme.PrimaryMarkup}]Backup files created (.csproj.bak):[/]");
            foreach (var path in result.ModifiedProjectPaths)
                lines.Add($"  [grey70]{Markup.Escape(Path.GetFileName(path + ".bak"))}[/]");
        }

        return string.Join("\n", lines);
    }

    // ── Navigation helpers ────────────────────────────────────────────────────

    private void SwitchToResultsTab()
    {
        if (_tabControl == null) return;
        _tabControl.ActiveTabIndex = 2;

        // Show only Close button on results tab
        if (_viewResultsBtn != null) _viewResultsBtn.Visible = false;
        Modal?.Invalidate(true);
    }

    private void HandleCancelOrClose()
    {
        switch (_state)
        {
            case WizardState.Analyzing:
            case WizardState.ScanDone:
                CloseWithResult(null);
                break;

            case WizardState.Migrating:
                _migrationCts?.Cancel();
                break;

            case WizardState.Done:
                CloseWizard();
                break;
        }
    }

    private void CloseWizard()
    {
        CloseWithResult(_migrationResult);
    }

    private void UpdateButtons()
    {
        switch (_state)
        {
            case WizardState.Analyzing:
                if (_migrateBtn    != null) { _migrateBtn.Visible = true;  _migrateBtn.IsEnabled = false; }
                if (_cancelBtn     != null) { _cancelBtn.Visible  = true; }
                if (_viewResultsBtn!= null)   _viewResultsBtn.Visible = false;
                if (_closeBtn      != null)   _closeBtn.Visible   = false;
                break;

            case WizardState.ScanDone:
                if (_migrateBtn    != null)
                {
                    _migrateBtn.Visible    = true;
                    _migrateBtn.IsEnabled  = _analysis?.ProjectsToMigrate.Count > 0;
                }
                if (_cancelBtn     != null)   _cancelBtn.Visible  = true;
                if (_viewResultsBtn!= null)   _viewResultsBtn.Visible = false;
                if (_closeBtn      != null)   _closeBtn.Visible   = false;
                break;

            case WizardState.Migrating:
                if (_migrateBtn    != null)   _migrateBtn.Visible    = false;
                if (_cancelBtn     != null)   _cancelBtn.Visible     = true;
                if (_viewResultsBtn!= null)   _viewResultsBtn.Visible = false;
                if (_closeBtn      != null)   _closeBtn.Visible      = false;
                break;

            case WizardState.Done:
                if (_migrateBtn    != null)   _migrateBtn.Visible    = false;
                if (_cancelBtn     != null)   _cancelBtn.Visible     = false;
                if (_viewResultsBtn!= null)   _viewResultsBtn.Visible = true;
                if (_closeBtn      != null)   _closeBtn.Visible      = true;
                break;
        }
    }

    // ── Tab lock guard ────────────────────────────────────────────────────────

    private void OnTabChanging(object? sender, TabChangingEventArgs e)
    {
        if (e.NewIndex > _maxAllowedTab)
            e.Cancel = true;
    }

    // ── Keyboard handling ─────────────────────────────────────────────────────

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape)
        {
            HandleCancelOrClose();
            e.Handled = true;
        }
        else if (e.KeyInfo.Key == ConsoleKey.Enter
            && _state == WizardState.ScanDone
            && _analysis?.ProjectsToMigrate.Count > 0)
        {
            AsyncHelper.FireAndForget(
                StartMigrationAsync,
                ex => AppendLog($"[{ColorScheme.ErrorMarkup}]✗ {Markup.Escape(ex.Message)}[/]"));
            e.Handled = true;
        }
        else
        {
            base.OnKeyPressed(sender, e);
        }
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    protected override void OnCleanup()
    {
        _migrationCts?.Cancel();
        _migrationCts?.Dispose();

        if (_tabControl != null && _tabChanging != null)
            _tabControl.TabChanging -= _tabChanging;

        if (_migrateBtn     != null && _migrateBtnClick     != null) _migrateBtn.Click     -= _migrateBtnClick;
        if (_cancelBtn      != null && _cancelBtnClick      != null) _cancelBtn.Click      -= _cancelBtnClick;
        if (_viewResultsBtn != null && _viewResultsBtnClick != null) _viewResultsBtn.Click -= _viewResultsBtnClick;
        if (_closeBtn       != null && _closeBtnClick       != null) _closeBtn.Click       -= _closeBtnClick;
    }
}
