using FluentAssertions;
using SmartGuard.Configuration;
using SmartGuard.Settings;
using SmartGuard.Settings.LogView;
using System.Windows;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public sealed class SettingsDiagnosticExportLifetimeTests
{
  [Fact]
  public void Both_coordinators_share_admission_and_logs_use_an_independent_reader()
  {
    WpfStaTestHost.Run(() =>
    {
      var root = Path.Combine(Path.GetTempPath(), "SmartGuardTwoExports_" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(root);
      using var session = new SettingsDiagnosticExportSession();
      var window = new Window(); using var toast = new ToastNotificationService(window);
      var entered = new TaskCompletionSource<DiagnosticBundleRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
      var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var about = new SettingsDiagnosticExportCoordinator(root, () => "test", (_, _) => { },
        () => (SmartGuardPaths.DefaultLogFile(root), SmartGuardPaths.StartupLogFile(root)), session,
        export: (request, _) => { entered.TrySetResult(request); return release.Task; });
      var logCalls = 0;
      DiagnosticBundleRequest? logged = null;
      var logs = new SettingsLogExportCoordinator(window, root, toast, () => false,
        () => null, () => null, () => null, () => null, () => null, () => null, () => new LogCorrelationCoordinator(),
        SmartGuardPaths.DefaultLogFile(root), SmartGuardPaths.StartupLogFile(root), diagnosticSession: session, isAlive: () => false,
        exportDiagnostic: (request, _) => { Interlocked.Increment(ref logCalls); logged = request; return Task.CompletedTask; });
      var flight = about.ExportToPath(Path.Combine(root, "about.zip"));
      try
      {
        WpfStaTestHost.WaitUntil(() => entered.Task.IsCompleted, TimeSpan.FromSeconds(5), "about worker");
        var request = entered.Task.Result;
        var rejected = logs.ExportDiagnosticToPath(Path.Combine(root, "logs.zip"), request);
        rejected.IsCompleted.Should().BeTrue(); logCalls.Should().Be(0);
        session.Cancel(DiagnosticExportOrigin.About);
        logs.ExportDiagnosticToPath(Path.Combine(root, "still-busy.zip"), request).IsCompleted.Should().BeTrue();
        logCalls.Should().Be(0);
        release.SetResult(); WpfStaTestHost.WaitUntil(() => flight.IsCompleted, TimeSpan.FromSeconds(5), "about exit");
        var logFlight = logs.ExportDiagnosticToPath(Path.Combine(root, "logs.zip"), request);
        WpfStaTestHost.WaitUntil(() => logFlight.IsCompleted, TimeSpan.FromSeconds(5), "log exit");
        logCalls.Should().Be(1); logged!.EventSource.Should().NotBeSameAs(request.EventSource);
      }
      finally
      {
        session.Dispose(); release.TrySetResult();
        WpfStaTestHost.WaitUntil(() => flight.IsCompleted && !session.IsRunning, TimeSpan.FromSeconds(5), "two exports cleanup");
        Directory.Delete(root, true);
      }
    });
  }

  [Fact]
  public async Task Cancel_keeps_busy_until_worker_exit_and_matches_origin()
  {
    using var session = new SettingsDiagnosticExportSession();
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    CancellationToken observed = default;
    var flight = session.RunAsync(DiagnosticExportOrigin.About, token => { observed = token; return release.Task; });
    session.Cancel(DiagnosticExportOrigin.Logs);
    observed.IsCancellationRequested.Should().BeFalse();
    session.Cancel(DiagnosticExportOrigin.About);
    observed.IsCancellationRequested.Should().BeTrue();
    var secondCalls = 0;
    await session.RunAsync(DiagnosticExportOrigin.Logs, _ => { secondCalls++; return Task.CompletedTask; });
    secondCalls.Should().Be(0); session.IsRunning.Should().BeTrue();
    release.SetResult(); await flight;
    session.IsRunning.Should().BeFalse(); session.ActiveOrigin.Should().BeNull();
    await session.RunAsync(DiagnosticExportOrigin.Logs, _ => { secondCalls++; return Task.CompletedTask; });
    secondCalls.Should().Be(1);
  }

  [Fact]
  public async Task Failure_releases_flight_and_other_window_is_independent()
  {
    using var first = new SettingsDiagnosticExportSession();
    using var second = new SettingsDiagnosticExportSession();
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    CancellationToken secondToken = default;
    var flight = second.RunAsync(DiagnosticExportOrigin.Logs, token => { secondToken = token; return release.Task; });
    first.Dispose(); secondToken.IsCancellationRequested.Should().BeFalse();
    release.SetResult(); await flight;
    var fail = () => second.RunAsync(DiagnosticExportOrigin.About, _ => throw new IOException("fixture"));
    await fail.Should().ThrowAsync<IOException>();
    second.IsRunning.Should().BeFalse();
  }

  [Fact]
  public void About_capture_is_pure_worker_preparation_is_off_dispatcher_and_close_ignores_result()
  {
    WpfStaTestHost.Run(() =>
    {
      var root = Path.Combine(Path.GetTempPath(), "SmartGuardDiagnosticFlight_" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(root);
      using var session = new SettingsDiagnosticExportSession();
      var window = new Window();
      using var toast = new ToastNotificationService(window);
      var host = new SettingsLogPageHost(window, root, toast, SmartGuardPaths.DefaultLogFile(root), null, session);
      var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var entered = new TaskCompletionSource<DiagnosticBundleRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
      var dispatcherThread = Environment.CurrentManagedThreadId;
      var workerThread = dispatcherThread;
      var toastCalls = 0;
      CancellationToken observed = default;
      var coordinator = new SettingsDiagnosticExportCoordinator(root, () => "test", (_, _) => toastCalls++, host.GetDiagnosticPaths, session,
        export: (request, token) => { workerThread = Environment.CurrentManagedThreadId; observed = token; entered.SetResult(request); return release.Task; });
      var flight = coordinator.ExportToPath(Path.Combine(root, "bundle.zip"));
      try
      {
        WpfStaTestHost.WaitUntil(() => entered.Task.IsCompleted, TimeSpan.FromSeconds(5), "worker entry");
        host.IsLogViewInitializedForTests.Should().BeFalse();
        workerThread.Should().NotBe(dispatcherThread);
        var request = entered.Task.Result;
        request.IncludeStartupLog.Should().BeTrue(); request.IncludeEmergencyLog.Should().BeTrue();
        request.IncludeCrashLog.Should().BeTrue(); request.IncludeCrashRingLog.Should().BeTrue();
        session.Dispose(); observed.IsCancellationRequested.Should().BeTrue();
        release.SetResult();
        WpfStaTestHost.WaitUntil(() => flight.IsCompleted, TimeSpan.FromSeconds(5), "closed flight");
        toastCalls.Should().Be(0); session.IsRunning.Should().BeFalse();
      }
      finally
      {
        session.Dispose(); release.TrySetResult();
        WpfStaTestHost.WaitUntil(() => flight.IsCompleted, TimeSpan.FromSeconds(5), "flight cleanup");
        host.Dispose(); Directory.Delete(root, true);
      }
    });
  }
}
