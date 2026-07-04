using FluentAssertions;
using SmartGuard.Configuration;
using SmartGuard.Settings;
using System.Windows;
using System.Windows.Controls;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
[Trait("Category", "WpfUi")]
public class SettingsGitHubTokenCoordinatorTests
{
  [Fact]
  public void SaveToken_persists_encrypted_token_via_repository()
  {
    WpfStaTestHost.Run(() =>
    {
      var dir = CreateTempDir();
      var path = SmartGuardPaths.ConfigFile(dir);
      var repository = new GuardConfigRepository(path);
      var original = GuardConfig.CreateDefault(dir);
      repository.Save(original);

      try
      {
        var window = new Window();
        var passwordBox = new PasswordBox();
        var status = new TextBlock();
        var saveButton = new Button();
        var clearButton = new Button();
        passwordBox.Password = "ghp_uiSaveToken1234567890";

        var coordinator = new SettingsGitHubTokenCoordinator(
          repository,
          () => original,
          config => original = config,
          (message, isError) => { });

        coordinator.WireAboutPage(window, passwordBox, status, saveButton, clearButton);
        saveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        original.GitHubToken.Should().Be("ghp_uiSaveToken1234567890");
        passwordBox.Password.Should().BeEmpty("saved token must not remain visible in PasswordBox");

        var json = File.ReadAllText(path);
        json.Should().Contain("GitHubTokenProtected");
        json.Should().NotContain("ghp_uiSaveToken1234567890");
      }
      finally
      {
        Directory.Delete(dir, recursive: true);
      }
    });
  }

  [Fact]
  public void ClearToken_removes_stored_token()
  {
    WpfStaTestHost.Run(() =>
    {
      var dir = CreateTempDir();
      var path = SmartGuardPaths.ConfigFile(dir);
      var repository = new GuardConfigRepository(path);
      var original = GuardConfig.CreateDefault(dir);
      original.GitHubToken = "ghp_uiClearToken1234567890";
      repository.Save(original);
      original = repository.TryLoad()!;

      try
      {
        SettingsUiTestMode.SetEnabled(true);
        var window = new Window();
        var passwordBox = new PasswordBox();
        var status = new TextBlock();
        var saveButton = new Button();
        var clearButton = new Button();

        var coordinator = new SettingsGitHubTokenCoordinator(
          repository,
          () => original,
          config => original = config,
          (message, isError) => { });

        coordinator.WireAboutPage(window, passwordBox, status, saveButton, clearButton);
        clearButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        original.GitHubToken.Should().BeEmpty();
        repository.TryLoad()!.GitHubToken.Should().BeEmpty();
        status.Text.Should().Contain("未配置");
      }
      finally
      {
        SettingsUiTestMode.SetEnabled(false);
        Directory.Delete(dir, recursive: true);
      }
    });
  }

  [Fact]
  public void WireAboutPage_does_not_prefill_password_box_when_token_configured()
  {
    WpfStaTestHost.Run(() =>
    {
      var dir = CreateTempDir();
      var path = SmartGuardPaths.ConfigFile(dir);
      var repository = new GuardConfigRepository(path);
      var original = GuardConfig.CreateDefault(dir);
      original.GitHubToken = "ghp_prefillCheckToken1234567890";
      repository.Save(original);
      original = repository.TryLoad()!;

      try
      {
        var window = new Window();
        var passwordBox = new PasswordBox();
        var status = new TextBlock();
        var saveButton = new Button();
        var clearButton = new Button();

        var coordinator = new SettingsGitHubTokenCoordinator(
          repository,
          () => original,
          config => original = config,
          (message, isError) => { });

        coordinator.WireAboutPage(window, passwordBox, status, saveButton, clearButton);

        passwordBox.Password.Should().BeEmpty();
        status.Text.Should().Contain("已配置");
      }
      finally
      {
        Directory.Delete(dir, recursive: true);
      }
    });
  }

  private static string CreateTempDir()
  {
    var dir = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    return dir;
  }
}
