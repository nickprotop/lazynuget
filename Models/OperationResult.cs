namespace LazyNuGet.Models;

/// <summary>
/// Represents the result of a CLI operation
/// </summary>
public class OperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ErrorDetails { get; set; }
    public int ExitCode { get; set; }

    public static OperationResult FromSuccess(string message) =>
        new() { Success = true, Message = message, ExitCode = 0 };

    public static OperationResult FromError(string message, string? details = null, int exitCode = 1) =>
        new() { Success = false, Message = message, ErrorDetails = details, ExitCode = exitCode };
}
