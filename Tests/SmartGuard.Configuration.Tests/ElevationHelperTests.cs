using System.Diagnostics;
using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class ElevationHelperTests
{
  [Fact]
  public void StartElevated_uses_runas_and_shell_execute()
  {
    ProcessStartInfo? captured = null;
    ElevationHelper.StartProcessForTests = info =>
    {
      captured = info;
      return null;
    };

    try
    {
      ElevationHelper.StartElevated(@"C:\Temp\setup.exe", "/SILENT");
      captured.Should().NotBeNull();
      captured!.FileName.Should().Be(@"C:\Temp\setup.exe");
      captured.Arguments.Should().Be("/SILENT");
      captured.Verb.Should().Be("runas");
      captured.UseShellExecute.Should().BeTrue();
    }
    finally
    {
      ElevationHelper.StartProcessForTests = null;
    }
  }

  [Fact]
  public void RerunCurrentProcessElevated_uses_current_executable()
  {
    ProcessStartInfo? captured = null;
    ElevationHelper.StartProcessForTests = info =>
    {
      captured = info;
      return null;
    };

    try
    {
      ElevationHelper.RerunCurrentProcessElevated("--root \"C:\\App\" --install");
      captured.Should().NotBeNull();
      captured!.FileName.Should().Be(Environment.ProcessPath);
      captured.Arguments.Should().Be("--root \"C:\\App\" --install");
      captured.Verb.Should().Be("runas");
      captured.UseShellExecute.Should().BeTrue();
    }
    finally
    {
      ElevationHelper.StartProcessForTests = null;
    }
  }
}
