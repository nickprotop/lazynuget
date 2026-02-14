using System.Text.Json;
using LazyNuGet.Models;

namespace LazyNuGet.Repositories;

/// <summary>
/// Repository for persisting and loading operation history from disk.
/// This is the data access layer - handles all file I/O for operation logs.
/// </summary>
public class HistoryRepository
{
    private readonly string _historyFilePath;

    public HistoryRepository(string historyFilePath)
    {
        _historyFilePath = historyFilePath ?? throw new ArgumentNullException(nameof(historyFilePath));
    }

    /// <summary>
    /// Load operation history from disk
    /// </summary>
    public async Task<List<OperationHistoryEntry>> LoadHistoryAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(_historyFilePath))
                    return new List<OperationHistoryEntry>();

                var json = File.ReadAllText(_historyFilePath);
                var entries = JsonSerializer.Deserialize<List<OperationHistoryEntry>>(json);
                return entries ?? new List<OperationHistoryEntry>();
            }
            catch
            {
                return new List<OperationHistoryEntry>();
            }
        });
    }

    /// <summary>
    /// Save operation history to disk
    /// </summary>
    public async Task SaveHistoryAsync(List<OperationHistoryEntry> entries)
    {
        await Task.Run(() =>
        {
            try
            {
                var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_historyFilePath, json);
            }
            catch
            {
                // Ignore save errors - not critical
            }
        });
    }

    /// <summary>
    /// Check if history file exists
    /// </summary>
    public bool HistoryFileExists()
    {
        return File.Exists(_historyFilePath);
    }

    /// <summary>
    /// Delete the history file
    /// </summary>
    public void DeleteHistoryFile()
    {
        try
        {
            if (File.Exists(_historyFilePath))
                File.Delete(_historyFilePath);
        }
        catch
        {
            // Ignore delete errors
        }
    }
}
