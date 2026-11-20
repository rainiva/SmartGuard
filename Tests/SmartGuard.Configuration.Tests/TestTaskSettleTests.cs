using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using FluentAssertions;

namespace SmartGuard.Configuration.Tests;

file sealed class Tracked(string? failure = null) : IDisposable
{
    private int _calls;
    internal int Calls => Volatile.Read(ref _calls);
    internal bool Disposed => Calls > 0;
    internal TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Dispose()
    {
        Interlocked.Increment(ref _calls);
        Released.TrySetResult();
        if (failure is not null) throw new IOException(failure);
    }
}

public class TestTaskSettleTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Deadline = TimeSpan.FromMilliseconds(50);

    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task A_timed_out_task_keeps_its_resources_until_it_ends()
    {
        var gate = new TaskCompletionSource();
        var resource = new Tracked();
        var stuck = gate.Task;
        var finished = Task.CompletedTask;

        try
        {
            var settle = () => TestTaskSettle.SettleAsync([stuck, finished], TimeSpan.FromMilliseconds(100), resource);
            await settle.Should().ThrowAsync<TimeoutException>();
            resource.Disposed.Should().BeFalse("the stuck task may still use it");
        }
        finally
        {
            gate.TrySetResult();
            SpinWait.SpinUntil(() => resource.Disposed, TimeSpan.FromSeconds(5)).Should().BeTrue("released once the task ends");
        }
    }

    [Fact]
    public async Task Every_task_is_awaited_even_when_one_failed()
    {
        var resource = new Tracked();
        var slow = Task.Delay(200);
        await TestTaskSettle.SettleAsync([Task.FromException(new InvalidOperationException()), slow], resource);
        slow.IsCompleted.Should().BeTrue("a faulted first task never skips waiting for the next one");
        resource.Disposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disposal_failures_are_reported_after_every_resource_is_attempted(bool secondFails)
    {
        var first = new Tracked("first-dispose-failed");
        var second = new Tracked(secondFails ? "second-dispose-failed" : null);
        var settle = () => TestTaskSettle.SettleAsync([Task.CompletedTask], first, second);

        var failure = await settle.Should().ThrowAsync<AggregateException>();
        failure.Which.Flatten().InnerExceptions.Should().HaveCount(secondFails ? 2 : 1);
        first.Disposed.Should().BeTrue();
        second.Disposed.Should().BeTrue("one failed disposal must not skip the next resource");
    }

    [Fact]
    public async Task Empty_and_null_inputs_release_the_resources_once()
    {
        var resource = new Tracked();
        await TestTaskSettle.SettleAsync([null, null], null, resource);
        resource.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Completed_tasks_release_every_resource_once()
    {
        var first = new Tracked();
        var second = new Tracked();
        await TestTaskSettle.SettleAsync([Task.CompletedTask, Task.FromResult(1)], first, second);
        first.Calls.Should().Be(1);
        second.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData("faulted")]
    [InlineData("cancelled")]
    public async Task An_ended_task_never_skips_waiting_for_a_pending_one(string how)
    {
        var ended = how == "faulted"
            ? Task.FromException(new IOException("expected-work-fault"))
            : Task.FromCanceled(new CancellationToken(true));
        var gate = Gate();
        var resource = new Tracked();
        var settling = TestTaskSettle.SettleAsync([ended, gate.Task], Limit, resource);
        try
        {
            settling.IsCompleted.Should().BeFalse("cleanup still waits for the pending task");
            resource.Calls.Should().Be(0, "the pending task may still use it");
        }
        finally
        {
            gate.TrySetResult();
            await settling.WaitAsync(Limit);
        }
        resource.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Ending_one_of_two_timed_out_tasks_keeps_the_resources()
    {
        var first = Gate();
        var second = Gate();
        var resource = new Tracked();
        try
        {
            var settle = () => TestTaskSettle.SettleAsync([first.Task, second.Task], Deadline, resource);
            await settle.Should().ThrowAsync<TimeoutException>();
            first.SetResult();
            await Task.Yield();
            resource.Calls.Should().Be(0, "the other task may still use it");
        }
        finally
        {
            first.TrySetResult();
            second.TrySetResult();
            await resource.Released.Task.WaitAsync(Limit);
        }
        resource.Calls.Should().Be(1, "delayed cleanup disposes exactly once");
    }

    [Fact]
    public async Task Two_independent_settles_keep_their_resources_apart()
    {
        var first = Gate();
        var second = Gate();
        var a = new Tracked();
        var b = new Tracked();
        try
        {
            var settleA = () => TestTaskSettle.SettleAsync([first.Task], Deadline, a);
            var settleB = () => TestTaskSettle.SettleAsync([second.Task], Deadline, b);
            await Task.WhenAll(settleA.Should().ThrowAsync<TimeoutException>(), settleB.Should().ThrowAsync<TimeoutException>());
            first.SetResult();
            await a.Released.Task.WaitAsync(Limit);
            b.Calls.Should().Be(0, "an unrelated cleanup never releases another operation's resource");
        }
        finally
        {
            first.TrySetResult();
            second.TrySetResult();
            await Task.WhenAll(a.Released.Task, b.Released.Task).WaitAsync(Limit);
        }
        a.Calls.Should().Be(1);
        b.Calls.Should().Be(1);
    }
}

// These cases change process-wide state (Console.Error, the unobserved-exception event, forced
// collections), so they run alone and cannot capture or hide another test's output.
[CollectionDefinition("TestHostGlobalState", DisableParallelization = true)]
public class TestHostGlobalStateCollection { }

[Collection("TestHostGlobalState")]
public class TestTaskSettleGlobalStateTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Deadline = TimeSpan.FromMilliseconds(50);

    [Fact]
    public async Task A_delayed_disposal_failure_is_written_to_stderr()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failing = new Tracked("delayed-dispose-marker");
        var last = new Tracked();
        var previous = Console.Error;
        using var captured = new StringWriter();
        var synchronized = TextWriter.Synchronized(captured);
        Console.SetError(synchronized);
        try
        {
            var settle = () => TestTaskSettle.SettleAsync([gate.Task], Deadline, failing, last);
            await settle.Should().ThrowAsync<TimeoutException>();
            failing.Calls.Should().Be(0);
            gate.SetResult();
            await last.Released.Task.WaitAsync(Limit);
            SpinWait.SpinUntil(() => { lock (synchronized) return captured.ToString().Contains("delayed-dispose-marker"); },
                TimeSpan.FromSeconds(2)).Should().BeTrue("a delayed disposal failure must stay visible");
        }
        finally
        {
            gate.TrySetResult();
            await last.Released.Task.WaitAsync(Limit);
            Console.SetError(previous);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_task_fault_before_or_after_the_timeout_is_observed(bool faultLate)
    {
        var marker = "unobserved-" + Guid.NewGuid().ToString("N");
        var seen = new ConcurrentQueue<string>();
        EventHandler<UnobservedTaskExceptionEventArgs> observer = (_, e) =>
        {
            // Only this test's marker is counted and marked observed; other exceptions are left alone.
            if (e.Exception.Flatten().InnerExceptions.Any(error => error.Message == marker))
            {
                seen.Enqueue(marker);
                e.SetObserved();
            }
        };
        TaskScheduler.UnobservedTaskException += observer;
        try
        {
            await SettleAFaultingTask(marker, faultLate);
            for (var i = 0; i < 8; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(10);
            }
            seen.Should().BeEmpty("the settle helper observes the original aggregate of every task");
        }
        finally { TaskScheduler.UnobservedTaskException -= observer; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task SettleAFaultingTask(string marker, bool faultLate)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resource = new Tracked();
        var prior = faultLate ? Task.CompletedTask : Task.FromException(new IOException(marker));
        try
        {
            var settle = () => TestTaskSettle.SettleAsync([prior, gate.Task], Deadline, resource);
            await settle.Should().ThrowAsync<TimeoutException>();
        }
        finally
        {
            if (faultLate) gate.TrySetException(new IOException(marker));
            else gate.TrySetResult();
            await resource.Released.Task.WaitAsync(Limit);
        }
    }
}
