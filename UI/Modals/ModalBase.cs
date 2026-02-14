using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Core;
using SharpConsoleUI.Drawing;
using Spectre.Console;
using LazyNuGet.UI.Utilities;

namespace LazyNuGet.UI.Modals;

/// <summary>
/// Abstract base class for all modals, eliminating ~1,200 lines of duplicated boilerplate.
/// Provides:
/// - Standard modal window creation and lifecycle
/// - TaskCompletionSource pattern for async/await
/// - Standard Escape key handling
/// - Result management
/// - Window system integration
/// </summary>
/// <typeparam name="TResult">The type of result this modal returns</typeparam>
public abstract class ModalBase<TResult>
{
    private readonly TaskCompletionSource<TResult> _tcs = new();
    protected TResult? Result { get; set; }

    protected Window Modal { get; private set; } = null!;
    protected ConsoleWindowSystem WindowSystem { get; private set; } = null!;
    protected Window? ParentWindow { get; private set; }

    /// <summary>
    /// Show the modal and return the result asynchronously.
    /// This is the main entry point for all modals.
    /// </summary>
    public Task<TResult> ShowAsync(
        ConsoleWindowSystem windowSystem,
        Window? parentWindow = null)
    {
        WindowSystem = windowSystem;
        ParentWindow = parentWindow;

        // Build the modal window
        Modal = CreateModal();

        // Build modal content (implemented by derived classes)
        BuildContent();

        // Wire up standard event handlers
        AttachEventHandlers();

        // Show the modal
        WindowSystem.AddWindow(Modal);
        WindowSystem.SetActiveWindow(Modal);

        // Let derived classes set initial focus
        SetInitialFocus();

        return _tcs.Task;
    }

    /// <summary>
    /// Create the modal window with standard settings.
    /// Override to customize window properties.
    /// </summary>
    protected virtual Window CreateModal()
    {
        var builder = new WindowBuilder(WindowSystem)
            .Centered()
            .AsModal()
            .Resizable(GetResizable())
            .Movable(GetMovable())
            .Minimizable(false)
            .WithColors(ColorScheme.WindowBackground, Color.Grey93)
            .WithBorderStyle(GetBorderStyle())
            .WithBorderColor(GetBorderColor());

        // Apply title if provided
        var title = GetTitle();
        if (!string.IsNullOrEmpty(title))
        {
            builder.WithTitle(title);
        }

        // Apply size
        var (width, height) = GetSize();
        builder.WithSize(width, height);

        return builder.Build();
    }

    /// <summary>
    /// Build the modal content. This is where derived classes add controls.
    /// </summary>
    protected abstract void BuildContent();

    /// <summary>
    /// Get the modal title. Override to provide a custom title.
    /// </summary>
    protected abstract string GetTitle();

    /// <summary>
    /// Get the modal size (width, height). Override to customize.
    /// </summary>
    protected virtual (int width, int height) GetSize() => (60, 18);

    /// <summary>
    /// Whether the modal is resizable. Override to customize.
    /// </summary>
    protected virtual bool GetResizable() => true;

    /// <summary>
    /// Whether the modal is movable. Override to customize.
    /// </summary>
    protected virtual bool GetMovable() => true;

    /// <summary>
    /// Get the border style. Override to customize.
    /// </summary>
    protected virtual BorderStyle GetBorderStyle() => BorderStyle.DoubleLine;

    /// <summary>
    /// Get the border color. Override to customize.
    /// </summary>
    protected virtual Color GetBorderColor() => ColorScheme.BorderColor;

    /// <summary>
    /// Set initial focus to a control. Override to customize.
    /// Default: no initial focus set.
    /// </summary>
    protected virtual void SetInitialFocus()
    {
        // Default: no-op
    }

    /// <summary>
    /// Attach standard event handlers for key press and close events.
    /// </summary>
    private void AttachEventHandlers()
    {
        Modal.KeyPressed += OnKeyPressed;
        Modal.OnClosed += OnModalClosed;
    }

    /// <summary>
    /// Handle key press events. Override to add custom key handling.
    /// Default behavior: Escape closes the modal.
    /// </summary>
    protected virtual void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Escape)
        {
            OnEscapePressed();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Called when Escape is pressed. Override to customize escape behavior.
    /// Default: closes modal with default result.
    /// </summary>
    protected virtual void OnEscapePressed()
    {
        CloseWithResult(GetDefaultResult());
    }

    /// <summary>
    /// Get the default result when modal is cancelled (e.g., via Escape).
    /// Override to provide a custom default.
    /// </summary>
    protected virtual TResult GetDefaultResult()
    {
        return default(TResult)!;
    }

    /// <summary>
    /// Called when the modal window is closed (regardless of how).
    /// This ensures the TCS is always completed.
    /// </summary>
    private void OnModalClosed(object? sender, EventArgs e)
    {
        // Call cleanup hook for derived classes (e.g., event handler unsubscribe)
        OnCleanup();

        // Always try to set result. If already set, this is a no-op.
        _tcs.TrySetResult(Result ?? GetDefaultResult());
    }

    /// <summary>
    /// Override this to perform cleanup when the modal closes.
    /// Use this for unsubscribing event handlers to prevent memory leaks.
    /// Called automatically before TCS completion.
    /// </summary>
    /// <example>
    /// protected override void OnCleanup()
    /// {
    ///     if (_myButton != null)
    ///         _myButton.Click -= _buttonClickHandler;
    ///     if (_myList != null)
    ///         _myList.SelectedIndexChanged -= _listChangedHandler;
    /// }
    /// </example>
    protected virtual void OnCleanup()
    {
        // Default: no-op. Override in derived classes for cleanup.
    }

    /// <summary>
    /// Close the modal with the specified result.
    /// This is the standard way for derived classes to complete the modal.
    /// </summary>
    protected void CloseWithResult(TResult result)
    {
        Result = result;
        Modal.Close();
    }
}
