using FluentAssertions;
using SmartGuard.Configuration;
using SmartGuard.Settings;
using System.Windows;
using System.Windows.Controls;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public class SettingsDiagnosticExportCoordinatorTests
{
  [Fact]
  public void Export_returns_to_dispatcher_before_slow_work_finishes()
  {
    WpfStaTestHost.Run(() =>
    {
      var dir = CreateTempDir();
      try
      {
        using var session = new SettingsDiagnosticExportSession();
        var coordinator = new SettingsDiagnosticExportCoordinator(dir, () => "test", (_, _) => { }, () => Paths(dir), session,
          export: (_, token) => Task.Delay(300, token));
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var flight = coordinator.ExportToPath(Path.Combine(dir, "slow.zip"));
        try { timer.ElapsedMilliseconds.Should().BeLessThan(100); }
        finally { session.Cancel(); WpfStaTestHost.WaitUntil(() => flight.IsCompleted, TimeSpan.FromSeconds(5), "export cleanup"); }
      }
      finally { Directory.Delete(dir, true); }
    });
  }

  [Fact]
  public void ExportToPath_creates_sanitized_diagnostic_zip()
  {
    WpfStaTestHost.Run(() =>
    {
      var dir = CreateTempDir();
      var configPath = SmartGuardPaths.ConfigFile(dir);
      var repository = new GuardConfigRepository(configPath);
      var config = GuardConfig.CreateDefault(dir);
      repository.Save(config);
      File.WriteAllText(configPath, """
        {
          "BalancedThresholdSec": 300,
          "GitHubToken": "ghp_diagnosticExportToken1234567890"
        }
        """);

      var zipPath = Path.Combine(dir, "export.zip");
      string? toastMessage = null;
      using var session = new SettingsDiagnosticExportSession();
      var coordinator = new SettingsDiagnosticExportCoordinator(
        dir,
        () => "1.1.0",
        (message, _) => toastMessage = message,
        () => Paths(dir), session, export: ExportIsolated);

      var flight = coordinator.ExportToPath(zipPath);
      WpfStaTestHost.WaitUntil(() => flight.IsCompleted, TimeSpan.FromSeconds(5), "diagnostic export");

      File.Exists(zipPath).Should().BeTrue();
      toastMessage.Should().Be("诊断包已保存");

      using var archive = System.IO.Compression.ZipFile.OpenRead(zipPath);
      var configEntry = archive.GetEntry("config/SmartGuard.config.json");
      configEntry.Should().NotBeNull();
      using var reader = new StreamReader(configEntry!.Open());
      var json = reader.ReadToEnd();
      json.Should().NotContain("ghp_diagnosticExportToken1234567890");
      json.Should().NotContain("GitHubTokenProtected");
      json.Should().NotContain("\"GitHubToken\"");
    });
  }

  [Fact]
  public void WireAboutPage_invokes_export_when_destination_picked()
  {
    WpfStaTestHost.Run(() =>
    {
      var dir = CreateTempDir();
      var zipPath = Path.Combine(dir, "picked.zip");
      using var session = new SettingsDiagnosticExportSession();
      var window = new Window();
      var button = new Button();
      var coordinator = new SettingsDiagnosticExportCoordinator(
        dir,
        () => "1.1.0",
        (_, _) => { },
        () => Paths(dir), session,
        pickDestinationPath: _ => zipPath, export: ExportIsolated);

      coordinator.WireAboutPage(window, button);
      button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
      WpfStaTestHost.WaitUntil(() => !session.IsRunning, TimeSpan.FromSeconds(5), "button export");
      File.Exists(zipPath).Should().BeTrue();
    });
  }

  private static (string, string) Paths(string dir) => (SmartGuardPaths.DefaultLogFile(dir), SmartGuardPaths.StartupLogFile(dir));
  private static Task ExportIsolated(DiagnosticBundleRequest request, CancellationToken token)
    => DiagnosticBundleExporter.ExportAsync(request with { IncludeEmergencyLog = false, IncludeCrashLog = false, IncludeCrashRingLog = false }, cancellationToken: token);

  private static string CreateTempDir()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    return dir;
  }
}
