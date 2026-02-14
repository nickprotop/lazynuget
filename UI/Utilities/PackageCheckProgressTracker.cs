using LazyNuGet.Models;

namespace LazyNuGet.UI.Utilities;

/// <summary>
/// Thread-safe tracker for background package update check progress.
/// Provides formatted progress messages with animated spinner for status bar display.
/// </summary>
public class PackageCheckProgressTracker
{
    private readonly object _lock = new object();
    private int _completedCount;
    private int _totalCount;
    private bool _isActive;
    private static readonly string[] SpinnerFrames = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };

    /// <summary>
    /// Gets whether tracking is currently active.
    /// </summary>
    public bool IsActive
    {
        get
        {
            lock (_lock)
            {
                return _isActive;
            }
        }
    }

    /// <summary>
    /// Start tracking with the specified total package count.
    /// </summary>
    public void Start(int totalCount)
    {
        lock (_lock)
        {
            _totalCount = totalCount;
            _completedCount = 0;
            _isActive = true;
        }
    }

    /// <summary>
    /// Increment the completed package count.
    /// Thread-safe - can be called from parallel tasks.
    /// </summary>
    public void IncrementCompleted()
    {
        lock (_lock)
        {
            if (_isActive)
            {
                _completedCount++;
            }
        }
    }

    /// <summary>
    /// Get formatted progress message with animated spinner.
    /// </summary>
    /// <param name="frame">Animation frame number for spinner rotation</param>
    /// <returns>Formatted message: "⠋ Checking for updates... 47/152 (30%)"</returns>
    public string GetProgressMessage(int frame)
    {
        lock (_lock)
        {
            if (!_isActive || _totalCount == 0)
            {
                return "[grey70]Checking for updates...[/]";
            }

            var spinner = SpinnerFrames[frame % SpinnerFrames.Length];
            var percentage = (_completedCount * 100) / _totalCount;

            return $"[cyan1]{spinner}[/] [grey70]Checking for updates... {_completedCount}/{_totalCount} ({percentage}%)[/]";
        }
    }

    /// <summary>
    /// Get current progress counts.
    /// </summary>
    /// <returns>Tuple of (completed, total)</returns>
    public (int completed, int total) GetProgress()
    {
        lock (_lock)
        {
            return (_completedCount, _totalCount);
        }
    }

    /// <summary>
    /// Count how many packages are outdated in the project list.
    /// </summary>
    /// <param name="projects">List of projects to check</param>
    /// <returns>Total number of outdated packages across all projects</returns>
    public int GetOutdatedCount(List<ProjectInfo> projects)
    {
        if (projects == null) return 0;

        var outdatedCount = 0;
        foreach (var project in projects)
        {
            foreach (var package in project.Packages)
            {
                if (package.IsOutdated)
                {
                    outdatedCount++;
                }
            }
        }
        return outdatedCount;
    }

    /// <summary>
    /// Mark tracking as complete/inactive.
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            _isActive = false;
        }
    }
}
