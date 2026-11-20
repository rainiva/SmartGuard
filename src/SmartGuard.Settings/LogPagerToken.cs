namespace SmartGuard.Settings;

internal abstract record LogPagerToken;

internal sealed record LogPageNumberToken(int PageNumber, bool IsCurrent) : LogPagerToken;

internal sealed record LogPageEllipsisToken : LogPagerToken;
