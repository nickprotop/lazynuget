using LazyNuGet.Models;
using LazyNuGet.Repositories;

namespace LazyNuGet.Services;

/// <summary>
/// Service for tracking and persisting NuGet operation history - provides business logic on top of HistoryRepository.
/// Manages history limits and provides query capabilities.
/// </summary>
public class OperationHistoryService
{
    private readonly HistoryRepository _repository;
    private readonly List<OperationHistoryEntry> _history = new();
    private const int MaxHistorySize = 100;

    public OperationHistoryService(string configDirectory)
    {
        var historyFilePath = Path.Combine(configDirectory, "operation_history.json");
        _repository = new HistoryRepository(historyFilePath);
        _ = LoadHistoryAsync();
    }

    /// <summary>
    /// Add a new operation to the history
    /// </summary>
    public void AddEntry(OperationHistoryEntry entry)
    {
        _history.Insert(0, entry); // Most recent first

        // Apply business rule: Trim to max size
        if (_history.Count > MaxHistorySize)
        {
            _history.RemoveRange(MaxHistorySize, _history.Count - MaxHistorySize);
        }

        _ = SaveHistoryAsync();
    }

    /// <summary>
    /// Get operation history entries
    /// </summary>
    public IReadOnlyList<OperationHistoryEntry> GetHistory(int limit = 50)
    {
        return _history.Take(limit).ToList();
    }

    /// <summary>
    /// Get only failed operations
    /// </summary>
    public IReadOnlyList<OperationHistoryEntry> GetFailedOperations()
    {
        return _history.Where(e => !e.Success).ToList();
    }

    /// <summary>
    /// Clear all history
    /// </summary>
    public void ClearHistory()
    {
        _history.Clear();
        _ = SaveHistoryAsync();
    }

    /// <summary>
    /// Get the 10 most recently installed packages (unique by PackageId)
    /// </summary>
    public async Task<List<RecentPackageInfo>> GetRecentInstallsAsync()
    {
        // Filter for successful Add operations
        var installOperations = _history
            .Where(e => e.Type == OperationType.Add && e.Success && !string.IsNullOrEmpty(e.PackageId))
            .ToList();

        // Group by PackageId and take the most recent entry for each package
        var recentPackages = installOperations
            .GroupBy(e => e.PackageId)
            .Select(g => g.OrderByDescending(e => e.Timestamp).First())
            .OrderByDescending(e => e.Timestamp)
            .Take(10)
            .Select(e => new RecentPackageInfo
            {
                PackageId = e.PackageId!,
                Version = e.PackageVersion ?? "latest",
                LastInstalled = e.Timestamp,
                ProjectName = e.ProjectName
            })
            .ToList();

        return await Task.FromResult(recentPackages);
    }

    private async Task LoadHistoryAsync()
    {
        var loaded = await _repository.LoadHistoryAsync();
        _history.Clear();
        _history.AddRange(loaded);
    }

    private async Task SaveHistoryAsync()
    {
        await _repository.SaveHistoryAsync(_history);
    }
}

/// <summary>
/// Information about a recently installed package
/// </summary>
public class RecentPackageInfo
{
    public string PackageId { get; set; } = "";
    public string Version { get; set; } = "";
    public DateTime LastInstalled { get; set; }
    public string ProjectName { get; set; } = "";
}
