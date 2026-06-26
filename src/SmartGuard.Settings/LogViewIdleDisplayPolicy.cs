namespace SmartGuard.Settings;

public static class LogViewIdleDisplayPolicy
{
    public const int ActivityDropThresholdSeconds = 5;
    public const int ActiveIdleMaxSeconds = 2;

    public static int Resolve(int extrapolatedSeconds, int apiIdleSeconds)
    {
        if (apiIdleSeconds <= ActiveIdleMaxSeconds)
            return apiIdleSeconds;

        if (apiIdleSeconds + ActivityDropThresholdSeconds < extrapolatedSeconds)
            return apiIdleSeconds;

        return extrapolatedSeconds;
    }
}
