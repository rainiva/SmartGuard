using SmartGuard.Configuration;

namespace SmartGuard.Engine.Worker;

/// <summary>
/// 带有效性结论的配置读取结果：仅 <see cref="IsValid"/>（TryLoad 成功返回配置）
/// 才能授权守护动作。缺失、破损或访问异常一律视为配置不可用，
/// 禁止新动作，也不得用默认配置猜测授权。
/// </summary>
internal readonly record struct GuardConfigReadResult(GuardConfig? Config, bool IsValid)
{
  public static GuardConfigReadResult FromConfig(GuardConfig? config)
    => new(config, config is not null);

  public static GuardConfigReadResult Unavailable { get; } = new(null, false);

  /// <summary>
  /// Engine 读取边界：<see cref="GuardConfigRepository.TryLoad"/> 的文件元数据访问可能抛异常，
  /// 此处统一转换为配置不可用（降级为仅观测，禁止从默认值猜测授权）。
  /// </summary>
  public static GuardConfigReadResult TryFromRepository(GuardConfigRepository repository)
  {
    try
    {
      return FromConfig(repository.TryLoad());
    }
    catch (Exception)
    {
      return Unavailable;
    }
  }
}
