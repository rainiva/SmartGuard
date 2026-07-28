namespace SmartGuard.Engine.Worker;

// The main loop's plan probe used to call PowerCfgExecutor directly; a
// transient powercfg timeout (machine under heavy load) then escaped the
// per-iteration try/catch and crashed the whole guardian process with
// 0xE0434352. Probe failures must degrade: keep the last known plan GUID,
// report a warning, and let the next iteration retry.
internal static class GuardianPlanProbe
{
  public static Guid? ProbeOrKeep(Func<Guid?> probe, Guid? lastKnownGuid, Action<string> reportWarning)
  {
    try
    {
      return probe();
    }
    catch (Exception ex)
    {
      try
      {
        reportWarning($"电源计划探测失败，保留上次计划状态：{ex.Message}");
      }
      catch
      {
        // reporting must never take the guardian down either
      }
      return lastKnownGuid;
    }
  }
}
