using SharpConsoleUI;
using SharpConsoleUI.Logging;
using LazyNuGet.UI.Modals;

namespace LazyNuGet.Services;

/// <summary>
/// Severity levels for error handling
/// </summary>
public enum ErrorSeverity
{
    /// <summary>
    /// Critical errors - shown in modal dialog and logged
    /// </summary>
    Critical,

    /// <summary>
    /// Warning errors - shown in status bar and logged
    /// </summary>
    Warning,

    /// <summary>
    /// Informational errors - logged only, no user notification
    /// </summary>
    Info
}

/// <summary>
/// Unified error handling service with consistent logging, modals, and status bar updates
/// </summary>
public class ErrorHandler
{
    private readonly ConsoleWindowSystem _windowSystem;
    private readonly ILogService? _logService;

    public ErrorHandler(ConsoleWindowSystem windowSystem, ILogService? logService = null)
    {
        _windowSystem = windowSystem ?? throw new ArgumentNullException(nameof(windowSystem));
        _logService = logService;
    }

    /// <summary>
    /// Handle an exception with the specified severity level
    /// </summary>
    /// <param name="exception">The exception to handle</param>
    /// <param name="severity">The severity level (Critical, Warning, Info)</param>
    /// <param name="title">Title for modal/notification</param>
    /// <param name="message">User-friendly message</param>
    /// <param name="context">Context for logging (e.g., "NuGet", "UI", "CLI")</param>
    /// <param name="parentWindow">Parent window for modal display</param>
    public async Task HandleAsync(
        Exception exception,
        ErrorSeverity severity,
        string title,
        string message,
        string context = "App",
        Window? parentWindow = null)
    {
        // Always log the error
        LogError(exception, severity, title, message, context);

        // Show user notification based on severity
        switch (severity)
        {
            case ErrorSeverity.Critical:
                await ShowCriticalErrorAsync(title, message, exception.Message, parentWindow);
                break;

            case ErrorSeverity.Warning:
                ShowWarningNotification(title, message, parentWindow);
                break;

            case ErrorSeverity.Info:
                // Info level - log only, no user notification
                break;
        }
    }

    /// <summary>
    /// Handle an error without an exception (for validation errors, business logic failures, etc.)
    /// </summary>
    /// <param name="severity">The severity level</param>
    /// <param name="title">Title for modal/notification</param>
    /// <param name="message">User-friendly message</param>
    /// <param name="details">Additional details</param>
    /// <param name="context">Context for logging</param>
    /// <param name="parentWindow">Parent window for modal display</param>
    public async Task HandleAsync(
        ErrorSeverity severity,
        string title,
        string message,
        string? details = null,
        string context = "App",
        Window? parentWindow = null)
    {
        // Log the error
        LogError(null, severity, title, message, context, details);

        // Show user notification based on severity
        switch (severity)
        {
            case ErrorSeverity.Critical:
                await ShowCriticalErrorAsync(title, message, details, parentWindow);
                break;

            case ErrorSeverity.Warning:
                ShowWarningNotification(title, message, parentWindow);
                break;

            case ErrorSeverity.Info:
                // Info level - log only, no user notification
                break;
        }
    }

    private void LogError(
        Exception? exception,
        ErrorSeverity severity,
        string title,
        string message,
        string context,
        string? details = null)
    {
        var logMessage = $"{title}: {message}";
        if (!string.IsNullOrEmpty(details))
        {
            logMessage += $" - {details}";
        }

        switch (severity)
        {
            case ErrorSeverity.Critical:
                _logService?.LogError(logMessage, exception, context);
                break;

            case ErrorSeverity.Warning:
                _logService?.LogWarning(logMessage, context);
                break;

            case ErrorSeverity.Info:
                _logService?.LogInfo(logMessage, context);
                break;
        }
    }

    private async Task ShowCriticalErrorAsync(
        string title,
        string message,
        string? details,
        Window? parentWindow)
    {
        await ErrorModal.ShowAsync(_windowSystem, title, message, details, parentWindow);
    }

    private void ShowWarningNotification(
        string title,
        string message,
        Window? parentWindow)
    {
        _windowSystem.NotificationStateService.ShowNotification(
            title,
            message,
            SharpConsoleUI.Core.NotificationSeverity.Warning,
            timeout: 5000,
            parentWindow: parentWindow);
    }
}
