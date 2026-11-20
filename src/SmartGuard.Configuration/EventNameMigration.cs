using System.Collections.Concurrent;

namespace SmartGuard.Configuration;

/// <summary>
/// 事件名称历史别名兼容名单：维护已废弃事件名到当前规范名的映射。
/// 阶段三不做全量重命名（高风险），但预留此接口供阶段四渐进迁移。
/// </summary>
public static class EventNameMigration
{
  private static readonly ConcurrentDictionary<string, string> _aliases = new(StringComparer.Ordinal);

  public static void AddAlias(string legacyName, string currentName)
  {
    _aliases.TryAdd(legacyName, currentName);
  }

  public static string Resolve(string name)
  {
    return _aliases.TryGetValue(name, out var resolved) ? resolved : name;
  }

  public static IReadOnlyDictionary<string, string> AllAliases =>
    new Dictionary<string, string>(_aliases);
}
