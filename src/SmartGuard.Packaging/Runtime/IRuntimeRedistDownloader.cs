namespace SmartGuard.Packaging.Runtime;

public interface IRuntimeRedistDownloader
{
    Task<string> EnsureRedistAsync(string redistDir, string runtimeVersion, CancellationToken cancellationToken = default);
}
