using System.Collections.Concurrent;

namespace SmartGuard.Configuration;

/// <summary>
/// 组件注册表：标识日志来源进程/子系统。
/// </summary>
public static class LogComponentRegistry
{
  private static readonly ConcurrentDictionary<string, bool> Registry = new(StringComparer.Ordinal);

  public const string Engine = nameof(Engine);
  public const string Tray = nameof(Tray);
  public const string Settings = nameof(Settings);
  public const string Cli = nameof(Cli);
  public const string Installer = nameof(Installer);
  public const string Configuration = nameof(Configuration);
  public const string Legacy = nameof(Legacy);

  static LogComponentRegistry()
  {
    Register(Engine);
    Register(Tray);
    Register(Settings);
    Register(Cli);
    Register(Installer);
    Register(Configuration);
    Register(Legacy);
  }

  public static void Register(string component)
  {
    if (string.IsNullOrWhiteSpace(component))
      throw new ArgumentException("Component must not be null or whitespace.", nameof(component));
    Registry.TryAdd(component, true);
  }

  public static bool IsRegistered(string component)
    => !string.IsNullOrWhiteSpace(component) && Registry.ContainsKey(component);

  public static IReadOnlyCollection<string> AllRegistered => Registry.Keys.ToList().AsReadOnly();
}
