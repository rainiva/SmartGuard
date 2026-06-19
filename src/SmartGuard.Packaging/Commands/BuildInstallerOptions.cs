namespace SmartGuard.Packaging.Commands;

public record BuildInstallerOptions(
    string Root,
    string Configuration = "Release",
    string StagingDir = "",
    string? IsccPath = null,
    bool SkipPublish = false,
    bool SkipRedistDownload = false,
    bool SkipVersionBump = false,
    string RuntimeVersion = "8.0.18");
