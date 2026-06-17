namespace SmartGuard.Engine.Infrastructure;

public static class PowerEventFormatter
{
  public static string FormatMessage(bool isOnAc) =>
    $"电源事件: {(isOnAc ? "插电" : "电池")}，立即重新评估";
}
