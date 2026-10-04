using System.Reflection;
using System.Text;

namespace SmartGuard.Configuration.Tests;

public sealed class GuardConfigDetailedLoadTests : IDisposable
{
  private readonly string _root = Path.Combine(Path.GetTempPath(), "SmartGuardDetailed_" + Guid.NewGuid().ToString("N"));
  private string ConfigPath => Path.Combine(_root, "config.json");
  public GuardConfigDetailedLoadTests() => Directory.CreateDirectory(_root);

  private static object Read(GuardConfigRepository repo)
  {
    var method = typeof(GuardConfigRepository).GetMethod("TryLoadDetailed", Type.EmptyTypes);
    method.Should().NotBeNull("the repository must expose one controlled load result");
    return method!.Invoke(repo, null)!;
  }
  private static T? Field<T>(object result, string name) => (T?)result.GetType().GetProperty(name)!.GetValue(result);

  [Fact]
  public void Missing_parse_invalid_and_access_failures_are_controlled_without_old_configuration()
  {
    var repo = new GuardConfigRepository(ConfigPath);
    Field<string>(Read(repo), "FailureKind").Should().Be("missing");
    File.WriteAllText(ConfigPath, "{", new UTF8Encoding(false));
    Field<string>(Read(repo), "FailureKind").Should().Be("parse");
    File.WriteAllText(ConfigPath, "null", new UTF8Encoding(false));
    Field<string>(Read(repo), "FailureKind").Should().Be("invalid");
    File.Delete(ConfigPath);
    Directory.CreateDirectory(ConfigPath);
    Field<string>(Read(repo), "FailureKind").Should().Be("access");
    Field<GuardConfig>(Read(repo), "Config").Should().BeNull();
    repo.TryLoad().Should().BeNull();
  }

  [Fact]
  public void Fingerprint_is_cached_changes_on_same_count_replacement_and_ignores_theme_and_path_case()
  {
    var repo = new GuardConfigRepository(ConfigPath);
    var config = GuardConfig.CreateDefault(_root);
    config.ForegroundRestrictionEnabled = true;
    config.ForegroundProgramPaths = [@"C:\Apps\alpha.exe"];
    repo.Save(config);
    var first = Read(repo);
    var fingerprint = Field<string>(first, "RuleFingerprint");
    fingerprint.Should().HaveLength(64);
    Read(repo).Should().Be(first);
    repo.TryLoad().Should().BeSameAs(Field<GuardConfig>(first, "Config"));
    repo.DiskReadCountForTests.Should().Be(1);
    config.ThemeIsDark = !config.ThemeIsDark;
    config.ForegroundProgramPaths = [@"c:\apps\ALPHA.EXE"];
    repo.Save(config);
    Field<string>(Read(repo), "RuleFingerprint").Should().Be(fingerprint);
    config.ForegroundProgramPaths = [@"C:\Other\alpha.exe"];
    repo.Save(config);
    Field<string>(Read(repo), "RuleFingerprint").Should().NotBe(fingerprint);
    File.WriteAllText(ConfigPath, "{", new UTF8Encoding(false));
    Field<GuardConfig>(Read(repo), "Config").Should().BeNull();
  }

  public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

  [Fact]
  public void Locked_config_is_an_io_failure_and_does_not_reuse_a_valid_read()
  {
    var repo = new GuardConfigRepository(ConfigPath);
    repo.Save(GuardConfig.CreateDefault(_root));
    repo.TryLoad().Should().NotBeNull();
    File.SetLastWriteTimeUtc(ConfigPath, DateTime.UtcNow.AddMinutes(1));
    using var held = new FileStream(ConfigPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    var result = Read(repo);
    Field<string>(result, "FailureKind").Should().Be("io");
    Field<GuardConfig>(result, "Config").Should().BeNull();
  }
}
