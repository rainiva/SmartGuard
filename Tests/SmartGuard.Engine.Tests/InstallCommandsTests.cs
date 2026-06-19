using SmartGuard.Configuration;
using SmartGuard.Engine.Cli;

namespace SmartGuard.Engine.Tests;

public class InstallCommandsTests
{
  [Fact]
  public void ScheduledTaskNames_delegate_to_registrar()
  {
    InstallPaths.ScheduledTaskNames.Should().BeEquivalentTo(ScheduledTaskRegistrar.TaskNames);
  }

  [Fact]
  public void GetEngineExe_points_to_bin_engine()
  {
    var root = @"D:\Project\SmartGuard";
    InstallPaths.GetEngineExe(root)
      .Should().Be(Path.Combine(root, "bin", "SmartGuard.Engine.exe"));
  }
}

public class ElevationDeclinedMarkerTests
{
  [Fact]
  public void Exists_returns_false_when_marker_missing()
  {
    var root = Path.Combine(Path.GetTempPath(), "sg-marker-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      ElevationDeclinedMarker.Exists(root).Should().BeFalse();
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public void Exists_returns_true_after_Create()
  {
    var root = Path.Combine(Path.GetTempPath(), "sg-marker-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      ElevationDeclinedMarker.Exists(root).Should().BeFalse();
      ElevationDeclinedMarker.Create(root);
      ElevationDeclinedMarker.Exists(root).Should().BeTrue();
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public void Create_is_idempotent()
  {
    var root = Path.Combine(Path.GetTempPath(), "sg-marker-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      ElevationDeclinedMarker.Create(root);
      ElevationDeclinedMarker.Create(root);
      ElevationDeclinedMarker.Exists(root).Should().BeTrue();
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public void Exists_returns_false_for_invalid_path()
  {
    ElevationDeclinedMarker.Exists(@"Z:\NonExistent\Path\12345").Should().BeFalse();
  }
}
