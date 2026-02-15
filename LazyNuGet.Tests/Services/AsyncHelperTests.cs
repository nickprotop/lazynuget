using FluentAssertions;
using LazyNuGet.Services;

namespace LazyNuGet.Tests.Services;

public class AsyncHelperTests
{
    [Fact]
    public async Task FireAndForget_SuccessfulTask_Completes()
    {
        var completed = false;

        AsyncHelper.FireAndForget(async () =>
        {
            await Task.Delay(10);
            completed = true;
        });

        // Allow time for the async operation to complete
        await Task.Delay(200);
        completed.Should().BeTrue();
    }

    [Fact]
    public async Task FireAndForget_ThrowingTask_CallsOnError()
    {
        Exception? captured = null;
        var errorCalled = new TaskCompletionSource<bool>();

        AsyncHelper.FireAndForget(
            async () =>
            {
                await Task.Delay(10);
                throw new InvalidOperationException("test error");
            },
            ex =>
            {
                captured = ex;
                errorCalled.SetResult(true);
            });

        await Task.WhenAny(errorCalled.Task, Task.Delay(2000));
        captured.Should().NotBeNull();
        captured.Should().BeOfType<InvalidOperationException>();
        captured!.Message.Should().Be("test error");
    }

    [Fact]
    public async Task FireAndForget_CancelledTask_DoesNotCallOnError()
    {
        var errorCalled = false;

        AsyncHelper.FireAndForget(
            async () =>
            {
                await Task.Delay(10);
                throw new OperationCanceledException();
            },
            ex => errorCalled = true);

        await Task.Delay(200);
        errorCalled.Should().BeFalse();
    }

    [Fact]
    public async Task FireAndForget_NullOnError_DoesNotThrow()
    {
        // Should not throw even when onError is null and task throws
        AsyncHelper.FireAndForget(
            async () =>
            {
                await Task.Delay(10);
                throw new InvalidOperationException("no handler");
            });

        // Give time for the exception to propagate (or not)
        await Task.Delay(200);
        // If we reach here without crashing, the test passes
    }
}
