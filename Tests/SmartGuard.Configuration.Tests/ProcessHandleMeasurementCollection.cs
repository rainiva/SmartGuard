namespace SmartGuard.Configuration.Tests;

// Process-wide handle counts are only meaningful while no other test class runs in this process.
[CollectionDefinition("ProcessHandleMeasurement", DisableParallelization = true)]
public sealed class ProcessHandleMeasurementCollection;
