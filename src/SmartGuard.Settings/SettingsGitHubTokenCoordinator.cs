using System.Windows;
using System.Windows.Controls;
using SmartGuard.Configuration;

namespace SmartGuard.Settings;

internal sealed class SettingsGitHubTokenCoordinator
{
  private readonly GuardConfigRepository _repository;
  private readonly Func<GuardConfig> _getOriginalConfig;
  private readonly Action<GuardConfig> _commitOriginalConfig;
  private readonly Action<string, bool> _showToast;

  internal SettingsGitHubTokenCoordinator(
    GuardConfigRepository repository,
    Func<GuardConfig> getOriginalConfig,
    Action<GuardConfig> commitOriginalConfig,
    Action<string, bool> showToast)
  {
    _repository = repository;
    _getOriginalConfig = getOriginalConfig;
    _commitOriginalConfig = commitOriginalConfig;
    _showToast = showToast;
  }

  internal void WireAboutPage(Window window)
  {
    WireAboutPage(
      window,
      window.FindName("pwdGitHubToken") as PasswordBox,
      window.FindName("txtGitHubTokenStatus") as TextBlock,
      window.FindName("btnSaveGitHubToken") as Button,
      window.FindName("btnClearGitHubToken") as Button);
  }

  internal void WireAboutPage(
    Window window,
    PasswordBox? passwordBox,
    TextBlock? statusText,
    Button? saveButton,
    Button? clearButton)
  {
    UpdateStatus(statusText);

    if (saveButton is not null)
      saveButton.Click += (_, _) => SaveToken(window, passwordBox, statusText);

    if (clearButton is not null)
      clearButton.Click += (_, _) => ClearToken(window, passwordBox, statusText);
  }

  private void SaveToken(Window window, PasswordBox? passwordBox, TextBlock? statusText)
  {
    if (passwordBox is null)
      return;

    var token = passwordBox.Password.Trim();
    if (string.IsNullOrEmpty(token))
    {
      _showToast("请输入 GitHub Token。", true);
      return;
    }

    try
    {
      var config = _getOriginalConfig();
      config.GitHubToken = token;
      _repository.Save(config);
      _commitOriginalConfig(config);
      passwordBox.Password = string.Empty;
      UpdateStatus(statusText);
      _showToast("GitHub Token 已保存。", false);
    }
    catch (Exception ex)
    {
      _showToast($"保存 Token 失败：{ex.Message}", true);
    }
  }

  private void ClearToken(Window window, PasswordBox? passwordBox, TextBlock? statusText)
  {
    if (!SettingsUiTestMode.IsEnabled &&
        !AppDialog.ShowConfirm(
          window,
          "清除 GitHub Token？",
          "清除后检查更新将不再使用 Token 认证。",
          AppDialogSeverity.Warning))
      return;

    try
    {
      var config = _getOriginalConfig();
      config.GitHubToken = string.Empty;
      _repository.Save(config);
      _commitOriginalConfig(config);

      if (passwordBox is not null)
        passwordBox.Password = string.Empty;

      UpdateStatus(statusText);
      _showToast("GitHub Token 已清除。", false);
    }
    catch (Exception ex)
    {
      _showToast($"清除 Token 失败：{ex.Message}", true);
    }
  }

  private void UpdateStatus(TextBlock? statusText)
  {
    if (statusText is null)
      return;

    var configured = !string.IsNullOrWhiteSpace(_getOriginalConfig().GitHubToken);
    statusText.Text = configured
      ? "已配置（如需更换请重新输入并保存；清除可删除）"
      : "未配置";
  }
}
