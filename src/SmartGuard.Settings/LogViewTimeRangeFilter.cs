namespace SmartGuard.Settings;

public static class LogViewTimeRangeFilter
{
    public static bool IsWithinRange(
        LogViewTimeRange mode,
        DateTime timestamp,
        DateTime now,
        DateTime? customStart,
        DateTime? customEnd)
    {
        if (mode == LogViewTimeRange.All)
            return true;

        if (mode == LogViewTimeRange.Today)
            return timestamp >= now.Date && timestamp <= now;

        if (mode == LogViewTimeRange.LastHour)
            return timestamp >= now.AddHours(-1) && timestamp <= now;

        if (mode == LogViewTimeRange.Custom)
        {
            if (customStart.HasValue && timestamp < customStart.Value)
                return false;
            if (customEnd.HasValue && timestamp > customEnd.Value)
                return false;
            return customStart.HasValue || customEnd.HasValue;
        }

        return true;
    }
}
