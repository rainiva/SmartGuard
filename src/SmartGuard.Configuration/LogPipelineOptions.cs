namespace SmartGuard.Configuration;

public sealed class LogPipelineOptions
{
  public int NormalQueueCapacity { get; init; } = 20_000;
  public int HighQueueCapacity { get; init; } = 2_000;
  public int MaxBatchSize { get; init; } = 200;
  public int FlushIntervalMs { get; init; } = 100;
  public int ShutdownTimeoutMs { get; init; } = 3_000;
}
