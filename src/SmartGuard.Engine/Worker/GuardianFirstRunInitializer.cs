using SmartGuard.Configuration;
using SmartGuard.Contracts;
using SmartGuard.Engine.Infrastructure;

namespace SmartGuard.Engine.Worker;

internal sealed class GuardianFirstRunInitializer
{
  private readonly string _initMarkerPath;
  private readonly BrightnessService _brightness;
  private readonly Action<GuardConfig, LogLevel, string> _writeLog;

  internal GuardianFirstRunInitializer(
    string initMarkerPath,
    BrightnessService brightness,
    Action<GuardConfig, LogLevel, string> writeLog)
  {
    _initMarkerPath = initMarkerPath;
    _brightness = brightness;
    _writeLog = writeLog;
  }

  // The brightness capability is a machine fact probed read-only on every start; only the one-time
  // alignment writes sit behind the marker. Returns true when the first-run alignment completed now.
  internal bool InitializeIfNeeded(GuardConfig config, ref bool powerCfgBrightnessSupported)
  {
    var firstRun = !File.Exists(_initMarkerPath);
    if (firstRun) _writeLog(config, LogLevel.Info, "INIT: 开始首次初始化...");
    var (capability, probe) = PowerCfgExecutor.ProbeBrightnessCapability(config.BalancedPlanGuid);
    if (capability == BrightnessCapability.ProbeFailed)
    {
      // Unknown is not "unsupported": keep the current mode, write no marker, retry on the next start.
      _writeLog(config, LogLevel.Warn,
        $"INIT: 亮度设置项探测失败（{PowerCfgExecutor.DescribeSchemeFailure(probe)}），保持当前模式，下次启动重试");
      return false;
    }

    powerCfgBrightnessSupported = capability == BrightnessCapability.Supported;
    if (!firstRun) return false;
    if (powerCfgBrightnessSupported)
    {
      var plans = new[] { config.ActivePlanGuid, config.BalancedPlanGuid, config.PowerSaverPlanGuid };
      PowerSchemeStatus? failure = null;
      foreach (var g in plans)
      {
        var disabled = PowerCfgExecutor.DisableAdaptiveBrightness(g);
        if (!disabled.Succeeded) { failure = disabled; break; }
      }

      var b = failure is null ? _brightness.GetBrightness() : -1;
      if (failure is null && b >= 0)
      {
        foreach (var g in plans)
        {
          var synced = PowerCfgExecutor.SyncPlanBrightness(g, b);
          if (!synced.Succeeded) { failure = synced; break; }
        }
      }

      if (failure is { } failed)
      {
        _writeLog(config, LogLevel.Warn,
          $"INIT: 电源计划亮度设置项写入失败（{PowerCfgExecutor.DescribeSchemeFailure(failed)}），未写完成标记，下次启动重试");
        return false;
      }
      if (b >= 0) _writeLog(config, LogLevel.Info, $"INIT: 三个电源计划的亮度设置项已对齐为 {b}%");
    }
    else
    {
      _writeLog(config, LogLevel.Info, "INIT: 本机电源计划不含亮度设置项，已切换为 WMI-only 模式（切计划后写回亮度）");
    }

    _writeLog(config, LogLevel.Info, "INIT: 请手动关闭 CABC 与节电模式降亮度");
    File.WriteAllText(_initMarkerPath, DateTime.Now.ToString("s"));
    _writeLog(config, LogLevel.Info, "INIT: 首次初始化完成");
    return true;
  }
}
