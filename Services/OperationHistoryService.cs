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
