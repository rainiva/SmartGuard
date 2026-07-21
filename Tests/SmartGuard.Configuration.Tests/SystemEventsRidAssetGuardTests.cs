using FluentAssertions;
using SmartGuard.Configuration.Build;

namespace SmartGuard.Configuration.Tests;

public class SystemEventsRidAssetGuardTests
{
  private const string FileName = "Microsoft.Win32.SystemEvents.dll";

  [Fact]
  public void Validate_returns_error_when_root_hash_differs_from_win_rid_asset()
  {
    var bin = Path.Combine(Path.GetTempPath(), "sg-sysevents-validate-" + Guid.NewGuid().ToString("N"));
    var ridDir = Path.Combine(bin, "runtimes", "win", "lib", "net8.0");
    Directory.CreateDirectory(ridDir);
    File.WriteAllBytes(Path.Combine(ridDir, FileName), [1, 2, 3, 4, 5, 6]);
    File.WriteAllBytes(Path.Combine(bin, FileName), [9, 9, 9]);

    try
    {
      var errors = SystemEventsRidAssetGuard.Validate(bin);

      errors.Should().Contain(error =>
        error.Contains(FileName, StringComparison.OrdinalIgnoreCase));
    }
    finally
    {
      try { Directory.Delete(bin, true); } catch { /* ignore */ }
    }
  }

  [Fact]
  public void EnsureAligned_overwrites_root_with_win_rid_asset_bytes()
  {
    var bin = Path.Combine(Path.GetTempPath(), "sg-sysevents-ensure-" + Guid.NewGuid().ToString("N"));
    var ridDir = Path.Combine(bin, "runtimes", "win", "lib", "net8.0");
    Directory.CreateDirectory(ridDir);
    var ridBytes = new byte[] { 1, 2, 3, 4, 5, 6 };
    File.WriteAllBytes(Path.Combine(ridDir, FileName), ridBytes);
    File.WriteAllBytes(Path.Combine(bin, FileName), [9, 9]);

    try
    {
      SystemEventsRidAssetGuard.EnsureAligned(bin);

      File.ReadAllBytes(Path.Combine(bin, FileName)).Should().Equal(ridBytes);
    }
    finally
    {
      try { Directory.Delete(bin, true); } catch { /* ignore */ }
    }
  }

  [Fact]
  public void Validate_returns_error_when_win_rid_asset_missing_but_root_present()
  {
    var bin = Path.Combine(Path.GetTempPath(), "sg-sysevents-missing-rid-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(bin);
    File.WriteAllBytes(Path.Combine(bin, FileName), [9, 9, 9]);

    try
    {
      var errors = SystemEventsRidAssetGuard.Validate(bin);

      errors.Should().NotBeEmpty();
    }
    finally
    {
      try { Directory.Delete(bin, true); } catch { /* ignore */ }
    }
  }
}
