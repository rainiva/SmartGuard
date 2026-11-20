namespace SmartGuard.Configuration;

/// <summary>
/// 故障注入测试框架：测试专用钩子，可注入各类故障场景。
/// 不修改生产代码路径，仅通过 [InternalsVisibleTo] 供测试访问。
/// 注入的故障标志由测试断言消费，生产路径不检查这些标志。
/// </summary>
internal sealed class LogFailureInjector
{
  public bool IsDiskFullInjected { get; private set; }
  public bool IsPathDeniedInjected { get; private set; }
  public bool IsFileLockedInjected { get; private set; }
  public bool IsConsumerExceptionInjected { get; private set; }

  public void InjectDiskFull() => IsDiskFullInjected = true;
  public void InjectPathPermissionDenied() => IsPathDeniedInjected = true;
  public void InjectFileLocked() => IsFileLockedInjected = true;
  public void InjectConsumerException() => IsConsumerExceptionInjected = true;

  public void Reset()
  {
    IsDiskFullInjected = false;
    IsPathDeniedInjected = false;
    IsFileLockedInjected = false;
    IsConsumerExceptionInjected = false;
  }
}
