using SmartGuard.Contracts;

namespace SmartGuard.Tray.Tests;

public class SingleInstanceActivationTests
{
  [Fact]
  public void TryNotifyExisting_returns_false_when_no_listener()
  {
    var component = "Test-" + Guid.NewGuid().ToString("N");

    SingleInstanceActivation.TryNotifyExisting(component, timeoutMs: 200)
      .Should().BeFalse();
  }

  [Fact]
  public void RunActivationServer_invokes_callback_when_notified()
  {
    var component = "Test-" + Guid.NewGuid().ToString("N");
    using var cts = new CancellationTokenSource();
    var signaled = new ManualResetEventSlim(false);

    // A dedicated thread, as the server blocks for its whole life; on a loaded runner a thread-pool
    // task may not create the pipe in time. The connect timeout is the readiness wait, so no fixed sleep.
    var serverTask = Task.Factory.StartNew(
      () => SingleInstanceActivation.RunActivationServer(component, () => signaled.Set(), cts.Token),
      CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    SingleInstanceActivation.TryNotifyExisting(component, timeoutMs: 10000).Should().BeTrue();
    signaled.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

    cts.Cancel();
    serverTask.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
  }
}
