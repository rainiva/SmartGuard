using SmartGuard.Configuration;

namespace SmartGuard.Engine.Worker;

public sealed partial class GuardianLoop
{
  private int ResolveWaitSeconds(TimeSpan? lifecycleReevaluateAfter)
    => GuardianIterationTiming.ResolveWaitSeconds(_configRepository, _rootPath, lifecycleReevaluateAfter);

  private GuardConfig? LoadConfig() => _configRepository.TryLoad();

  private void EnsureConfigExists()
  {
    try
    {
      // File.Exists hides denied access. Initialize only confirmed absence;
      // existing unreadable data must be left for the controlled read boundary.
      if (_configRepository.TryLoadDetailed().FailureKind != "missing") return;
      if (!Directory.Exists(_rootPath)) Directory.CreateDirectory(_rootPath);
      _configRepository.Save(GuardConfig.CreateDefault(_rootPath));
    }
    catch
    {
      // A failed initial commit leaves configuration unavailable. The existing
      // cycle reports that reason through bounded logging and health/status.
    }
  }
}
