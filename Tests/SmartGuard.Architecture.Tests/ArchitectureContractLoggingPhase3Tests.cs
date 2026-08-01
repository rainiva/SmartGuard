using FluentAssertions;

namespace SmartGuard.Architecture.Tests;

public class ArchitectureContractLoggingPhase3Tests
{
  private static string ContractContent =>
    SourceScanHelper.ReadSource("docs/ARCHITECTURE-CONTRACT.md");

  [Fact]
  public void Contract_registers_crash_ring_buffer()
  {
    ContractContent.Should().Contain("CrashRingBuffer");
    ContractContent.Should().Contain("EmergencySyncWriter");
  }

  [Fact]
  public void Contract_registers_recovery_state_machine()
  {
    ContractContent.Should().Contain("LogRecoveryStateMachine");
    ContractContent.Should().Contain("LogConsumerRecovery");
  }

  [Fact]
  public void Contract_registers_deduplication_filter()
  {
    ContractContent.Should().Contain("LogDeduplicationFilter");
  }

  [Fact]
  public void Contract_registers_backpressure_controller()
  {
    ContractContent.Should().Contain("LogBackpressureController");
  }

  [Fact]
  public void Contract_registers_operation_id_context()
  {
    ContractContent.Should().Contain("OperationIdContext");
  }
}
