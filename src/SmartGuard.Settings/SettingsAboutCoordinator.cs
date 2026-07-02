using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SmartGuard.Settings;

internal sealed class SettingsAboutCoordinator
{
  private readonly SettingsUpdateCheckCoordinator _updateCheck = new();

  internal static string GetDisplayVersion()
  {
    var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
    if (version is null) return "1.0.0";
    if (version.Build > 0)
      return $"{version.Major}.{version.Minor}.{version.Build}";
    return $"{version.Major}.{version.Minor}";
  }

  internal void WireAboutPage(Window window, TextBlock txtVersion, Func<string?> getGitHubToken)
  {
    txtVersion.Text = GetDisplayVersion();

    var lnkRepo = window.FindName("lnkRepo") as Hyperlink;
    if (lnkRepo is not null)
    {
      lnkRepo.RequestNavigate += (_, e) =>
      {
        e.Handled = true;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri)
        {
          UseShellExecute = true
        });
      };
    }

    var btnCheckUpdate = window.FindName("btnCheckUpdate") as Button;
    if (btnCheckUpdate is null)
      return;

    btnCheckUpdate.Click += async (_, _) =>
    {
      btnCheckUpdate.Content = "检查中...";
      btnCheckUpdate.IsEnabled = false;
      try
      {
        await _updateCheck.CheckForUpdateAsync(window, getGitHubToken).ConfigureAwait(true);
      }
      catch (Exception ex)
      {
        window.Dispatcher.Invoke(() =>
          AppDialog.ShowAlert(window, "检查更新", $"检查更新时发生错误：{ex.Message}", AppDialogSeverity.Error));
      }
      finally
      {
        window.Dispatcher.Invoke(() =>
        {
          btnCheckUpdate.Content = "检查更新";
          btnCheckUpdate.IsEnabled = true;
        });
      }
    };
  }
}
