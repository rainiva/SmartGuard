using System.Reflection;
using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Architecture.Tests;

public class ScriptPathConstantsParityArchitectureTests
{
  private static readonly Dictionary<string, string> FileNameToPs1Variable = new()
  {
    [nameof(SmartGuardPaths.ConfigFileName)] = "$SmartGuardConfigFileName",
    [nameof(SmartGuardPaths.StatusFileName)] = "$SmartGuardStatusFileName",
    [nameof(SmartGuardPaths.DefaultLogFileName)] = "$SmartGuardDefaultLogFileName",
    [nameof(SmartGuardPaths.StartupLogFileName)] = "$SmartGuardStartupLogFileName",
    [nameof(SmartGuardPaths.InitMarkerFileName)] = "$SmartGuardInitMarkerFileName",
  };

  private static readonly Dictionary<string, string> FileNameToInnoDefine = new()
  {
    [nameof(SmartGuardPaths.ConfigFileName)] = "ConfigFileName",
    [nameof(SmartGuardPaths.StatusFileName)] = "StatusFileName",
    [nameof(SmartGuardPaths.DefaultLogFileName)] = "DefaultLogFileName",
    [nameof(SmartGuardPaths.StartupLogFileName)] = "StartupLogFileName",
    [nameof(SmartGuardPaths.InitMarkerFileName)] = "InitMarkerFileName",
  };

  [Fact]
  public void Path_constants_ps1_must_mirror_SmartGuardPaths_file_names()
  {
    var ps1 = SourceScanHelper.ReadSource("scripts/SmartGuardPathConstants.ps1");
    foreach (var (fieldName, variableName) in FileNameToPs1Variable)
    {
      var expectedValue = GetSmartGuardPathsConstant(fieldName);
      ps1.Should().Contain($"{variableName} = '{expectedValue}'",
        $"SmartGuardPathConstants.ps1 must define {variableName} matching SmartGuardPaths.{fieldName}");
    }
  }

  [Fact]
  public void SmartGuardPathConstants_cmd_must_not_be_revived()
  {
    File.Exists(Path.Combine(SourceScanHelper.RepoRoot, "SmartGuardPathConstants.cmd"))
      .Should().BeFalse("cmd path constants were removed; use SmartGuardPaths in C#");
  }

  [Fact]
  public void Inno_uninstall_delete_must_use_defined_artifact_names()
  {
    var iss = SourceScanHelper.ReadSource("installer/SmartGuard.iss");
    foreach (var (fieldName, defineName) in FileNameToInnoDefine)
    {
      var expectedValue = GetSmartGuardPathsConstant(fieldName);
      iss.Should().Contain($"#define {defineName} \"{expectedValue}\"",
        $"SmartGuard.iss must define {defineName} from SmartGuardPaths.{fieldName}");
      iss.Should().Contain("{#" + defineName + "}",
        $"SmartGuard.iss must reference {{#{defineName}}} in uninstall sections");
    }

    var afterDefines = iss[(iss.IndexOf("[UninstallDelete]", StringComparison.Ordinal)..)];
    afterDefines.Should().NotContain(@"{app}\SmartGuard.config.json");
    afterDefines.Should().NotContain(@"{app}\SmartGuard.status.json");
    afterDefines.Should().NotContain(@"{app}\SmartGuard.startup.log");
    afterDefines.Should().Contain("{#DefaultLogFileName}");
  }

  private static string GetSmartGuardPathsConstant(string fieldName)
  {
    var field = typeof(SmartGuardPaths).GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
    field.Should().NotBeNull($"SmartGuardPaths.{fieldName} must exist");
    return (string)field!.GetRawConstantValue()!;
  }
}
