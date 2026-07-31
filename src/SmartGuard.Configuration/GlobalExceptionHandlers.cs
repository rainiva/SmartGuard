namespace SmartGuard.Configuration;

public static class GlobalExceptionHandlers
{
  private static int _registered;

  public static void Register(string component, string root)
  {
    if (Interlocked.Exchange(ref _registered, 1) == 1) return; // 幂等：防重复注册导致重复崩溃日志
    AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    {
      if (e.ExceptionObject is Exception ex)
        CrashLogWriter.Write(component, ResolveMainLog(root), ex);
    };

    TaskScheduler.UnobservedTaskException += (_, e) =>
    {
      CrashLogWriter.Write(component, ResolveMainLog(root), e.Exception);
      e.SetObserved();
    };
  }

  private static string? ResolveMainLog(string root)
  {
    try
    {
      var repository = new GuardConfigRepository(SmartGuardPaths.ConfigFile(root));
      var config = repository.LoadOrDefault(root);
      return SmartGuardPaths.ResolveLogFile(config, root);
    }
    catch
    {
      return null; // 无法解析主日志时仅写应急崩溃文件
    }
  }
}
