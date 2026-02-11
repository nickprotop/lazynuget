namespace LazyNuGet.Models;

/// <summary>
/// Types of NuGet operations that can be performed
/// </summary>
public enum OperationType
{
    Restore,
    Update,
    Add,
    Remove
}
