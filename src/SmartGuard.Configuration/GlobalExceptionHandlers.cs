namespace SmartGuard.Configuration;

public static class GlobalExceptionHandlers
{
  private static int _registered;

  public static void Register(string component, string root, GuardConfig? startupConfig = null)
  {
    if (Interlocked.Exchange(ref _registered, 1) == 1) return; // 幂等：防重复注册导致重复崩溃日志
    // Capture only immutable log settings from an already loaded startup
    // context. Fault callbacks must not read or synthesize configuration.
    var mainLog = startupConfig is null ? null : SmartGuardPaths.ResolveLogFile(startupConfig, root);
    var retention = startupConfig is null ? null : LogRetentionPolicy.FromGuardConfig(startupConfig);
    AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    {
      if (e.ExceptionObject is Exception ex)
      {
        try { CrashRingBuffer.Instance.FlushToSync(CrashRingBuffer.CrashRingLogFile); } catch { } // 崩溃路径极简，全异常隔离
        CrashLogWriter.Write(component, mainLog, ex, retention);
      }
    };

    TaskScheduler.UnobservedTaskException += (_, e) =>
    {
      try { CrashRingBuffer.Instance.FlushToSync(CrashRingBuffer.CrashRingLogFile); } catch { }
      CrashLogWriter.Write(component, mainLog, e.Exception, retention);
      e.SetObserved();
    };
  }

}
