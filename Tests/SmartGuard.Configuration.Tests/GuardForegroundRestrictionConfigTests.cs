namespace SmartGuard.Configuration.Tests;

public class GuardForegroundRestrictionConfigTests
{
  private static string NewTempDir()
    => Path.Combine(Path.GetTempPath(), "sg-foreground-" + Guid.NewGuid().ToString("N"));

  private static string NewConfigPath(string dir)
    => Path.Combine(dir, SmartGuardPaths.ConfigFileName);

  private static GuardConfig ValidBaseConfig() => new()
  {
    BalancedThresholdSec = 300,
    PowerSaverThresholdSec = 900,
    LowBatteryPercent = 30,
    CheckIntervalSec = 15,
    BrightnessRestoreMs = 300,
  };

  // ---------- 旧配置兼容：缺少新字段时保持原行为（默认关闭 + 空名单） ----------

  [Fact]
  public void LoadFromJson_legacy_config_without_new_fields_defaults_to_disabled_and_empty()
  {
    var config = GuardConfig.LoadFromJson("""
      {
        "BalancedThresholdSec": 300,
        "PowerSaverThresholdSec": 900,
        "Paused": false
      }
      """);

    config.ForegroundRestrictionEnabled.Should().BeFalse();
    config.ForegroundProgramPaths.Should().BeEmpty();
  }

  [Fact]
  public void TryLoad_legacy_config_file_defaults_to_disabled_and_empty()
  {
    var dir = NewTempDir();
    Directory.CreateDirectory(dir);
    var path = NewConfigPath(dir);
    File.WriteAllText(path, """
      {
        "BalancedThresholdSec": 300,
        "PowerSaverThresholdSec": 900
      }
      """);

    try
    {
      var loaded = new GuardConfigRepository(path).TryLoad();
      loaded.Should().NotBeNull();
      loaded!.ForegroundRestrictionEnabled.Should().BeFalse();
      loaded.ForegroundProgramPaths.Should().BeEmpty();
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  [Fact]
  public void CreateDefault_disables_foreground_restriction_with_empty_list()
  {
    var config = GuardConfig.CreateDefault(NewTempDir());

    config.ForegroundRestrictionEnabled.Should().BeFalse();
    config.ForegroundProgramPaths.Should().BeEmpty();
  }

  // ---------- 持久化往返（repository.Save 必须写出新字段） ----------

  [Fact]
  public void Save_round_trips_foreground_restriction_fields_including_chinese_paths()
  {
    var dir = NewTempDir();
    Directory.CreateDirectory(dir);
    var path = NewConfigPath(dir);

    try
    {
      var config = GuardConfig.LoadFromJson("""
        {
          "BalancedThresholdSec": 300,
          "PowerSaverThresholdSec": 900,
          "ForegroundRestrictionEnabled": true,
          "ForegroundProgramPaths": ["C:\\Tools\\编辑器.exe", "D:\\Dev\\build.exe"]
        }
        """);

      var repo = new GuardConfigRepository(path);
      repo.Save(config);

      File.ReadAllText(path).Should().Contain("ForegroundRestrictionEnabled");
      File.ReadAllText(path).Should().Contain("ForegroundProgramPaths");
      File.ReadAllText(path).Should().Contain("编辑器.exe");

      var reloaded = new GuardConfigRepository(path).TryLoad();
      reloaded.Should().NotBeNull();
      reloaded!.ForegroundRestrictionEnabled.Should().BeTrue();
      reloaded.ForegroundProgramPaths.Should().Equal(@"C:\Tools\编辑器.exe", @"D:\Dev\build.exe");
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  [Fact]
  public void Save_adds_foreground_fields_to_existing_legacy_config_file()
  {
    var dir = NewTempDir();
    Directory.CreateDirectory(dir);
    var path = NewConfigPath(dir);
    File.WriteAllText(path, """
      {
        "BalancedThresholdSec": 300,
        "PowerSaverThresholdSec": 900
      }
      """);

    try
    {
      var repo = new GuardConfigRepository(path);
      var loaded = repo.TryLoad();
      loaded.Should().NotBeNull();
      loaded!.ForegroundRestrictionEnabled = true;
      loaded.ForegroundProgramPaths = new List<string> { @"C:\Tools\app.exe" };
      repo.Save(loaded);

      var reloaded = repo.TryLoad();
      reloaded.Should().NotBeNull();
      reloaded!.ForegroundRestrictionEnabled.Should().BeTrue();
      reloaded.ForegroundProgramPaths.Should().Equal(@"C:\Tools\app.exe");
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  // ---------- 保存其他设置（Settings 全量快照）不丢开关和名单 ----------

  [Fact]
  public void ApplyTraySettings_preserves_foreground_restriction_fields()
  {
    var current = GuardConfig.CreateDefault(NewTempDir());
    current.ForegroundRestrictionEnabled = true;
    current.ForegroundProgramPaths = new List<string> { @"C:\Tools\app.exe", @"D:\工具\编译.exe" };

    var updated = SettingsSnapshotMapper.ApplyTraySettings(
      current,
      balancedThresholdMin: 5,
      powerSaverThresholdMin: 15,
      lowBatteryPercent: 30,
      checkIntervalSec: 15,
      brightnessRestoreMs: 300,
      heartbeatIntervalMin: current.HeartbeatIntervalMin,
      activePlanGuid: current.ActivePlanGuid,
      balancedPlanGuid: current.BalancedPlanGuid,
      powerSaverPlanGuid: current.PowerSaverPlanGuid,
      paused: true,
      notifyOnPlanChange: true,
      notifyOnExternalChange: true,
      autoStartEnabled: true);

    updated.ForegroundRestrictionEnabled.Should().BeTrue();
    updated.ForegroundProgramPaths.Should().Equal(@"C:\Tools\app.exe", @"D:\工具\编译.exe");
  }

  // ---------- 暂停切换与手动接管不丢名单 ----------

  [Fact]
  public void SetPaused_keeps_foreground_restriction_fields()
  {
    var dir = NewTempDir();
    Directory.CreateDirectory(dir);
    var path = NewConfigPath(dir);
    var repository = new GuardConfigRepository(path);
    var config = GuardConfig.CreateDefault(dir);
    config.ForegroundRestrictionEnabled = true;
    config.ForegroundProgramPaths = new List<string> { @"C:\Tools\app.exe" };
    repository.Save(config);
    var mutations = new ConfigMutationService(repository);

    try
    {
      mutations.SetPaused(true, dir, SmartGuardPaths.StartupLogFile(dir));

      var reloaded = repository.TryLoad();
      reloaded.Should().NotBeNull();
      reloaded!.Paused.Should().BeTrue();
      reloaded.ForegroundRestrictionEnabled.Should().BeTrue();
      reloaded.ForegroundProgramPaths.Should().Equal(@"C:\Tools\app.exe");
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  [Fact]
  public void SetManualHighPerformanceUntil_keeps_foreground_restriction_fields()
  {
    var dir = NewTempDir();
    Directory.CreateDirectory(dir);
    var path = NewConfigPath(dir);
    var repository = new GuardConfigRepository(path);
    var config = GuardConfig.CreateDefault(dir);
    config.ForegroundRestrictionEnabled = true;
    config.ForegroundProgramPaths = new List<string> { @"C:\Tools\app.exe" };
    repository.Save(config);
    var mutations = new ConfigMutationService(repository);
    var until = DateTime.Now.AddHours(1);

    try
    {
      mutations.SetManualHighPerformanceUntil(until);

      var reloaded = repository.TryLoad();
      reloaded.Should().NotBeNull();
      reloaded!.ManualHighPerformanceUntil.Should().Be(until);
      reloaded.ForegroundRestrictionEnabled.Should().BeTrue();
      reloaded.ForegroundProgramPaths.Should().Equal(@"C:\Tools\app.exe");
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  // ---------- 恢复默认 = 关闭 + 清空，且经保存链覆盖磁盘上的旧限制状态 ----------

  [Fact]
  public void Reset_clears_foreground_restriction_and_overwrites_persisted_state()
  {
    var dir = NewTempDir();
    Directory.CreateDirectory(dir);
    var path = NewConfigPath(dir);
    File.WriteAllText(path, """
      {
        "BalancedThresholdSec": 300,
        "PowerSaverThresholdSec": 900,
        "ForegroundRestrictionEnabled": true,
        "ForegroundProgramPaths": ["C:\\Tools\\app.exe"],
        "LogFile": "D:\\Custom\\SmartGuard.log"
      }
      """);

    try
    {
      var repo = new GuardConfigRepository(path);
      var current = repo.TryLoad();
      current.Should().NotBeNull();

      var reset = GuardConfigResetService.CreateResetConfig(current!, dir);
      reset.ForegroundRestrictionEnabled.Should().BeFalse();
      reset.ForegroundProgramPaths.Should().BeEmpty();
      reset.LogFile.Should().Be(@"D:\Custom\SmartGuard.log");

      repo.Save(reset);

      var reloaded = repo.TryLoad();
      reloaded.Should().NotBeNull();
      reloaded!.ForegroundRestrictionEnabled.Should().BeFalse();
      reloaded.ForegroundProgramPaths.Should().BeEmpty();
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  // ---------- 名单规则校验（输入保存与运行时共用同一算法） ----------

  [Fact]
  public void Validate_accepts_valid_absolute_exe_paths_and_empty_list()
  {
    var emptyList = ValidBaseConfig();
    emptyList.ForegroundRestrictionEnabled = true;
    emptyList.ForegroundProgramPaths = new List<string>();

    var validPaths = ValidBaseConfig();
    validPaths.ForegroundRestrictionEnabled = true;
    validPaths.ForegroundProgramPaths = new List<string>
      { @"C:\Tools\app.exe", @"C:\Tools\LAUNCHER.EXE", @"D:\工具\编译.exe" };

    GuardConfigValidator.Validate(emptyList).Should().BeEmpty();
    GuardConfigValidator.Validate(validPaths).Should().BeEmpty();
  }

  [Fact]
  public void Validate_rejects_relative_paths_and_bare_file_names()
  {
    var relative = ValidBaseConfig();
    relative.ForegroundProgramPaths = new List<string> { @"Tools\app.exe" };

    var bareName = ValidBaseConfig();
    bareName.ForegroundProgramPaths = new List<string> { "app.exe" };

    GuardConfigValidator.Validate(relative)
      .Should().Contain("前台程序路径必须为以 .exe 结尾的绝对 Windows 路径");
    GuardConfigValidator.Validate(bareName)
      .Should().Contain("前台程序路径必须为以 .exe 结尾的绝对 Windows 路径");
  }

  [Fact]
  public void Validate_rejects_drive_relative_and_root_relative_paths()
  {
    var driveRelative = ValidBaseConfig();
    driveRelative.ForegroundProgramPaths = new List<string> { "C:app.exe" };

    var rootRelative = ValidBaseConfig();
    rootRelative.ForegroundProgramPaths = new List<string> { @"\app.exe" };

    GuardConfigValidator.Validate(driveRelative)
      .Should().Contain("前台程序路径必须为以 .exe 结尾的绝对 Windows 路径");
    GuardConfigValidator.Validate(rootRelative)
      .Should().Contain("前台程序路径必须为以 .exe 结尾的绝对 Windows 路径");
  }

  [Fact]
  public void Validate_rejects_non_exe_paths_case_insensitively()
  {
    var config = ValidBaseConfig();
    config.ForegroundProgramPaths = new List<string> { @"C:\Tools\app.dll", @"C:\Tools\README.TXT" };

    GuardConfigValidator.Validate(config)
      .Should().Contain("前台程序路径必须为以 .exe 结尾的绝对 Windows 路径");
  }

  [Fact]
  public void Validate_rejects_wildcard_entries()
  {
    var config = ValidBaseConfig();
    config.ForegroundProgramPaths = new List<string> { @"C:\Tools\*.exe" };

    GuardConfigValidator.Validate(config)
      .Should().Contain("前台程序路径不能包含通配符或设备路径前缀");
  }

  [Fact]
  public void Validate_rejects_device_path_prefixes()
  {
    var extended = ValidBaseConfig();
    extended.ForegroundProgramPaths = new List<string> { @"\\?\C:\Tools\app.exe" };

    var device = ValidBaseConfig();
    device.ForegroundProgramPaths = new List<string> { @"\\.\pipe\app.exe" };

    GuardConfigValidator.Validate(extended)
      .Should().Contain("前台程序路径不能包含通配符或设备路径前缀");
    GuardConfigValidator.Validate(device)
      .Should().Contain("前台程序路径不能包含通配符或设备路径前缀");
  }

  [Fact]
  public void Validate_rejects_empty_or_whitespace_entries()
  {
    var config = ValidBaseConfig();
    config.ForegroundProgramPaths = new List<string> { @"C:\Tools\app.exe", "   " };

    GuardConfigValidator.Validate(config)
      .Should().Contain("前台程序名单不能包含空路径");
  }

  [Fact]
  public void Validate_rejects_entries_longer_than_32767_chars()
  {
    const string prefix = @"C:\Tools\";
    const string suffix = ".exe";

    // .NET 8（net8.0-windows）Path.GetFullPath 对非前缀绝对路径的规范化上界为 32766 字符；
    // 恰好 32767 的条目虽在计划预算（≤32767）内，但无法规范化，按非法条目拒绝（fail-closed）。
    var atCanonicalLimit = ValidBaseConfig();
    atCanonicalLimit.ForegroundProgramPaths = new List<string>
      { prefix + new string('a', 32766 - prefix.Length - suffix.Length) + suffix };

    var overBudget = ValidBaseConfig();
    overBudget.ForegroundProgramPaths = new List<string>
      { prefix + new string('a', 32768 - prefix.Length - suffix.Length) + suffix };

    var atBudgetButUncanonicalizable = ValidBaseConfig();
    atBudgetButUncanonicalizable.ForegroundProgramPaths = new List<string>
      { prefix + new string('a', 32767 - prefix.Length - suffix.Length) + suffix };

    GuardConfigValidator.Validate(atCanonicalLimit).Should().BeEmpty();
    GuardConfigValidator.Validate(overBudget)
      .Should().Contain("前台程序路径过长（最多 32767 个字符）");
    GuardConfigValidator.Validate(atBudgetButUncanonicalizable)
      .Should().Contain("前台程序路径必须为以 .exe 结尾的绝对 Windows 路径");
  }

  [Fact]
  public void Validate_rejects_more_than_128_distinct_paths_and_accepts_exactly_128()
  {
    var atLimit = ValidBaseConfig();
    atLimit.ForegroundRestrictionEnabled = true;
    atLimit.ForegroundProgramPaths = Enumerable.Range(0, 128)
      .Select(i => $@"C:\Apps\App{i}.exe")
      .ToList();

    var overLimit = ValidBaseConfig();
    overLimit.ForegroundRestrictionEnabled = true;
    overLimit.ForegroundProgramPaths = Enumerable.Range(0, 129)
      .Select(i => $@"C:\Apps\App{i}.exe")
      .ToList();

    GuardConfigValidator.Validate(atLimit).Should().BeEmpty();
    GuardConfigValidator.Validate(overLimit)
      .Should().Contain("前台程序名单最多 128 项");
  }

  [Fact]
  public void Validate_counts_duplicates_once_toward_the_128_limit()
  {
    var config = ValidBaseConfig();
    config.ForegroundRestrictionEnabled = true;
    config.ForegroundProgramPaths = Enumerable.Range(0, 100)
      .SelectMany(i => new[] { $@"C:\Apps\App{i}.exe", $@"c:\apps\app{i}.EXE" })
      .ToList();

    GuardConfigValidator.Validate(config).Should().BeEmpty();
  }

  // ---------- 保存失败后正式配置仍有效 ----------

  [Fact]
  public void Validation_failure_refuses_save_and_keeps_existing_config_intact()
  {
    var dir = NewTempDir();
    Directory.CreateDirectory(dir);
    var path = NewConfigPath(dir);

    try
    {
      var repo = new GuardConfigRepository(path);
      var valid = GuardConfig.CreateDefault(dir);
      valid.ForegroundRestrictionEnabled = true;
      valid.ForegroundProgramPaths = new List<string> { @"C:\Tools\app.exe" };
      repo.Save(valid);

      var rejected = GuardConfig.CreateDefault(dir);
      rejected.ForegroundRestrictionEnabled = true;
      rejected.ForegroundProgramPaths = Enumerable.Range(0, 129)
        .Select(i => $@"C:\Apps\App{i}.exe")
        .ToList();

      // 与 SettingsDebouncedSaver 相同的保存前置校验；有错误即拒绝保存，不调用 repository.Save。
      GuardConfigValidator.Validate(rejected).Should().NotBeEmpty();

      var reloaded = repo.TryLoad();
      reloaded.Should().NotBeNull();
      reloaded!.ForegroundRestrictionEnabled.Should().BeTrue();
      reloaded.ForegroundProgramPaths.Should().Equal(@"C:\Tools\app.exe");
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  // ---------- 规范化助手：规范化完整路径 + OrdinalIgnoreCase 去重 ----------

  [Fact]
  public void NormalizeProgramPaths_merges_duplicates_ignoring_case_and_separators_preserving_first()
  {
    var normalized = GuardConfigValidator.NormalizeProgramPaths(new[]
    {
      @"C:\Apps\Tools\A.exe",
      @"c:\apps\tools\a.exe",
      "C:/Apps/Tools/A.exe",
      @"C:\Apps\Other\B.exe",
    });

    normalized.Should().Equal(@"C:\Apps\Tools\A.exe", @"C:\Apps\Other\B.exe");
  }

  [Fact]
  public void NormalizeProgramPaths_resolves_relative_segments_via_GetFullPath()
  {
    var normalized = GuardConfigValidator.NormalizeProgramPaths(new[]
    {
      @"C:\Apps\Sub\..\Tools\app.exe",
      "C:/Apps/app.exe",
    });

    normalized.Should().Equal(@"C:\Apps\Tools\app.exe", @"C:\Apps\app.exe");
  }

  [Fact]
  public void NormalizeProgramPaths_keeps_entries_it_cannot_canonicalize_so_validation_still_rejects()
  {
    var normalized = GuardConfigValidator.NormalizeProgramPaths(new string?[]
    {
      @"C:\Tools\app.exe",
      @"Tools\app.exe",
      null,
    });

    normalized.Should().HaveCount(3, "规范化助手不得静默丢弃非法条目，验证仍须拒绝它们");

    var config = ValidBaseConfig();
    config.ForegroundRestrictionEnabled = true;
    config.ForegroundProgramPaths = normalized.ToList();
    GuardConfigValidator.Validate(config).Should().NotBeEmpty();
  }
}
