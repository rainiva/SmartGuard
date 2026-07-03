using SmartGuard.Configuration.Distribution;

namespace SmartGuard.Packaging.Staging;

public static class StagingLayout
{
    public static IReadOnlyList<string> RequiredRelativePaths { get; } = DesktopDistributionManifest.RequiredStagingPaths;

    public const string RedistPattern = @"redist\windowsdesktop-runtime-*-win-x64.exe";
}
