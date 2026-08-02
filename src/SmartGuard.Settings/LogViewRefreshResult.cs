using SmartGuard.Settings.LogView;

namespace SmartGuard.Settings;

public sealed record LogViewRefreshResult(
    LogWindowDelta WindowDelta,
    LogViewSnapshot Snapshot,
    bool ContentChanged);
