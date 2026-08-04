using FluentAssertions;
using SmartGuard.Configuration;

namespace SmartGuard.Settings.Tests;

public class SavedLogFilterCoordinatorTests
{
  [Fact]
  public void Coordinator_add_overwrite_rename_delete_reorder_and_default_persist()
  {
    var dir = CreateTempDir();
    var path = ConfigPath(dir);

    try
    {
      var repository = new GuardConfigRepository(path);
      var config = GuardConfig.CreateDefault(dir);
      repository.Save(config);

      GuardConfig current = config;
      var coordinator = new SavedLogFilterCoordinator(
        repository,
        () => current,
        updated => current = updated);

      coordinator.Add(new SavedLogFilter
      {
        Id = "f1",
        Name = "Engine 错误",
        Query = LogQueryExpressionDto.FromExpression(
          new LogQueryFieldExpression("component", LogQueryOperator.Equals, "Engine")),
      });
      coordinator.Add(new SavedLogFilter
      {
        Id = "f2",
        Name = "全部",
        Query = LogQueryExpressionDto.MatchAll(),
      });
      coordinator.SetDefault("f1");
      coordinator.Reorder(["f2", "f1"]);

      coordinator.Update(new SavedLogFilter
      {
        Id = "f1",
        Name = "Engine 错误",
        Query = LogQueryExpressionDto.FromExpression(
          new LogQueryFieldExpression("severity", LogQueryOperator.Equals, "Error")),
      });
      coordinator.Rename("f2", "全部日志");
      coordinator.Delete("f1");
      coordinator.SetDefault(null);

      var reloaded = repository.LoadOrDefault(dir);
      reloaded.LogViewPreferences.SavedFilters.Should().ContainSingle();
      reloaded.LogViewPreferences.SavedFilters[0].Id.Should().Be("f2");
      reloaded.LogViewPreferences.SavedFilters[0].Name.Should().Be("全部日志");
      reloaded.LogViewPreferences.DefaultFilterId.Should().BeNull();
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  [Fact]
  public void Coordinator_persists_only_through_GuardConfigRepository()
  {
    var source = File.ReadAllText(Path.Combine(
      FindRepoRoot(),
      "src",
      "SmartGuard.Settings",
      "SavedLogFilterCoordinator.cs"));

    source.Should().Contain("_repository.Save(");
    source.Should().NotContain("File.WriteAllText");
    source.Should().NotContain("GuardConfigAtomicFileWriter");
  }

  private static string CreateTempDir()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    return dir;
  }

  private static string ConfigPath(string dir) => Path.Combine(dir, "SmartGuard.config.json");

  private static string FindRepoRoot()
  {
    var dir = AppContext.BaseDirectory;
    while (!string.IsNullOrEmpty(dir))
    {
      if (File.Exists(Path.Combine(dir, "SmartGuard.slnx")))
        return dir;

      dir = Directory.GetParent(dir)?.FullName ?? string.Empty;
    }

    throw new InvalidOperationException("Repository root not found.");
  }
}
