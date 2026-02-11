using System.Text.Json;
using LazyNuGet.Models;

namespace LazyNuGet.Services;

/// <summary>
/// Service for tracking and persisting NuGet operation history
/// </summary>
public class OperationHistoryService
{
    private readonly List<OperationHistoryEntry> _history = new();
    private readonly string _historyFilePath;
    private const int MaxHistorySize = 100;

    public OperationHistoryService(string configDirectory)
    {
        _historyFilePath = Path.Combine(configDirectory, "operation_history.json");
        LoadHistory();
    }

    /// <summary>
    /// Add a new operation to the history
    /// </summary>
    public void AddEntry(OperationHistoryEntry entry)
    {
        _history.Insert(0, entry); // Most recent first

        // Trim to max size
        if (_history.Count > MaxHistorySize)
        {
            _history.RemoveRange(MaxHistorySize, _history.Count - MaxHistorySize);
        }

        SaveHistory();
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
        SaveHistory();
    }

    private void LoadHistory()
    {
        try
        {
            if (File.Exists(_historyFilePath))
            {
                var json = File.ReadAllText(_historyFilePath);
                var loaded = JsonSerializer.Deserialize<List<OperationHistoryEntry>>(json);
                if (loaded != null)
                {
                    _history.AddRange(loaded);
                }
            }
        }
        catch (Exception)
        {
            // Ignore errors loading history - start fresh
        }
    }

    private void SaveHistory()
    {
        try
        {
            var json = JsonSerializer.Serialize(_history, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_historyFilePath, json);
        }
        catch (Exception)
        {
            // Ignore errors saving history - not critical
        }
    }
}
