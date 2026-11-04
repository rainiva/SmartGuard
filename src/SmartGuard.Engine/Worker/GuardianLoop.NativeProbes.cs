using System.Text.Json;
using SmartGuard.Configuration;
using SmartGuard.Engine.Domain;
using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Worker;

public sealed partial class GuardianLoop
{
  private void ObserveNativeProbe(WmiProbeObservation observation)
  {
    var level = observation.Recovered ? LogLevel.Info : LogLevel.Warn;
    // A probe failure must not perform another configuration read or disk write.
    // The existing consumer owns persistence, fallback and the crash-ring boundary.
    if (LogLevelFilter.ShouldWrite(level, CaptureLogSettings(null).MinimumLevel))
      EnqueueLog(level, CreateNativeProbeEvent(observation));
  }

  internal static LogEvent CreateNativeProbeEvent(WmiProbeObservation observation)
  {
    var label = observation.Operation switch
    {
      "brightness-read" => "亮度读取", "brightness-write" => "亮度写入", "brightness-restore" => "亮度恢复流程",
      "battery-read" => "电池补充探测", "plan-catalog" => "电源计划名称加载",
      "plan-read" => "当前电源计划读取", "plan-apply" => "电源计划切换命令执行",
      "plan-brightness-ac" => "插电亮度同步", "plan-brightness-dc" => "电池亮度同步",
      _ => "系统探测",
    };
    var degradation = observation.Operation switch
    {
      "brightness-read" => "亮度标记为不可用，继续守护",
      "brightness-write" => "亮度写入结果无法确认，不自动补发超时操作",
      "brightness-restore" => "停止本次亮度恢复，继续最终计划观测",
      "battery-read" => "保留原生供电状态，不依赖失败的WMI补充",
      "plan-catalog" => "使用已有计划标识和默认名称，继续守护",
      "plan-brightness-ac" or "plan-brightness-dc" => "停止本次可选亮度同步",
      _ => "本次计划操作未确认，等待下一轮重新探测",
    };
    var cleanup = observation.TerminationConfirmed ? "子进程已退出或未启动" : "清理未确认，探测启动资格已关闭";
    var message = observation.Recovered ? $"系统探测已恢复：{label}。此前合并省略 {observation.SuppressedCount} 次同类异常。"
      : $"系统探测{(observation.FailureKind == "cancelled" ? "已取消" : "异常")}：{label}；{observation.FailureKind}；耗时 {observation.ElapsedMilliseconds}ms，预算 {observation.BudgetMilliseconds}ms；{degradation}；{cleanup}。合并省略 {observation.SuppressedCount} 次同类请求。";
    return EngineLogEvents.Create(observation.Recovered ? LogLevel.Info : LogLevel.Warn,
      observation.Recovered ? EventNameRegistry.NativeProbeRecovered : EventNameRegistry.NativeProbeFailed,
      message, OperationIdContext.Current, TraceIdContext.Current,
      properties: new Dictionary<string, JsonElement>
      {
        ["operation"] = JsonSerializer.SerializeToElement(observation.Operation),
        ["failureKind"] = JsonSerializer.SerializeToElement(observation.FailureKind),
        ["elapsedMs"] = JsonSerializer.SerializeToElement(observation.ElapsedMilliseconds),
        ["budgetMs"] = JsonSerializer.SerializeToElement(observation.BudgetMilliseconds),
        ["terminationConfirmed"] = JsonSerializer.SerializeToElement(observation.TerminationConfirmed),
        ["exitCode"] = JsonSerializer.SerializeToElement(observation.ExitCode),
        ["suppressedCount"] = JsonSerializer.SerializeToElement(observation.SuppressedCount),
        ["recovered"] = JsonSerializer.SerializeToElement(observation.Recovered),
        ["recoveryScope"] = JsonSerializer.SerializeToElement(observation.Operation == "plan-apply" ? "command-execution" : "probe"),
        ["actionResult"] = JsonSerializer.SerializeToElement(observation.Recovered ? "probe-recovered" : "unconfirmed"),
        ["degradation"] = JsonSerializer.SerializeToElement(degradation),
      });
  }
}
