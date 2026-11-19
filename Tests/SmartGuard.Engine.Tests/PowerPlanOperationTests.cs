using SmartGuard.Configuration;
using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Tests;

[Collection("LoggingFallbackTests")]
public class PowerPlanOperationTests
{
  private static readonly Guid High = PowerPlanCatalogProvider.HighPerformancePlanGuid;
  private static readonly Guid Balanced = PowerPlanCatalogProvider.BalancedPlanGuid;

  // Theory parameters are strings: a public test method cannot expose the internal enum (CS0051).
  [Theory]
  [InlineData("api-error", 5, "ApiFailed")]
  [InlineData("timed-out", null, "TimedOut")]
  [InlineData("call-pending", null, "CallPending")]
  [InlineData("not-admitted", null, "NotAdmitted")]
  public void Scheme_failures_map_to_truthful_plan_failure_kinds(string kind, int? code, string expected)
    => PowerCfgExecutor.MapFailureKind(new PowerSchemeStatus(kind, code, 0)).Should().Be(Enum.Parse<PowerPlanFailureKind>(expected));

  [Theory]
  [InlineData("ApiFailed", 5, "电源服务调用失败（错误码 5）")]
  [InlineData("ApiFailed", null, "电源服务调用失败")]
  [InlineData("CallPending", null, "上次电源服务调用仍未返回，本轮未发起")]
  [InlineData("TimedOut", null, "电源服务调用超时")]
  [InlineData("VerificationMismatch", null, "切换后读回的计划不一致")]
  [InlineData("NotAdmitted", null, "电源状态已变化，本次未执行")]
  public void Failure_descriptions_are_chinese_labels(string kind, int? code, string expected)
    => PowerCfgExecutor.DescribeFailure(Enum.Parse<PowerPlanFailureKind>(kind), code).Should().Be(expected);

  [Fact]
  public async Task Probe_reads_active_scheme_and_name_through_the_scheme_service()
  {
    var native = new FakePowrProfNative { GetActive = () => (0, High), ReadName = _ => (0, "高性能") };
    var statuses = new List<PowerSchemeStatus>();
    PowerPlanProbeResult probe = default;
    await PowerSchemeTestScope.WithNative(native, async () =>
      probe = await PowerCfgExecutor.ProbeCurrentPlanAsync(CancellationToken.None, statuses.Add));
    probe.Succeeded.Should().BeTrue();
    probe.Plan.Should().Be(new PowerSchemeInfo(High, "高性能"));
    statuses.Should().ContainSingle().Which.Succeeded.Should().BeTrue();
  }

  [Fact]
  public async Task Probe_failure_carries_the_win32_code_and_skips_the_name()
  {
    var native = new FakePowrProfNative { GetActive = () => (5, Guid.Empty) };
    PowerPlanProbeResult probe = default;
    // Named argument: a single-argument call would bind the legacy powercfg overload until Task 7.
    await PowerSchemeTestScope.WithNative(native, async () =>
      probe = await PowerCfgExecutor.ProbeCurrentPlanAsync(CancellationToken.None, observe: null));
    probe.Succeeded.Should().BeFalse();
    probe.FailureKind.Should().Be(PowerPlanFailureKind.ApiFailed);
    probe.Win32Error.Should().Be(5);
    native.Calls.Should().Equal("get-active");
  }

  [Fact]
  public async Task Missing_name_does_not_fail_the_probe()
  {
    var native = new FakePowrProfNative { GetActive = () => (0, High), ReadName = _ => (2, string.Empty) };
    PowerPlanProbeResult probe = default;
    await PowerSchemeTestScope.WithNative(native, async () =>
      probe = await PowerCfgExecutor.ProbeCurrentPlanAsync(CancellationToken.None, observe: null));
    probe.Succeeded.Should().BeTrue();
    probe.Plan.Should().Be(new PowerSchemeInfo(High, null));
  }

  [Fact]
  public async Task Apply_is_successful_only_when_followup_probe_matches_target()
  {
    var recorder = new SchemeRecorder(Ok(true), Active(Balanced), Active(Balanced), Active(Balanced));
    var result = await PowerCfgExecutor.ApplyAndVerifyAsync(High, recorder.SetActiveAsync, recorder.GetActiveAsync,
      (_, _) => Task.CompletedTask, CancellationToken.None);
    result.Succeeded.Should().BeFalse();
    result.ObservedGuid.Should().Be(Balanced);
    result.FailureKind.Should().Be(PowerPlanFailureKind.VerificationMismatch);
    recorder.Calls.Should().Equal($"set {High:D}", "get", "get", "get");
  }

  [Fact]
  public async Task Apply_verification_accepts_bounded_delayed_convergence()
  {
    var delays = new List<TimeSpan>();
    var recorder = new SchemeRecorder(Ok(true), Active(Balanced), Active(High));
    var result = await PowerCfgExecutor.ApplyAndVerifyAsync(High, recorder.SetActiveAsync, recorder.GetActiveAsync,
      (delay, _) => { delays.Add(delay); return Task.CompletedTask; }, CancellationToken.None);
    result.Succeeded.Should().BeTrue();
    delays.Should().Equal(TimeSpan.FromMilliseconds(250));
    recorder.Calls.Should().Equal($"set {High:D}", "get", "get");
  }

  [Fact]
  public async Task Rejected_set_active_reports_its_win32_code_without_probing()
  {
    var recorder = new SchemeRecorder(Failed<bool>("api-error", 5));
    var result = await PowerCfgExecutor.ApplyAndVerifyAsync(High, recorder.SetActiveAsync, recorder.GetActiveAsync,
      (_, _) => Task.CompletedTask, CancellationToken.None);
    result.FailureKind.Should().Be(PowerPlanFailureKind.ApiFailed);
    result.Win32Error.Should().Be(5);
    recorder.Calls.Should().Equal($"set {High:D}");
  }

  [Fact]
  public async Task Set_active_refused_by_admission_is_not_admitted_without_probing()
  {
    var recorder = new SchemeRecorder(Failed<bool>("not-admitted", null));
    var result = await PowerCfgExecutor.ApplyAndVerifyAsync(High, recorder.SetActiveAsync, recorder.GetActiveAsync,
      (_, _) => Task.CompletedTask, CancellationToken.None);
    result.Succeeded.Should().BeFalse();
    result.FailureKind.Should().Be(PowerPlanFailureKind.NotAdmitted);
    recorder.Calls.Should().Equal($"set {High:D}");
  }

  private static PowerSchemeResult<bool> Ok(bool value) => new(new("none", null, 0), value);
  private static PowerSchemeResult<Guid> Active(Guid scheme) => new(new("none", null, 0), scheme);
  private static PowerSchemeResult<T> Failed<T>(string kind, int? code) => new(new(kind, code, 0), default);

  private sealed class SchemeRecorder(params object[] results)
  {
    private readonly Queue<object> _results = new(results);
    public List<string> Calls { get; } = [];

    public Task<PowerSchemeResult<bool>> SetActiveAsync(Guid scheme, CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();
      Calls.Add($"set {scheme:D}");
      return Task.FromResult((PowerSchemeResult<bool>)_results.Dequeue());
    }

    public Task<PowerSchemeResult<Guid>> GetActiveAsync(CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();
      Calls.Add("get");
      return Task.FromResult((PowerSchemeResult<Guid>)_results.Dequeue());
    }
  }
}
