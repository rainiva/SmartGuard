namespace SmartGuard.Packaging.Commands;

public record StageOptions(
    string Root,
    string Configuration = "Release",
    string StagingDir = "",
    string RuntimeVersion = "8.0.18",
    bool SkipPublish = false,
    bool SkipRedistDownload = false);
