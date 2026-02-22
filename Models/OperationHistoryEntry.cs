namespace LazyNuGet.Models;

/// <summary>
/// Represents a historical record of a NuGet operation
/// </summary>
public class OperationHistoryEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public OperationType Type { get; init; }
    public string ProjectName { get; init; } = "";
    public string Description { get; init; } = "";
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public TimeSpan Duration { get; init; }

    // For retry functionality
    public string ProjectPath { get; init; } = "";
    public string? PackageId { get; init; }
    public string? PackageVersion { get; init; }
    public string? PreviousVersion { get; init; }

    // CPM metadata — needed so rollback/retry can route through CpmRepository instead of CLI
    public VersionSource VersionSource { get; init; } = VersionSource.Inline;
    public string? PropsFilePath { get; init; }
}
