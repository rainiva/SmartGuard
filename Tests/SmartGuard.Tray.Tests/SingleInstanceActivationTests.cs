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

    var serverTask = Task.Run(() =>
      SingleInstanceActivation.RunActivationServer(component, () => signaled.Set(), cts.Token));

    Thread.Sleep(100);
    SingleInstanceActivation.TryNotifyExisting(component, timeoutMs: 1000).Should().BeTrue();
    signaled.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();

    cts.Cancel();
    serverTask.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
  }
}
