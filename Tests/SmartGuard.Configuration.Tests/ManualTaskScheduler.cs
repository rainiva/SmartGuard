using System.Collections.Concurrent;

namespace SmartGuard.Configuration.Tests;

internal sealed class ManualTaskScheduler : TaskScheduler
{
  private readonly ConcurrentQueue<Task> _queue = new();

  public int RunPending()
  {
    var count = 0;
    while (_queue.TryDequeue(out var task)) { TryExecuteTask(task); count++; }
    return count;
  }

  protected override void QueueTask(Task task) => _queue.Enqueue(task);
  protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;
  protected override IEnumerable<Task> GetScheduledTasks() => _queue.ToArray();
}
