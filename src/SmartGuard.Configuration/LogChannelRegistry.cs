using System.Collections.Concurrent;

namespace SmartGuard.Configuration;

/// <summary>
/// 通道注册表：标识稳定业务通道。
/// </summary>
public static class LogChannelRegistry
{
  private static readonly ConcurrentDictionary<string, bool> Registry = new(StringComparer.Ordinal);

  public const string Lifecycle = nameof(Lifecycle);
  public const string Policy = nameof(Policy);
  public const string Power = nameof(Power);
  public const string Brightness = nameof(Brightness);
  public const string Recovery = nameof(Recovery);
  public const string Diagnostics = nameof(Diagnostics);
  public const string UI = nameof(UI);
  public const string Install = nameof(Install);
  public const string Security = nameof(Security);
  public const string Compatibility = nameof(Compatibility);

  static LogChannelRegistry()
  {
    Register(Lifecycle);
    Register(Policy);
    Register(Power);
    Register(Brightness);
    Register(Recovery);
    Register(Diagnostics);
    Register(UI);
    Register(Install);
    Register(Security);
    Register(Compatibility);
  }

  public static void Register(string channel)
  {
    if (string.IsNullOrWhiteSpace(channel))
      throw new ArgumentException("Channel must not be null or whitespace.", nameof(channel));
    Registry.TryAdd(channel, true);
  }

  public static bool IsRegistered(string channel)
    => !string.IsNullOrWhiteSpace(channel) && Registry.ContainsKey(channel);

  public static IReadOnlyCollection<string> AllRegistered => Registry.Keys.ToList().AsReadOnly();
}
