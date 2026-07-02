using SmartGuard.Configuration;

namespace SmartGuard.Tray;

internal static class TrayGuardianRecoveryHandler
{
  internal static int RegisterMissedStatusRead(int missedStatusReads, ref bool guardianRecoveryAttempted, string root)
  {
    var next = missedStatusReads + 1;
    if (!guardianRecoveryAttempted && GuardianRecovery.ShouldAttemptStart(next))
    {
      guardianRecoveryAttempted = true;
      _ = Task.Run(() => GuardianRecovery.TryStartGuardian(root));
    }

    return next;
  }

  internal static int ResetMissedStatusReads() => 0;
}
