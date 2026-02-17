using SharpConsoleUI;
using SharpConsoleUI.Controls;
using LazyNuGet.Models;
using LazyNuGet.Services;
using LazyNuGet.UI.Utilities;

namespace LazyNuGet.Orchestrators;

/// <summary>
/// Manages background package outdated checks with progress animation.
/// Extracted from LazyNuGetWindow to reduce god-class complexity.
/// </summary>
public class PackageCheckController : IDisposable
{
    private readonly ConsoleWindowSystem _windowSystem;
    private NuGetClientService _nugetService;
    private readonly MarkupControl? _bottomHelpBar;
    private readonly Window? _window;
    private readonly StatusBarManager? _statusBarManager;
    private readonly ErrorHandler? _errorHandler;
    private readonly Func<ViewState> _getCurrentViewState;
    private readonly Func<bool> _isFilterMode;
    private readonly Action _refreshCurrentView;
    private readonly Func<List<ProjectInfo>> _getProjects;

    private System.Threading.Timer? _checkProgressTimer;
    private PackageCheckProgressTracker? _checkProgressTracker;

    public PackageCheckController(
        ConsoleWindowSystem windowSystem,
        NuGetClientService nugetService,
        MarkupControl? bottomHelpBar,
        Window? window,
        StatusBarManager? statusBarManager,
        ErrorHandler? errorHandler,
        Func<ViewState> getCurrentViewState,
        Func<bool> isFilterMode,
        Action refreshCurrentView,
        Func<List<ProjectInfo>> getProjects)
    {
        _windowSystem = windowSystem;
        _nugetService = nugetService;
        _bottomHelpBar = bottomHelpBar;
        _window = window;
        _statusBarManager = statusBarManager;
        _errorHandler = errorHandler;
        _getCurrentViewState = getCurrentViewState;
        _isFilterMode = isFilterMode;
        _refreshCurrentView = refreshCurrentView;
        _getProjects = getProjects;
    }

    public void SetNuGetService(NuGetClientService nugetService)
    {
        _nugetService = nugetService;
    }

    public async Task CheckForOutdatedPackagesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var projects = _getProjects();

            // Collect all packages from all projects
            var allPackages = projects.SelectMany(p => p.Packages).ToList();
            if (allPackages.Count == 0) return;

            _windowSystem.LogService.LogInfo($"Checking {allPackages.Count} packages for updates...", "NuGet");

            // START PROGRESS ANIMATION
            StartCheckProgressAnimation(allPackages.Count);

            // Use semaphore to limit concurrent API calls (max 10 at a time)
            var semaphore = new SemaphoreSlim(10, 10);

            // Check packages in parallel with throttling
            var tasks = allPackages.Select(async package =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var (isOutdated, latestVersion) = await _nugetService.CheckIfOutdatedAsync(
                        package.Id,
                        package.Version,
                        cancellationToken);

                    // Safe: each package object is a distinct reference from SelectMany
                    package.LatestVersion = latestVersion;

                    // UPDATE PROGRESS
                    UpdateCheckProgress();
                }
                catch (Exception ex)
                {
                    _windowSystem.LogService.LogWarning($"Failed to check {package.Id}: {ex.Message}", "NuGet");
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            // STOP PROGRESS ANIMATION
            StopCheckProgressAnimation(cancelled: false);

            // Refresh current view to reflect updated outdated status
            _refreshCurrentView();

            _windowSystem.LogService.LogInfo($"Completed checking {allPackages.Count} packages", "NuGet");
        }
        catch (OperationCanceledException)
        {
            StopCheckProgressAnimation(cancelled: true);
            _windowSystem.LogService.LogInfo("Package check cancelled", "NuGet");
        }
        catch (Exception ex)
        {
            StopCheckProgressAnimation(cancelled: false);
            AsyncHelper.FireAndForget(
                () => _errorHandler?.HandleAsync(ex, ErrorSeverity.Info,
                    "Package Check Error", "Failed to check for outdated packages.", "NuGet", _window) ?? Task.CompletedTask);
        }
    }

    private void StartCheckProgressAnimation(int totalPackages)
    {
        _checkProgressTracker = new PackageCheckProgressTracker();
        _checkProgressTracker.Start(totalPackages);

        _checkProgressTimer?.Dispose();

        var tracker = _checkProgressTracker;
        var spinnerFrame = 0;
        _checkProgressTimer = new System.Threading.Timer(_ =>
        {
            try
            {
                if (tracker == null || !tracker.IsActive)
                    return;

                spinnerFrame++;
                var message = tracker.GetProgressMessage(spinnerFrame);

                _bottomHelpBar?.SetContent(new List<string> { message });
                _window?.Invalidate(true);
            }
            catch
            {
                // Timer might fire after disposal, ignore
            }
        }, null, 0, 100); // Update every 100ms
    }

    private void StopCheckProgressAnimation(bool cancelled = false)
    {
        _checkProgressTimer?.Dispose();
        _checkProgressTimer = null;

        if (_checkProgressTracker == null) return;

        var (completed, total) = _checkProgressTracker.GetProgress();
        _checkProgressTracker.Stop();

        string completionMessage;
        int displayDurationMs;

        if (cancelled)
        {
            completionMessage = $"[yellow]⚠[/] [grey70]Update check cancelled[/]";
            displayDurationMs = 1500;
        }
        else
        {
            var projects = _getProjects();
            var outdatedCount = _checkProgressTracker.GetOutdatedCount(projects);
            completionMessage = $"[green]✓[/] [grey70]Checked {total} packages - {outdatedCount} outdated[/]";
            displayDurationMs = 2000;
        }

        _bottomHelpBar?.SetContent(new List<string> { completionMessage });
        _window?.Invalidate(true);

        // Restore help text after delay — capture view state now, not at restore time
        var trackerSnapshot = _checkProgressTracker;
        var viewStateAtCompletion = _getCurrentViewState();
        _checkProgressTracker = null;
        AsyncHelper.FireAndForget(async () =>
        {
            await Task.Delay(displayDurationMs);

            // Only restore if view state hasn't changed since completion
            if (!_isFilterMode() && trackerSnapshot != null && !trackerSnapshot.IsActive
                && _getCurrentViewState() == viewStateAtCompletion)
            {
                _statusBarManager?.UpdateHelpBar(viewStateAtCompletion);
                _window?.Invalidate(true);
            }
        });
    }

    private void UpdateCheckProgress()
    {
        _checkProgressTracker?.IncrementCompleted();
    }

    public void Dispose()
    {
        _checkProgressTimer?.Dispose();
        _checkProgressTimer = null;
        _checkProgressTracker = null;
    }
}
