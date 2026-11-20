namespace SmartGuard.Engine.Tests;

// Same content as Tests/SmartGuard.Configuration.Tests/DescendantFixture.cs (the test assemblies do not
// reference each other); only the namespace differs.
// Test cleanup. Call it after unblocking every task. It waits for ALL tasks (one timing out never
// skips the others) and owns the resources those tasks may use: disposed at once when every task has
// ended; otherwise handed to a continuation that disposes them only after the remaining tasks end, and
// the cleanup fails. Throwing can never stand in for keeping a resource alive. Exceptions of the tasks
// are observed either way; their outcomes are asserted by the tests themselves.
internal static class TestTaskSettle
{
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    internal static Task SettleAsync(IEnumerable<Task?> tasks, params IDisposable?[] resources)
        => SettleAsync(tasks, DefaultTimeout, resources);

    internal static async Task SettleAsync(IEnumerable<Task?> tasks, TimeSpan timeout, params IDisposable?[] resources)
    {
        var started = tasks.OfType<Task>().ToArray();
        var all = Task.WhenAll(started);
        if (await Task.WhenAny(all, Task.Delay(timeout)) == all)
        {
            _ = all.Exception;
            var disposalFailure = DisposeAll(resources);
            if (disposalFailure is not null) throw disposalFailure;
            return;
        }
        var runningCount = started.Count(task => !task.IsCompleted);
        var cleanup = all.ContinueWith(late =>
        {
            _ = late.Exception;
            var disposalFailure = DisposeAll(resources);
            if (disposalFailure is not null)
                Console.Error.WriteLine($"Delayed test cleanup failed: {disposalFailure}");
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        _ = cleanup.ContinueWith(failed => _ = failed.Exception,
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        throw new TimeoutException(
            $"{runningCount} background task(s) did not finish after being unblocked; their resources are released only when they end.");
    }

    private static AggregateException? DisposeAll(IDisposable?[] resources)
    {
        List<Exception>? failures = null;
        foreach (var resource in resources)
        {
            try { resource?.Dispose(); }
            catch (Exception error) { (failures ??= []).Add(error); }
        }
        return failures is null ? null : new AggregateException("Test resource cleanup failed.", failures);
    }
}
