namespace LazyNuGet.Services;

public static class AsyncHelper
{
    public static async void FireAndForget(
        Func<Task> asyncAction,
        Action<Exception>? onError = null)
    {
        try
        {
            await asyncAction();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            onError?.Invoke(ex);
        }
    }
}
