using System.Diagnostics;

namespace SmartGuard.Configuration.Tests;

// A parent process that starts a descendant inheriting the redirected pipes, records both PIDs and
// exits while the descendant keeps the pipes open. The fixture owns the descendant and ends it.
internal sealed class DescendantFixture : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"sg-descendant-{Guid.NewGuid():N}");
    internal DescendantFixture() => Directory.CreateDirectory(_dir);
    private string ParentPidPath => Path.Combine(_dir, "parent.pid");
    private string ChildPidPath => Path.Combine(_dir, "child.pid");

    // The descendant inherits the redirected pipes (-NoNewWindow); the parent records both PIDs and exits.
    internal string ParentArguments =>
        "-NoProfile -Command \"$p = Start-Process powershell.exe -NoNewWindow -PassThru -ArgumentList '-NoProfile -Command Start-Sleep -Seconds 20'; " +
        $"[IO.File]::WriteAllText('{ChildPidPath}', [string]$p.Id); [IO.File]::WriteAllText('{ParentPidPath}', [string]$PID)\"";

    // The scenario under test: the parent has exited while its descendant still holds the pipes.
    internal bool WaitParentExitedWithDescendantAlive(TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        // Both PIDs must be readable, not merely present: a file can exist before its text is written.
        if (!SpinWait.SpinUntil(() => ReadPid(ParentPidPath) is not null && ReadPid(ChildPidPath) is not null, timeout)) return false;
        using (var parent = TryGet(ParentPidPath))
            if (parent is not null && !parent.WaitForExit((int)Math.Max(0, (timeout - watch.Elapsed).TotalMilliseconds))) return false;
        using var child = TryGet(ChildPidPath);
        return child is not null && !child.HasExited;
    }

    // Signal only: TerminateProcess returns at once and the kernel then closes the child's handles.
    internal void SignalDescendantExit()
    {
        using var child = TryGet(ChildPidPath);
        try { child?.Kill(); } catch (InvalidOperationException) { }
    }

    internal void KillAndWait()
    {
        using var child = TryGet(ChildPidPath);
        if (child is null) return;
        try { child.Kill(entireProcessTree: true); child.WaitForExit(5000); } catch (InvalidOperationException) { }
    }

    private static int? ReadPid(string pidPath)
    {
        try { return File.Exists(pidPath) && int.TryParse(File.ReadAllText(pidPath), out var pid) ? pid : null; }
        catch (IOException) { return null; }
    }

    private static Process? TryGet(string pidPath)
    {
        try { return ReadPid(pidPath) is { } pid ? Process.GetProcessById(pid) : null; }
        catch (ArgumentException) { return null; }
    }

    public void Dispose()
    {
        KillAndWait();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }
}

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
