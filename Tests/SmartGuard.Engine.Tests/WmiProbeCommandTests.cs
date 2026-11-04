using SmartGuard.Engine.Infrastructure;
using SmartGuard.Configuration;

namespace SmartGuard.Engine.Tests;

public class WmiProbeCommandTests
{
  [Fact]
  public async Task Child_deadline_exits_independently_and_disposal_disarms_it()
  {
    var start = typeof(WmiProbeCommand).GetMethod("StartDeadline", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
    Assert.NotNull(start);
    var expired = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
    using ((IDisposable)start.Invoke(null, new object[] { (Action<int>)expired.SetResult, TimeSpan.FromMilliseconds(30) })!)
      Assert.Equal(124, await expired.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    var calls = 0;
    using ((IDisposable)start.Invoke(null, new object[] { (Action<int>)(_ => Interlocked.Increment(ref calls)), TimeSpan.FromMilliseconds(100) })!) { }
    await Task.Delay(150);
    Assert.Equal(0, calls);
  }

  private readonly Xunit.Abstractions.ITestOutputHelper _output;
  public WmiProbeCommandTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

  [Theory]
  [InlineData("brightness-read")]
  [InlineData("battery-read")]
  public async Task Real_read_only_child_returns_small_versioned_reply(string operation)
  {
    var executable = Path.ChangeExtension(typeof(Program).Assembly.Location, ".exe");
    var result = await PowerCfgProcessRunner.RunProcessAsync(executable,
      "--wmi-probe " + operation, TimeSpan.FromSeconds(2));
    _output.WriteLine($"operation={operation}; failure={result.FailureKind}; elapsedMs={result.ElapsedMilliseconds}; terminated={result.TerminationConfirmed}; exit={result.ExitCode}; reply={result.StandardOutput.Trim()}");
    Assert.True(result.Started);
    Assert.False(result.TimedOut);
    Assert.True(result.TerminationConfirmed);
    Assert.StartsWith("SGWMI1|", result.StandardOutput);
    Assert.InRange(result.StandardOutput.Length, 1, 128);
    Assert.Equal("", result.StandardError);
    // A machine without this provider can report unavailable; it must still
    // complete the internal child protocol without starting a guardian.
    Assert.Contains(result.ExitCode, new int?[] { 0, 3 });
  }

  [Theory]
  [InlineData("brightness-write", "101")]
  [InlineData("brightness-write", "-1")]
  [InlineData("brightness-write", "50 & anything")]
  [InlineData("arbitrary-path", "file")]
  public void Invalid_probe_arguments_exit_before_guardian_initialization(string operation, string value)
  {
    // Invalid inputs never reach WMI, root resolution or guardian initialization.
    Assert.Equal(2, Program.Main(["--wmi-probe", operation, value]));
  }
}
