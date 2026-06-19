namespace SmartGuard.Packaging.Staging;

public static class StagingLayout
{
    public static IReadOnlyList<string> RequiredRelativePaths { get; } = new List<string>
    {
        @"bin\SmartGuard.Engine.exe",
        @"bin\SmartGuard.Tray.exe",
        @"bin\SmartGuard.LogViewer.exe",
        @"bin\SmartGuard.Settings.exe",
        @"lib\SmartGuard.ico",
        @"lib\SmartGuard.Settings.xaml",
        @"license_zh-CN.txt",
        @"VERSION.txt"
    }.AsReadOnly();

    public const string RedistPattern = @"redist\windowsdesktop-runtime-*-win-x64.exe";
}
