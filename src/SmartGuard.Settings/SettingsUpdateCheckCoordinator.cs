using System.Windows;

namespace SmartGuard.Settings;

internal sealed class SettingsUpdateCheckCoordinator
{
  private DateTime _lastUpdateCheckTime = DateTime.MinValue;
  private bool _lastUpdateCheckNoUpdate;

  internal async Task CheckForUpdateAsync(Window owner, Func<string?> getGitHubToken)
  {
    if (SettingsUiTestMode.IsEnabled)
      return;

    const string repoOwner = "rainiva";
    const string repoName = "SmartGuard";
    var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    if (_lastUpdateCheckNoUpdate && DateTime.Now - _lastUpdateCheckTime < TimeSpan.FromMinutes(5))
    {
      ShowUpdateAlert(owner, "当前已是最新版本。", AppDialogSeverity.Information);
      return;
    }

    try
    {
      var proxy = System.Net.WebRequest.GetSystemWebProxy();
      proxy.Credentials = System.Net.CredentialCache.DefaultNetworkCredentials;
      using var handler = new System.Net.Http.HttpClientHandler
      {
        Proxy = proxy,
        UseProxy = true,
        DefaultProxyCredentials = System.Net.CredentialCache.DefaultNetworkCredentials
      };
      using var client = new System.Net.Http.HttpClient(handler);
      client.DefaultRequestHeaders.Add("User-Agent", "SmartGuard-UpdateChecker");
      client.Timeout = TimeSpan.FromSeconds(30);

      var token = getGitHubToken();
      if (!string.IsNullOrWhiteSpace(token))
      {
        client.DefaultRequestHeaders.Authorization =
          new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Trim());
      }

      var url = $"https://api.github.com/repos/{repoOwner}/{repoName}/releases/latest";

      const int maxAttempts = 3;
      Exception? lastException = null;
      System.Net.Http.HttpResponseMessage? response = null;
      for (var attempt = 1; attempt <= maxAttempts; attempt++)
      {
        try
        {
          response = await client.GetAsync(url);
          response.EnsureSuccessStatusCode();
          lastException = null;
          break;
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
          lastException = ex;
          await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt));
        }
      }

      if (lastException is not null)
        throw lastException;

      if (response is null)
        throw new InvalidOperationException("No response received from update server.");

      var json = await response.Content.ReadAsStringAsync();
      using var doc = System.Text.Json.JsonDocument.Parse(json);
      var tagName = doc.RootElement.GetProperty("tag_name").GetString() ?? "";

      var latestVersionString = tagName.TrimStart('v', 'V');
      if (!Version.TryParse(latestVersionString, out var latestVersion))
      {
        ShowUpdateAlert(owner, "无法解析最新版本号。", AppDialogSeverity.Warning);
        return;
      }

      var comparison = currentVersion.CompareTo(latestVersion);
      if (comparison < 0)
      {
        if (ShowUpdateConfirm(
              owner,
              "发现新版本",
              $"发现新版本：{latestVersion}\n当前版本：{currentVersion}\n\n是否下载并安装更新？",
              AppDialogSeverity.Information))
        {
          await DownloadAndInstallUpdateAsync(owner, doc.RootElement, latestVersion, repoOwner, repoName, getGitHubToken);
        }
      }
      else
      {
        _lastUpdateCheckNoUpdate = true;
        _lastUpdateCheckTime = DateTime.Now;
        ShowUpdateAlert(owner, "当前已是最新版本。", AppDialogSeverity.Information);
      }
    }
    catch (System.Net.Http.HttpRequestException ex)
    {
      var statusCode = ex.StatusCode;
      var detail = ex.InnerException?.Message ?? ex.Message;
      var tokenConfigured = !string.IsNullOrWhiteSpace(getGitHubToken()) ? "已配置" : "未配置";
      string message;
      if (statusCode == System.Net.HttpStatusCode.NotFound)
        message = $"未找到发布版本，请确认仓库地址正确。\n\nToken 状态：{tokenConfigured}\n详情：{detail}";
      else if (statusCode == System.Net.HttpStatusCode.Forbidden)
        message = $"请求过于频繁，请稍后再试。\n\nToken 状态：{tokenConfigured}\n详情：{detail}";
      else
        message = $"网络连接失败，请检查网络后重试。\n\nToken 状态：{tokenConfigured}\n详情：{detail}";
      ShowUpdateAlert(owner, message, AppDialogSeverity.Warning);
    }
    catch (TaskCanceledException)
    {
      ShowUpdateAlert(owner, "连接超时，请检查网络后重试。", AppDialogSeverity.Warning);
    }
    catch (Exception ex)
    {
      ShowUpdateAlert(owner, $"检查更新时发生错误：{ex.Message}", AppDialogSeverity.Error);
    }
  }

  private static async Task DownloadAndInstallUpdateAsync(
    Window owner,
    System.Text.Json.JsonElement releaseRoot,
    Version latestVersion,
    string repoOwner,
    string repoName,
    Func<string?> getGitHubToken)
  {
    var asset = UpdateInstallerLauncher.ResolveAsset(releaseRoot, latestVersion);
    if (string.IsNullOrEmpty(asset.AssetName) || string.IsNullOrEmpty(asset.DownloadUrl))
    {
      var releaseUrl = releaseRoot.GetProperty("html_url").GetString()
        ?? $"https://github.com/{repoOwner}/{repoName}/releases";
      System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(releaseUrl)
      {
        UseShellExecute = true
      });
      return;
    }

    var installerPath = UpdateInstallerLauncher.GetLocalInstallerPath(asset.AssetName);
    var progress = UpdateDownloadProgressWindowFactory.Create(owner);
    var progressWindow = progress.Window;
    var progressBar = progress.Bar;
    var statusText = progress.Status;
    var cts = progress.Cts;
    var downloadCompleted = false;
    progressWindow.Show();

    try
    {
      var downloadProxy = System.Net.WebRequest.GetSystemWebProxy();
      downloadProxy.Credentials = System.Net.CredentialCache.DefaultNetworkCredentials;
      using var downloadHandler = new System.Net.Http.HttpClientHandler
      {
        Proxy = downloadProxy,
        UseProxy = true,
        DefaultProxyCredentials = System.Net.CredentialCache.DefaultNetworkCredentials
      };
      using var httpClient = new System.Net.Http.HttpClient(downloadHandler);
      httpClient.DefaultRequestHeaders.Add("User-Agent", "SmartGuard-UpdateDownloader");
      httpClient.Timeout = TimeSpan.FromMinutes(10);

      var downloadToken = getGitHubToken();
      if (!string.IsNullOrWhiteSpace(downloadToken))
      {
        httpClient.DefaultRequestHeaders.Authorization =
          new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", downloadToken.Trim());
      }
      using var downloader = new HttpUpdateAssetDownloader(httpClient);

      var downloadProgress = new Progress<double>(value =>
      {
        progressBar.Value = value * 100;
        statusText.Text = $"已下载 {value:P0}";
      });

      await downloader.DownloadAsync(asset.DownloadUrl, installerPath, downloadProgress, cts.Token);
      downloadCompleted = true;
      progressWindow.Close();

      UpdateInstallerLauncher.StartInstaller(installerPath);
      owner.Close();
    }
    catch (OperationCanceledException)
    {
      progressWindow.Close();
      if (!downloadCompleted)
        ShowUpdateAlert(owner, "下载已取消。", AppDialogSeverity.Information);
    }
    catch (Exception ex)
    {
      progressWindow.Close();
      ShowUpdateAlert(owner, $"下载更新失败：{ex.Message}", AppDialogSeverity.Error);
    }
  }

  private static void ShowUpdateAlert(Window owner, string message, AppDialogSeverity severity)
  {
    if (SettingsUiTestMode.IsEnabled)
      return;

    owner.Dispatcher.Invoke(() => AppDialog.ShowAlert(owner, "检查更新", message, severity));
  }

  private static bool ShowUpdateConfirm(Window owner, string title, string message, AppDialogSeverity severity)
  {
    if (SettingsUiTestMode.IsEnabled)
      return false;

    return owner.Dispatcher.Invoke(() => AppDialog.ShowConfirm(owner, title, message, severity));
  }
}
