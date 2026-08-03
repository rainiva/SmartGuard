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
  public void ExportToPath_creates_sanitized_diagnostic_zip()
  {
    WpfStaTestHost.Run(() =>
    {
      var dir = CreateTempDir();
      var configPath = SmartGuardPaths.ConfigFile(dir);
      var repository = new GuardConfigRepository(configPath);
      var config = GuardConfig.CreateDefault(dir);
      config.GitHubToken = "ghp_diagnosticExportToken1234567890";
      repository.Save(config);

      var zipPath = Path.Combine(dir, "export.zip");
      string? toastMessage = null;
      var eventSource = new TestLogEventSource([
        CreateEvent("startup ok", DateTimeOffset.UtcNow),
      ]);
      var coordinator = new SettingsDiagnosticExportCoordinator(
        dir,
        () => "1.1.0",
        (message, _) => toastMessage = message,
        () => eventSource);

      coordinator.ExportToPath(zipPath);

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
      var eventSource = new TestLogEventSource([
        CreateEvent("picked", DateTimeOffset.UtcNow),
      ]);
      var window = new Window();
      var button = new Button();
      var coordinator = new SettingsDiagnosticExportCoordinator(
        dir,
        () => "1.1.0",
        (_, _) => { },
        () => eventSource,
        pickDestinationPath: _ => zipPath);

      coordinator.WireAboutPage(window, button);
      button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

      File.Exists(zipPath).Should().BeTrue();
    });
  }

  private static LogEvent CreateEvent(string message, DateTimeOffset timestamp)
    => new(
      LogEvent.CurrentSchemaVersion,
      Guid.NewGuid().ToString("N"),
      timestamp,
      LogSeverity.Info,
      "Engine",
      "Lifecycle",
      "AppStarted",
      message,
      null,
      null,
      null,
      null,
      null,
      null,
      null,
      LogEvent.EmptyProperties);

  private sealed class TestLogEventSource : ILogEventSource
  {
    private readonly IReadOnlyList<LogEvent> _events;

    internal TestLogEventSource(IReadOnlyList<LogEvent> events) => _events = events;

    public Task<LogEventPage> ReadLatestAsync(LogReadRequest request, CancellationToken cancellationToken)
      => Task.FromResult(LogEventPage.Empty);

    public Task<LogEventPage> ReadOlderAsync(LogReadRequest request, CancellationToken cancellationToken)
      => Task.FromResult(LogEventPage.Empty);

    public Task<LogEventPage> ReadNewerAsync(LogReadRequest request, CancellationToken cancellationToken)
      => Task.FromResult(LogEventPage.Empty);

    public async IAsyncEnumerable<LogEvent> StreamAsync(
      LogReadScope scope,
      [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
      foreach (var logEvent in _events)
      {
        yield return logEvent;
        await Task.Yield();
      }
    }
  }

  private static string CreateTempDir()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    return dir;
  }
}
