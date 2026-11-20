using System.Diagnostics;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
public class SettingsProgramDiscoveryTests
{
  [Fact]
  public void Shortcut_fixture_writes_names_outside_the_ansi_code_page()
  {
    // An emoji is outside every ANSI code page, so this is what a Chinese name is on a non-Chinese machine.
    var root = Path.Combine(Path.GetTempPath(), "SmartGuardShortcutFixture_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      var path = Path.Combine(root, "候选" + char.ConvertFromUtf32(0x1F600) + ".lnk");
      WpfStaTestHost.Run(() => UnicodeShortcutFixture.Create(path, Path.Combine(Environment.SystemDirectory, "notepad.exe")));
      File.Exists(path).Should().BeTrue();
    }
    finally { Directory.Delete(root, true); }
  }

  [Fact]
  public async Task A_shortcut_whose_name_is_outside_the_ansi_code_page_is_discovered()
  {
    // On a machine whose ANSI code page cannot spell the name (an emoji anywhere, Chinese on an English
    // system), the shortcut must still be read: names are Unicode.
    var root = Path.Combine(Path.GetTempPath(), "SmartGuardProgramDiscoveryUnicode_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      var name = "候选" + char.ConvertFromUtf32(0x1F600);
      var target = Path.Combine(Environment.SystemDirectory, "notepad.exe");
      WpfStaTestHost.Run(() => UnicodeShortcutFixture.Create(Path.Combine(root, name + ".lnk"), target));
      var candidates = await SettingsProgramCatalog.LoadAsync(CancellationToken.None, [root]);
      var matched = candidates.Where(c => c.Source == ProgramCandidateSource.Shortcut).Should().ContainSingle().Subject;
      matched.ExecutablePath.Should().BeEquivalentTo(target);
    }
    finally { Directory.Delete(root, true); }
  }

  [Fact]
  public async Task An_unopened_local_program_is_discovered_from_its_chinese_shortcut_name()
  {
    var root = Path.Combine(Path.GetTempPath(), "SmartGuardProgramDiscovery_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      var target = Path.Combine(Environment.SystemDirectory, "notepad.exe");
      WpfStaTestHost.Run(() => UnicodeShortcutFixture.Create(Path.Combine(root, "微信测试候选.lnk"), target));
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
