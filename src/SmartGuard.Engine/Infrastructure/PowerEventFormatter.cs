namespace SmartGuard.Engine.Infrastructure;

public static class PowerEventFormatter
{
  public static string FormatMessage(bool isOnAc) =>
    $"电源事件: {(isOnAc ? "插电" : "拔电（电池供电）")}，立即重新评估";
}
