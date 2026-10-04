using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Architecture.Tests;

public class LoggingRepairArchitectureTests
{
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
