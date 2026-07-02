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

  internal bool InitializeIfNeeded(GuardConfig config, ref bool powerCfgBrightnessSupported)
  {
    if (File.Exists(_initMarkerPath)) return false;

    _writeLog(config, LogLevel.Info, "INIT: 开始首次初始化...");
    powerCfgBrightnessSupported = PowerCfgExecutor.IsBrightnessSupported(config.BalancedPlanGuid);
    if (powerCfgBrightnessSupported)
    {
      foreach (var g in new[] { config.ActivePlanGuid, config.BalancedPlanGuid, config.PowerSaverPlanGuid })
        PowerCfgExecutor.DisableAdaptiveBrightness(g);

      var b = _brightness.GetBrightness();
      if (b >= 0)
      {
        foreach (var g in new[] { config.ActivePlanGuid, config.BalancedPlanGuid, config.PowerSaverPlanGuid })
          PowerCfgExecutor.SyncPlanBrightness(g, b);
        _writeLog(config, LogLevel.Info, $"INIT: powercfg 三计划亮度对齐为 {b}%");
      }
    }
    else
    {
      _writeLog(config, LogLevel.Info, "INIT: 本机不支持 powercfg 亮度项，已切换为 WMI-only 模式（切计划后写回亮度）");
    }

    _writeLog(config, LogLevel.Info, "INIT: 请手动关闭 CABC 与节电模式降亮度");
    File.WriteAllText(_initMarkerPath, DateTime.Now.ToString("s"));
    _writeLog(config, LogLevel.Info, "INIT: 首次初始化完成");
    return true;
  }
}
