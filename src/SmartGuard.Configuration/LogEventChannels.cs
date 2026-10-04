namespace SmartGuard.Configuration;

public static class LogEventChannels
{
  public static string ForEventName(string eventName)
    => eventName switch
    {
      EventNameRegistry.AppStarted or EventNameRegistry.AppStopping => LogChannelRegistry.Lifecycle,
      EventNameRegistry.PowerSuspendObserved
        or EventNameRegistry.PowerResumeObserved => LogChannelRegistry.Lifecycle,
      EventNameRegistry.PowerPlanSwitched
        or EventNameRegistry.PowerSourceChanged
        or EventNameRegistry.ExternalPlanChange
        or EventNameRegistry.BatteryStatusPolled
        or EventNameRegistry.PowerPlanProbeFailed
        or EventNameRegistry.PowerPlanApplyFailed => LogChannelRegistry.Power,
      EventNameRegistry.BrightnessAdjusted => LogChannelRegistry.Brightness,
      EventNameRegistry.IdleDetected => LogChannelRegistry.Policy,
      EventNameRegistry.ConfigReloaded
        or EventNameRegistry.ConfigMutationLogged
        or EventNameRegistry.ConfigMutationFailed => LogChannelRegistry.Policy,
      EventNameRegistry.IterationError
        or EventNameRegistry.RecoveryDegraded
        or EventNameRegistry.RecoveryRestored
        or EventNameRegistry.PowerLifecycleRecoveryForced
        or EventNameRegistry.PowerLifecycleSignalOverflow => LogChannelRegistry.Recovery,
      EventNameRegistry.LogBackpressureApplied
        or EventNameRegistry.LogDeduplicationEmitted
        or EventNameRegistry.PipelineHealthSampled => LogChannelRegistry.Diagnostics,
      EventNameRegistry.HeartbeatTick => LogChannelRegistry.Diagnostics,
      EventNameRegistry.InstallProgressLogged => LogChannelRegistry.Install,
      _ => LogChannelRegistry.Diagnostics,
    };
}
