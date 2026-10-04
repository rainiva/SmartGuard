using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Architecture.Tests;

public class LoggingRepairArchitectureTests
{
  [Fact]
  public void Diagnostic_evidence_uses_bounded_tail_reader_and_atomic_publication()
  {
    typeof(DiagnosticBundleRequest).GetProperty("IncludeCrashRingLog").Should().NotBeNull();
    typeof(SmartGuardLogWriter).Assembly.GetType("SmartGuard.Configuration.DiagnosticLogTailReader").Should().NotBeNull();
    SourceScanHelper.ReadSource("src/SmartGuard.Configuration/DiagnosticBundleExporter.cs")
      .Should().NotContain("File.Delete(request.DestinationZipPath)").And.NotContain("EstimatePayloadBytes");
  }

  [Fact]
  public void Supplemental_budget_and_emergency_append_share_the_canonical_writer()
  {
    typeof(SmartGuardLogWriter).GetMethod("TryAppendSupplementalLines").Should().NotBeNull();
    typeof(SmartGuardLogWriter).Assembly.GetType("SmartGuard.Configuration.SupplementalLogPolicy").Should().NotBeNull();
    SourceScanHelper.ReadSource("src/SmartGuard.Configuration/EmergencySyncWriter.cs")
      .Should().NotContain("new FileStream").And.Contain("SmartGuardLogWriter.TryAppendSupplementalLines");
    SourceScanHelper.ReadSource("src/SmartGuard.Configuration/GlobalExceptionHandlers.cs")
      .Should().NotContain("new GuardConfigRepository", "crash callbacks consume a startup snapshot only");
  }

  [Fact]
  public void Unavailable_configuration_retains_only_a_finite_logging_snapshot()
  {
    var snapshot = typeof(SmartGuard.Engine.Worker.GuardianLoop).GetNestedType("LogSettingsSnapshot", System.Reflection.BindingFlags.NonPublic);
    snapshot.Should().NotBeNull();
    snapshot!.IsValueType.Should().BeTrue();
    snapshot!.GetProperties().Select(p => p.Name).Should().BeEquivalentTo("MinimumLevel", "MainPath", "MainRetention", "StartupPath");
    snapshot.GetProperties().Should().NotContain(p => p.PropertyType == typeof(GuardConfig));
    SourceScanHelper.ReadSource("src/SmartGuard.Engine/Worker/GuardianLoop.cs").Should().NotContain("LoadOrDefault");
    SourceScanHelper.ReadSource("src/SmartGuard.Engine/Worker/GuardianLoop.Logging.cs").Should().Contain("primaryAvailable:");
  }

  [Fact]
  public void Rule_observation_events_are_registered_and_owned_by_the_main_loop()
  {
    EventNameRegistry.IsRegistered("ForegroundAdmissionChanged").Should().BeTrue();
    EventNameRegistry.IsRegistered("GuardStateChanged").Should().BeTrue();
    LogEventChannels.ForEventName("ForegroundAdmissionChanged").Should().Be(LogChannelRegistry.Policy);
    LogEventChannels.ForEventName("GuardStateChanged").Should().Be(LogChannelRegistry.Policy);
    SourceScanHelper.ReadSource("src/SmartGuard.Engine/Worker/GuardianLoop.cs")
      .Should().Contain("DrainGuardObservations");
  }

  [Fact]
  public void Detailed_configuration_load_and_rule_fingerprint_have_one_repository_owner()
  {
    var method = typeof(GuardConfigRepository).GetMethod("TryLoadDetailed", Type.EmptyTypes);
    method.Should().NotBeNull();
    method!.ReturnType.FullName.Should().Be("SmartGuard.Configuration.GuardConfigLoadResult");
    SourceScanHelper.ReadSource("src/SmartGuard.Configuration/GuardConfigRepository.cs")
      .Should().Contain("TryLoadDetailed().Config");
  }

  [Fact]
  public void Shared_status_has_one_typed_optional_logging_health_contract()
  {
    var field = typeof(SmartGuard.Contracts.StatusPayload).GetProperty("logHealth");
    field.Should().NotBeNull();
    field!.PropertyType.FullName.Should().Be("SmartGuard.Contracts.LogHealthStatus");
    typeof(LogPipeline).GetMethod("SampleLogHealth").Should().NotBeNull();
    SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsLogViewRefresher.cs")
      .Should().NotContain("LogViewIdleReader.TryRead(", "UI apply 只能消费后台观测结果");
    SourceScanHelper.ReadSource("src/SmartGuard.Settings/SettingsLogPageHost.Refresh.cs")
      .Should().NotContain("LogViewIdleReader.TryRead(");
  }

  [Fact]
  public void Pipeline_write_result_and_flush_confirmation_are_typed_and_registered()
  {
    var result = typeof(LogPipeline).Assembly.GetType("SmartGuard.Configuration.LogBatchWriteResult");
    result.Should().NotBeNull("boolean success cannot express fallback and unconfirmed records");
    result!.GetProperty("PrimaryWritten").Should().NotBeNull();
    result.GetProperty("FallbackWritten").Should().NotBeNull();
    result.GetProperty("Lost").Should().NotBeNull();
    typeof(LogPipeline).GetMethod("FlushAsync")!.ReturnType.Should().Be(typeof(Task<bool>));
    typeof(LogPipelineDiagnostics).GetProperty("LostLines").Should().NotBeNull();
  }

  [Fact]
  public void Daily_cycle_error_handling_has_no_synchronous_file_writer_bypass()
  {
    SourceScanHelper.ReadSource("src/SmartGuard.Engine/Worker/GuardianLoop.Foreground.cs")
      .Should().NotContain("SmartGuardLogWriter.TryAppendLine");
  }

  [Fact]
  public void Configuration_failure_event_and_repository_entry_have_one_registered_owner()
  {
    EventNameRegistry.IsRegistered("ConfigMutationFailed").Should().BeTrue();
    LogEventChannels.ForEventName("ConfigMutationFailed").Should().Be(LogChannelRegistry.Policy);
    typeof(GuardConfigRepository).GetMethod("AppendMutationFailureLog",
      new[] { typeof(string), typeof(string) }).Should().NotBeNull();
  }
}
