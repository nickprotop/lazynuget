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
    private readonly object _lock = new();
    private readonly SemaphoreSlim _initGate = new(0, 1);
    private const int MaxHistorySize = 100;

    public OperationHistoryService(string configDirectory)
    {
        var historyFilePath = Path.Combine(configDirectory, "operation_history.json");
        _repository = new HistoryRepository(historyFilePath);
        AsyncHelper.FireAndForget(() => InitializeAsync());
    }

    private async Task InitializeAsync()
    {
        try
        {
            var loaded = await _repository.LoadHistoryAsync();
            lock (_lock)
            {
                // Merge: preserve any entries added during initialization
                var pendingEntries = new List<OperationHistoryEntry>(_history);
                _history.Clear();
                _history.AddRange(pendingEntries);
                _history.AddRange(loaded);
            }
        }
        finally
        {
            _initGate.Release();
        }
    }

    /// <summary>
    /// Add a new operation to the history
    /// </summary>
    public void AddEntry(OperationHistoryEntry entry)
    {
        lock (_lock)
        {
            _history.Insert(0, entry); // Most recent first

            // Apply business rule: Trim to max size
            if (_history.Count > MaxHistorySize)
            {
                _history.RemoveRange(MaxHistorySize, _history.Count - MaxHistorySize);
            }
        }

        AsyncHelper.FireAndForget(() => SaveHistoryAsync());
    }

    /// <summary>
    /// Get operation history entries
    /// </summary>
    public IReadOnlyList<OperationHistoryEntry> GetHistory(int limit = 50)
    {
        lock (_lock)
        {
            return _history.Take(limit).ToList();
        }
    }

    /// <summary>
    /// Get only failed operations
    /// </summary>
    public IReadOnlyList<OperationHistoryEntry> GetFailedOperations()
    {
        lock (_lock)
        {
            return _history.Where(e => !e.Success).ToList();
        }
    }

    /// <summary>
    /// Clear all history
    /// </summary>
    public void ClearHistory()
    {
        lock (_lock)
        {
            _history.Clear();
        }
        AsyncHelper.FireAndForget(() => SaveHistoryAsync());
    }

    private async Task SaveHistoryAsync()
    {
        List<OperationHistoryEntry> snapshot;
        lock (_lock)
        {
            snapshot = new List<OperationHistoryEntry>(_history);
        }
        await _repository.SaveHistoryAsync(snapshot);
    }
}
