using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
public class SettingsProgramDiscoveryTests
{
  [Fact]
  public async Task An_unopened_local_program_is_discovered_from_its_chinese_shortcut_name()
  {
    var root = Path.Combine(Path.GetTempPath(), "SmartGuardProgramDiscovery_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      var target = Path.Combine(Environment.SystemDirectory, "notepad.exe");
      WpfStaTestHost.Run(() =>
      {
        var shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        object? shortcut = null;
        try
        {
          shortcut = ((dynamic)shell).CreateShortcut(Path.Combine(root, "微信测试候选.lnk"));
          ((dynamic)shortcut).TargetPath = target;
          ((dynamic)shortcut).Save();
        }
        finally
        {
          if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut);
          Marshal.FinalReleaseComObject(shell);
        }
      });
      var elapsed = Stopwatch.StartNew();
      var candidates = await SettingsProgramCatalog.LoadAsync(CancellationToken.None, [root]);
      var matched = SettingsProgramCatalog.Search(candidates, "微信测试候选").Single();
      matched.ExecutablePath.Should().BeEquivalentTo(target, "Windows path identity is case insensitive");
      matched.Source.Should().Be(ProgramCandidateSource.Shortcut);
      matched.VerificationText.Should().Be("尚未验证", "discovering a launch entry is not a foreground match");
      matched.Icon.Should().NotBeNull();
      matched.Icon!.IsFrozen.Should().BeTrue("cross-thread icons must have no UI ownership");
      elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "a small local shortcut fixture must finish promptly");
    }
    finally { Directory.Delete(root, true); }
  }
}
