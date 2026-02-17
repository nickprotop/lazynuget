using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drawing;
using Spectre.Console;
using LazyNuGet.Models;
using LazyNuGet.Services;
using LazyNuGet.UI.Components;
using LazyNuGet.UI.Utilities;

namespace LazyNuGet.Orchestrators;

/// <summary>
/// Manages package detail tab switching, loading animation, and detail fetching.
/// Extracted from LazyNuGetWindow to reduce god-class complexity.
/// </summary>
public class PackageDetailsController : IDisposable
{
    private NuGetClientService _nugetService;
    private readonly ErrorHandler? _errorHandler;
    private readonly Window? _window;
    private readonly ScrollablePanelControl? _detailsPanel;
    private readonly Action<List<IWindowControl>> _updateDetailsPanel;
    private readonly Func<ProjectInfo?> _getSelectedProject;
    private readonly Func<PackageReference, Task> _onUpdatePackage;
    private readonly Func<PackageReference, Task> _onChangeVersion;
    private readonly Func<PackageReference, Task> _onRemovePackage;
    private readonly Func<ProjectInfo, PackageReference?, Task> _onShowDependencyTree;

    private PackageDetailTab _currentDetailTab = PackageDetailTab.Overview;
    private PackageReference? _cachedPackageRef;
    private NuGetPackage? _cachedNuGetData;
    private TabControl? _tabControl;
    private CancellationTokenSource? _packageLoadCancellation;
    private System.Threading.Timer? _loadingAnimationTimer;
    private int _spinnerFrame;
    private MarkupControl? _loadingMessageControl;
    private ProgressBarControl? _loadingProgressBar;

    public PackageDetailTab CurrentTab => _currentDetailTab;

    public PackageDetailsController(
        NuGetClientService nugetService,
        ErrorHandler? errorHandler,
        Window? window,
        ScrollablePanelControl? detailsPanel,
        Action<List<IWindowControl>> updateDetailsPanel,
        Func<ProjectInfo?> getSelectedProject,
        Func<PackageReference, Task> onUpdatePackage,
        Func<PackageReference, Task> onChangeVersion,
        Func<PackageReference, Task> onRemovePackage,
        Func<ProjectInfo, PackageReference?, Task> onShowDependencyTree)
    {
        _nugetService = nugetService;
        _errorHandler = errorHandler;
        _window = window;
        _detailsPanel = detailsPanel;
        _updateDetailsPanel = updateDetailsPanel;
        _getSelectedProject = getSelectedProject;
        _onUpdatePackage = onUpdatePackage;
        _onChangeVersion = onChangeVersion;
        _onRemovePackage = onRemovePackage;
        _onShowDependencyTree = onShowDependencyTree;
    }

    public void SetNuGetService(NuGetClientService nugetService)
    {
        _nugetService = nugetService;
    }

    public bool HandleDetailTabShortcut(ConsoleKey key)
    {
        var tabIndex = key switch
        {
            ConsoleKey.F1 => 0,
            ConsoleKey.F2 => 1,
            ConsoleKey.F3 => 2,
            ConsoleKey.F4 => 3,
            _ => -1
        };

        if (tabIndex < 0) return false;

        if (_tabControl != null)
        {
            _tabControl.ActiveTabIndex = tabIndex;
            _currentDetailTab = (PackageDetailTab)tabIndex;
        }

        return true;
    }

    public void ShowPackageDetails(PackageReference package)
    {
        // Cancel any previous package load operation to prevent race conditions
        var previousCts = _packageLoadCancellation;
        _packageLoadCancellation = new CancellationTokenSource();
        try { previousCts?.Cancel(); } catch (ObjectDisposedException) { }
        try { previousCts?.Dispose(); } catch (ObjectDisposedException) { }

        // Reset to overview tab and cache state
        _currentDetailTab = PackageDetailTab.Overview;
        _cachedPackageRef = package;
        _cachedNuGetData = null;

        // Show animated loading state
        ShowLoadingState(package);

        // Fetch package details asynchronously
        AsyncHelper.FireAndForget(
            () => LoadPackageDetailsAsync(package, _packageLoadCancellation.Token),
            ex => _errorHandler?.HandleAsync(ex, ErrorSeverity.Info, "Package Details Error", "Failed to load package details.", "NuGet", _window));
    }

    private void RebuildPackageDetailsForTab()
    {
        if (_cachedPackageRef == null) return;

        var controls = InteractivePackageDetailsBuilder.BuildInteractiveDetails(
            _cachedPackageRef,
            _cachedNuGetData,
            _currentDetailTab,
            onUpdate: () => OnUpdatePackage(_cachedPackageRef),
            onChangeVersion: () => OnChangeVersion(_cachedPackageRef),
            onRemove: () => OnRemovePackage(_cachedPackageRef),
            onDeps: () => OnShowPackageDeps(_cachedPackageRef));
        _updateDetailsPanel(controls);
        CaptureTabControl();
    }

    private void CaptureTabControl()
    {
        if (_detailsPanel == null) return;

        _tabControl = FindControlByName<TabControl>(_detailsPanel.GetChildren(), "packageTabs");
    }

    private T? FindControlByName<T>(IReadOnlyList<IWindowControl> controls, string name) where T : class, IWindowControl
    {
        foreach (var control in controls)
        {
            if (control is T typed && control.Name == name)
                return typed;

            // Recursively search in container controls
            if (control is IContainerControl container)
            {
                var found = FindControlByName<T>(container.GetChildren(), name);
                if (found != null)
                    return found;
            }
        }
        return null;
    }

    private void ShowLoadingState(PackageReference package)
    {
        var controls = new List<IWindowControl>();

        // Package header
        var header = Controls.Markup()
            .AddLine($"[cyan1 bold]Package: {Markup.Escape(package.Id)}[/]")
            .AddLine($"[grey70]Installed: {Markup.Escape(package.Version)}[/]")
            .AddEmptyLine()
            .WithMargin(1, 1, 0, 0)
            .Build();
        controls.Add(header);

        // Loading message with spinner
        _loadingMessageControl = Controls.Markup()
            .AddLine($"[cyan1]⠋[/] [grey70]Connecting to NuGet.org...[/]")
            .WithMargin(1, 1, 1, 0)
            .Build();
        controls.Add(_loadingMessageControl);

        // Indeterminate progress bar
        _loadingProgressBar = new ProgressBarControl
        {
            Value = 0,
            MaxValue = 100,
            Width = 50,
            ShowPercentage = false,
            Margin = new Margin(1, 0, 1, 1)
        };
        controls.Add(_loadingProgressBar);

        _updateDetailsPanel(controls);

        // Start loading animation
        StartLoadingAnimation();
    }

    private void StartLoadingAnimation()
    {
        _spinnerFrame = 0;
        _loadingAnimationTimer?.Dispose();

        var spinnerChars = new[] { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
        var messages = new[]
        {
            "Connecting to NuGet.org...",
            "Fetching package metadata...",
            "Loading dependencies...",
            "Analyzing versions..."
        };

        var msgControl = _loadingMessageControl;
        var progressBar = _loadingProgressBar;
        _loadingAnimationTimer = new System.Threading.Timer(_ =>
        {
            try
            {
                _spinnerFrame++;
                var spinnerChar = spinnerChars[_spinnerFrame % spinnerChars.Length];
                var messageIndex = (_spinnerFrame / 5) % messages.Length;
                var message = messages[messageIndex];

                msgControl?.SetContent(new List<string>
                {
                    $"[cyan1]{spinnerChar}[/] [grey70]{message}[/]"
                });

                // Animate progress bar (fake progress for visual feedback)
                if (progressBar != null)
                {
                    progressBar.Value = (_spinnerFrame * 3) % 100;
                }

                _window?.Invalidate(true);
            }
            catch
            {
                // Timer might fire after disposal, ignore
            }
        }, null, 0, 100); // Update every 100ms
    }

    private void StopLoadingAnimation()
    {
        _loadingAnimationTimer?.Dispose();
        _loadingAnimationTimer = null;
        _loadingMessageControl = null;
        _loadingProgressBar = null;
    }

    private async Task LoadPackageDetailsAsync(PackageReference package, CancellationToken cancellationToken)
    {
        try
        {
            // Fetch package details from NuGet.org
            var nugetData = await _nugetService.GetPackageDetailsAsync(package.Id);

            // Check if this operation was cancelled (user selected a different package)
            if (cancellationToken.IsCancellationRequested)
            {
                StopLoadingAnimation();
                return;
            }

            // Update the package reference with latest version info
            if (nugetData != null && !string.IsNullOrEmpty(nugetData.Version))
            {
                package.LatestVersion = nugetData.Version;
            }

            // Cache the fetched data for tab switching
            _cachedNuGetData = nugetData;

            // Check again before updating UI (user might have switched packages)
            if (cancellationToken.IsCancellationRequested)
            {
                StopLoadingAnimation();
                return;
            }

            // Stop the loading animation
            StopLoadingAnimation();

            // Rebuild the details view with the fetched data and interactive buttons
            var controls = InteractivePackageDetailsBuilder.BuildInteractiveDetails(
                package,
                nugetData,
                _currentDetailTab,
                onUpdate: () => OnUpdatePackage(package),
                onChangeVersion: () => OnChangeVersion(package),
                onRemove: () => OnRemovePackage(package),
                onDeps: () => OnShowPackageDeps(package));
            _updateDetailsPanel(controls);
            CaptureTabControl();
        }
        catch (OperationCanceledException)
        {
            // Operation was cancelled, this is expected - do nothing
            StopLoadingAnimation();
        }
        catch (Exception ex)
        {
            StopLoadingAnimation();

            // Only show error if not cancelled
            if (!cancellationToken.IsCancellationRequested)
            {
                AsyncHelper.FireAndForget(
                    () => _errorHandler?.HandleAsync(ex, ErrorSeverity.Info,
                        "Package Details Error", "Failed to load package details.", "NuGet", _window) ?? Task.CompletedTask);
            }
        }
    }

    private void OnShowPackageDeps(PackageReference package)
    {
        var selectedProject = _getSelectedProject();
        if (selectedProject != null)
        {
            AsyncHelper.FireAndForget(
                () => _onShowDependencyTree(selectedProject, package),
                ex => _errorHandler?.HandleAsync(ex, ErrorSeverity.Info, "Dependency Error", "Failed to show dependencies.", "UI", _window));
        }
    }

    private void OnUpdatePackage(PackageReference package)
    {
        AsyncHelper.FireAndForget(
            () => _onUpdatePackage(package),
            ex => _errorHandler?.HandleAsync(ex, ErrorSeverity.Warning, "Update Error", "Failed to update package.", "NuGet", _window));
    }

    private void OnChangeVersion(PackageReference package)
    {
        AsyncHelper.FireAndForget(
            () => _onChangeVersion(package),
            ex => _errorHandler?.HandleAsync(ex, ErrorSeverity.Warning, "Version Error", "Failed to change version.", "NuGet", _window));
    }

    private void OnRemovePackage(PackageReference package)
    {
        AsyncHelper.FireAndForget(
            () => _onRemovePackage(package),
            ex => _errorHandler?.HandleAsync(ex, ErrorSeverity.Warning, "Remove Error", "Failed to remove package.", "NuGet", _window));
    }

    public void Dispose()
    {
        _loadingAnimationTimer?.Dispose();
        _loadingAnimationTimer = null;

        _packageLoadCancellation?.Cancel();
        _packageLoadCancellation?.Dispose();
        _packageLoadCancellation = null;
    }
}
