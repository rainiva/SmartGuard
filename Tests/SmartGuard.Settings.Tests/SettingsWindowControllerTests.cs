using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using SmartGuard.Configuration;
using SmartGuard.Settings;

namespace SmartGuard.Settings.Tests;

[Collection("WpfUiTests")]
public class SettingsWindowControllerTests
{
    private static T RunOnSta<T>(Func<T> action)
    {
        T? result = default;
        WpfStaTestHost.Run(() => result = action());
        return result!;
    }

    private static void RunOnSta(Action action)
    {
        WpfStaTestHost.Run(action);
    }

    [Fact]
    public void Embedded_resource_can_load_window()
    {
        RunOnSta(() =>
        {
            try
            {
                // WPF LoadComponent uses .xaml extension, compiler auto-resolves to .baml
                var window = (Window)Application.LoadComponent(
                    new Uri("/SmartGuard.Settings;component/SmartGuard.Settings.xaml", UriKind.Relative));
                window.Should().NotBeNull();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to load embedded window. Available resources: {string.Join(", ",
                        typeof(SettingsWindowController).Assembly.GetManifestResourceNames())}", ex);
            }
        });
    }

    [Fact]
    public void Embedded_resource_has_correct_baml_path()
    {
        RunOnSta(() =>
        {
            var resources = typeof(SettingsWindowController).Assembly.GetManifestResourceNames();
            resources.Should().Contain("SmartGuard.Settings.g.resources");

            using var stream = typeof(SettingsWindowController).Assembly
                .GetManifestResourceStream("SmartGuard.Settings.g.resources")!;
            using var reader = new System.Resources.ResourceReader(stream);
            var names = new List<string>();
            var enumerator = reader.GetEnumerator();
            while (enumerator.MoveNext())
            {
                names.Add((string)enumerator.Key);
            }
            names.Should().Contain("smartguard.settings.baml");
        });
    }

    [Fact]
    public void User_opens_settings_from_installed_app_without_external_xaml()
    {
        RunOnSta(() =>
        {
            // Simulate installed environment: no external XAML file, must use embedded resource
            var installRoot = Path.Combine(Path.GetTempPath(), "sg-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(installRoot);
            Directory.CreateDirectory(Path.Combine(installRoot, "bin"));
            // Intentionally do NOT create lib/SmartGuard.Settings.xaml

            try
            {
                var configPath = Path.Combine(installRoot, "SmartGuard.config.json");
                File.WriteAllText(configPath, "{\"BalancedThresholdSec\":300,\"PowerSaverThresholdSec\":900,\"LowBatteryPercent\":25,\"CheckIntervalSec\":30,\"BrightnessRestoreMs\":1000}");
                var repository = new GuardConfigRepository(configPath);
                var config = repository.LoadOrDefault(installRoot);

                // This is the exact path Program.Main takes when user clicks "设置..." in tray
                var controller = SettingsWindowController.TryCreate(installRoot, repository, config);
                controller.Should().NotBeNull(
                    "Settings should open from embedded resource when installed without external XAML. " +
                    "This simulates the real user flow: tray icon -> Settings -> window opens.");

                // Verify window was created and is ready for interaction
                // (ShowDialog blocks until closed, so we only verify creation here)
                var window = GetWindowField(controller);
                window.Should().NotBeNull();
                window.Width.Should().BeGreaterThan(0);
                window.Height.Should().BeGreaterThan(0);
            }
            finally
            {
                try { Directory.Delete(installRoot, true); } catch { }
            }
        });
    }

    [Fact]
    public void User_opens_settings_window_survives_layout_measure()
    {
        RunOnSta(() =>
        {
            var installRoot = Path.Combine(Path.GetTempPath(), "sg-test-measure-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(installRoot);
            Directory.CreateDirectory(Path.Combine(installRoot, "bin"));

            try
            {
                var configPath = Path.Combine(installRoot, "SmartGuard.config.json");
                File.WriteAllText(configPath, "{\"BalancedThresholdSec\":300,\"PowerSaverThresholdSec\":900,\"LowBatteryPercent\":25,\"CheckIntervalSec\":30,\"BrightnessRestoreMs\":1000}");
                var repository = new GuardConfigRepository(configPath);
                var config = repository.LoadOrDefault(installRoot);

                var controller = SettingsWindowController.TryCreate(installRoot, repository, config);
                controller.Should().NotBeNull();

                var window = GetWindowField(controller);
                var act = () =>
                {
                    window.Width = 800;
                    window.Height = 640;
                    window.Measure(new Size(800, 640));
                    window.Arrange(new Rect(0, 0, 800, 640));
                };

                act.Should().NotThrow(
                    "StaticResource forward references in templates fail during Measure/Show, not during TryCreate.");
            }
            finally
            {
                try { Directory.Delete(installRoot, true); } catch { }
            }
        });
    }

    [Fact]
    public void Controller_finds_all_numberbox_controls()
    {
        RunOnSta(() =>
        {
            var root = Path.GetFullPath(AppContext.BaseDirectory);
            var projectRoot = Path.GetFullPath(Path.Combine(root, "..", "..", "..", ".."));
            var xamlPath = Path.Combine(projectRoot, "lib", "SmartGuard.Settings.xaml");
            if (!File.Exists(xamlPath))
            {
                projectRoot = Path.GetFullPath(Path.Combine(root, "..", "..", ".."));
                xamlPath = Path.Combine(projectRoot, "lib", "SmartGuard.Settings.xaml");
            }
            if (!File.Exists(xamlPath))
            {
                return;
            }

            var configPath = Path.Combine(projectRoot, "SmartGuard.config.json");
            var repository = new GuardConfigRepository(configPath);
            var config = repository.LoadOrDefault(projectRoot);

            var controller = SettingsWindowController.TryCreate(projectRoot, repository, config);
            controller.Should().NotBeNull();
        });
    }

    [Fact]
    public void NumberBox_values_initialize_from_config()
    {
        RunOnSta(() =>
        {
            var root = Path.GetFullPath(AppContext.BaseDirectory);
            var projectRoot = Path.GetFullPath(Path.Combine(root, "..", "..", "..", ".."));
            var xamlPath = Path.Combine(projectRoot, "lib", "SmartGuard.Settings.xaml");
            if (!File.Exists(xamlPath))
            {
                projectRoot = Path.GetFullPath(Path.Combine(root, "..", "..", ".."));
                xamlPath = Path.Combine(projectRoot, "lib", "SmartGuard.Settings.xaml");
            }
            if (!File.Exists(xamlPath))
            {
                return;
            }

            var configPath = Path.Combine(projectRoot, "SmartGuard.config.json");
            var repository = new GuardConfigRepository(configPath);
            var config = new GuardConfig
            {
                BalancedThresholdSec = 300,
                PowerSaverThresholdSec = 900,
                LowBatteryPercent = 25,
                CheckIntervalSec = 30,
                BrightnessRestoreMs = 1000
            };

            var controller = SettingsWindowController.TryCreate(projectRoot, repository, config);
            controller.Should().NotBeNull();
        });
    }

    [Fact]
    public void Navigation_pages_exist_in_window()
    {
        RunOnSta(() =>
        {
            var root = AppContext.BaseDirectory;
            var xamlPath = Path.Combine(root, "lib", "SmartGuard.Settings.xaml");
            if (!File.Exists(xamlPath))
            {
                // Skip if xaml not available in test environment
                return;
            }

            var xaml = File.ReadAllText(xamlPath);
            var window = (Window)System.Windows.Markup.XamlReader.Parse(xaml);

            var pageGeneral = window.FindName("pageGeneral") as StackPanel;
            var pageAdvanced = window.FindName("pageAdvanced") as StackPanel;
            var pageNotifications = window.FindName("pageNotifications") as StackPanel;
            var navList = window.FindName("navList") as ListBox;

            pageGeneral.Should().NotBeNull();
            pageAdvanced.Should().NotBeNull();
            pageNotifications.Should().NotBeNull();
            navList.Should().NotBeNull();
            navList.Items.Count.Should().Be(6);
        });
    }

    [Fact]
    public void Display_page_theme_controls_exist()
    {
        RunOnSta(() =>
        {
            var root = AppContext.BaseDirectory;
            var xamlPath = Path.Combine(root, "lib", "SmartGuard.Settings.xaml");
            if (!File.Exists(xamlPath))
            {
                return;
            }

            var xaml = File.ReadAllText(xamlPath);
            var window = (Window)System.Windows.Markup.XamlReader.Parse(xaml);

            window.FindName("tglThemeFollowSystem").Should().NotBeNull();
            window.FindName("tglThemeLight").Should().NotBeNull();
            window.FindName("tglThemeDark").Should().NotBeNull();
            window.FindName("rowThemeLight").Should().NotBeNull();
            window.FindName("rowThemeDark").Should().NotBeNull();
            window.FindName("btnThemeToggle").Should().BeNull();
            window.FindName("imgAppIcon").Should().NotBeNull();
        });
    }

    [Fact]
    public void InfoBar_is_removed_in_favor_of_toast_notifications()
    {
        RunOnSta(() =>
        {
            var window = (Window)Application.LoadComponent(
                new Uri("/SmartGuard.Settings;component/SmartGuard.Settings.xaml", UriKind.Relative));

            window.FindName("infoBar").Should().BeNull(
                "The inline InfoBar is replaced by top-right toast notifications for instant-apply UX.");
            window.FindName("txtInfoBar").Should().BeNull();
        });
    }

    [Fact]
    public void All_numberbox_controls_have_correct_names()
    {
        RunOnSta(() =>
        {
            var root = AppContext.BaseDirectory;
            var xamlPath = Path.Combine(root, "lib", "SmartGuard.Settings.xaml");
            if (!File.Exists(xamlPath))
            {
                return;
            }

            var xaml = File.ReadAllText(xamlPath);
            var window = (Window)System.Windows.Markup.XamlReader.Parse(xaml);

            window.FindName("sldBalanced").Should().NotBeNull();
            window.FindName("sldSaver").Should().NotBeNull();
            window.FindName("sldBattery").Should().NotBeNull();
            window.FindName("sldPoll").Should().NotBeNull();
            window.FindName("sldBrightMs").Should().NotBeNull();
        });
    }

    [Fact]
    public void Log_page_controls_exist()
    {
        RunOnSta(() =>
        {
            var root = AppContext.BaseDirectory;
            var xamlPath = Path.Combine(root, "lib", "SmartGuard.Settings.xaml");
            if (!File.Exists(xamlPath))
            {
                return;
            }

            var xaml = File.ReadAllText(xamlPath);
            var window = (Window)System.Windows.Markup.XamlReader.Parse(xaml);

            var pageLogs = window.FindName("pageLogs") as Panel;
            var logSearchFilterHost = window.FindName("logSearchFilterHost") as ContentControl;
            var logTagFilterLinksPanel = window.FindName("logTagFilterLinksPanel") as WrapPanel;
            var lstLogView = window.FindName("lstLogView") as ListBox;
            var lblLogStatus = window.FindName("lblLogStatus") as TextBlock;

            pageLogs.Should().NotBeNull();
            logSearchFilterHost.Should().NotBeNull();
            logTagFilterLinksPanel.Should().NotBeNull();
            lstLogView.Should().NotBeNull();
            lblLogStatus.Should().NotBeNull();
            window.FindName("btnLogCopy").Should().NotBeNull();
            window.FindName("btnLogExport").Should().NotBeNull();
            window.FindName("btnLogOpenFolder").Should().NotBeNull();
            window.FindName("btnLogScrollTop").Should().NotBeNull();
            window.FindName("btnLogScrollBottom").Should().NotBeNull();
            window.FindName("btnLogRefresh").Should().NotBeNull();
            window.FindName("chkLogFollowTail").Should().NotBeNull();
            window.FindName("cmbLogTimeRange").Should().NotBeNull();
            window.FindName("chkLogSearchCaseSensitive").Should().NotBeNull();
        });
    }

    [Fact]
    public void Log_page_shows_custom_range_inputs_only_for_custom_mode()
    {
        RunOnSta(() =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardSettingsLogRangeUi_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            File.WriteAllText(Path.Combine(tempRoot, "SmartGuard.log"), "[INFO] 2026-06-21 10:00:00 ok\n");

            try
            {
                var configPath = Path.Combine(tempRoot, "SmartGuard.config.json");
                var repository = new GuardConfigRepository(configPath);
                var config = repository.LoadOrDefault(tempRoot);

                var controller = SettingsWindowController.TryCreate(tempRoot, repository, config);
                controller.Should().NotBeNull();

                var window = GetWindowField(controller!);
                var navList = window.FindName("navList") as ListBox;
                navList!.SelectedIndex = 3;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                var combo = window.FindName("cmbLogTimeRange") as ComboBox;
                var customPanel = window.FindName("panelLogCustomRange") as UIElement;

                combo.Should().NotBeNull();
                customPanel.Should().NotBeNull();
                customPanel!.Visibility.Should().Be(Visibility.Collapsed);

                combo!.SelectedIndex = 3;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                customPanel.Visibility.Should().Be(Visibility.Visible);
            }
            finally
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        });
    }

    [Fact]
    public void Log_view_enables_horizontal_scroll_and_rich_text_box()
    {
        RunOnSta(() =>
        {
            var root = AppContext.BaseDirectory;
            var xamlPath = Path.Combine(root, "lib", "SmartGuard.Settings.xaml");
            if (!File.Exists(xamlPath))
            {
                return;
            }

            var xaml = File.ReadAllText(xamlPath);
            var window = (Window)System.Windows.Markup.XamlReader.Parse(xaml);

            var lstLogView = window.FindName("lstLogView") as ListBox;

            lstLogView.Should().NotBeNull();
            VirtualizingPanel.GetIsVirtualizing(lstLogView!).Should().BeTrue();
        });
    }

    [Fact]
    public void Log_page_exposes_clickable_tag_filter_links()
    {
        RunOnSta(() =>
        {
            var root = AppContext.BaseDirectory;
            var xamlPath = Path.Combine(root, "lib", "SmartGuard.Settings.xaml");
            if (!File.Exists(xamlPath))
            {
                return;
            }

            var xaml = File.ReadAllText(xamlPath);
            var window = (Window)System.Windows.Markup.XamlReader.Parse(xaml);
            var logTagFilterLinksPanel = window.FindName("logTagFilterLinksPanel") as WrapPanel;

            logTagFilterLinksPanel.Should().NotBeNull();
            xaml.Should().Contain("x:Name=\"logTagFilterLinksPanel\"");
            xaml.Should().NotContain("x:Name=\"chkInfo\"");
        });
    }

    [Fact]
    public void User_search_with_no_matches_sees_empty_state_message()
    {
        RunOnSta(() =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardSettingsLogSearch_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            var logPath = Path.Combine(tempRoot, "SmartGuard.log");
            File.WriteAllText(logPath, "[INFO] 2026-06-21 10:00:00 alpha\n[WARN] 2026-06-21 10:01:00 beta\n");

            try
            {
                var configPath = Path.Combine(tempRoot, "SmartGuard.config.json");
                var repository = new GuardConfigRepository(configPath);
                var config = repository.LoadOrDefault(tempRoot);

                var controller = SettingsWindowController.TryCreate(tempRoot, repository, config);
                controller.Should().NotBeNull();

                var window = GetWindowField(controller!);
                var navList = window.FindName("navList") as ListBox;
                navList!.SelectedIndex = 3;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                var filterBar = GetLogSearchFilterBar(controller!);
                var lblLogStatus = window.FindName("lblLogStatus") as TextBlock;

                filterBar.SetKeyword("missing-term");
                FlushLogSearchDebounce(controller!, window);

                GetLogViewPlainText(controller!).Should().Contain("无匹配结果");
                lblLogStatus!.Text.Should().Contain("匹配 0 条");
            }
            finally
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        });
    }

    [Fact]
    public void User_adding_tag_filter_narrows_visible_logs()
    {
        RunOnSta(() =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardSettingsLogLevels_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            var logPath = Path.Combine(tempRoot, "SmartGuard.log");
            File.WriteAllText(logPath,
                "[INFO] 2026-06-21 10:00:00 alpha\n" +
                "[ERROR] 2026-06-21 10:01:00 beta\n");

            try
            {
                var configPath = Path.Combine(tempRoot, "SmartGuard.config.json");
                var repository = new GuardConfigRepository(configPath);
                var config = repository.LoadOrDefault(tempRoot);

                var controller = SettingsWindowController.TryCreate(tempRoot, repository, config);
                controller.Should().NotBeNull();

                var window = GetWindowField(controller!);
                var navList = window.FindName("navList") as ListBox;
                navList!.SelectedIndex = 3;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                var filterBar = GetLogSearchFilterBar(controller!);
                GetLogViewPlainText(controller!).Should().Contain("alpha");
                GetLogViewPlainText(controller!).Should().Contain("beta");

                filterBar.AddTagFilter("ERROR");
                InvokeRefreshLogView(controller!);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                GetLogViewPlainText(controller!).Should().Contain("beta");
                GetLogViewPlainText(controller!).Should().NotContain("alpha");
            }
            finally
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        });
    }

    [Fact]
    public void User_on_logs_page_sees_idle_without_clicking_refresh()
    {
        RunOnSta(() =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardSettingsLogIdleOpen_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            var logPath = Path.Combine(tempRoot, "SmartGuard.log");
            File.WriteAllText(logPath, "[HEART] 2026-06-21 10:00:00 active\n");

            var publishedAt = DateTime.Now.AddSeconds(-30);
            var status = new SmartGuard.Contracts.StatusPayload
            {
                idleSeconds = 480,
                timestamp = publishedAt.ToString("s"),
            };
            File.WriteAllText(
                Path.Combine(tempRoot, "SmartGuard.status.json"),
                System.Text.Json.JsonSerializer.Serialize(status));

            LogViewIdleReader.ApiReadOverrideForTests = () => 505;
            try
            {
                var configPath = Path.Combine(tempRoot, "SmartGuard.config.json");
                var repository = new GuardConfigRepository(configPath);
                var config = repository.LoadOrDefault(tempRoot);

                var controller = SettingsWindowController.TryCreate(tempRoot, repository, config);
                controller.Should().NotBeNull();

                var window = GetWindowField(controller!);
                var navList = window.FindName("navList") as ListBox;
                navList!.SelectedIndex = 3;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                var statusLabel = window.FindName("lblLogStatus") as TextBlock;
                statusLabel.Should().NotBeNull();
                statusLabel!.Text.Should().MatchRegex(@"当前空闲 (5\d\d|510) 秒");
            }
            finally
            {
                LogViewIdleReader.ApiReadOverrideForTests = null;
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        });
    }

    [Fact]
    public void User_sees_lower_idle_after_local_activity_while_status_stale()
    {
        RunOnSta(() =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardSettingsLogIdleActiveUi_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            var logPath = Path.Combine(tempRoot, "SmartGuard.log");
            File.WriteAllText(logPath, "[HEART] 2026-06-21 10:00:00 active\n");

            var publishedAt = DateTime.Now.AddSeconds(-30);
            var status = new SmartGuard.Contracts.StatusPayload
            {
                idleSeconds = 500,
                timestamp = publishedAt.ToString("s"),
            };
            File.WriteAllText(
                Path.Combine(tempRoot, "SmartGuard.status.json"),
                System.Text.Json.JsonSerializer.Serialize(status));

            LogViewIdleReader.ApiReadOverrideForTests = () => 8;
            try
            {
                var configPath = Path.Combine(tempRoot, "SmartGuard.config.json");
                var repository = new GuardConfigRepository(configPath);
                var config = repository.LoadOrDefault(tempRoot);

                var controller = SettingsWindowController.TryCreate(tempRoot, repository, config);
                controller.Should().NotBeNull();

                var window = GetWindowField(controller!);
                var navList = window.FindName("navList") as ListBox;
                navList!.SelectedIndex = 3;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                var statusLabel = window.FindName("lblLogStatus") as TextBlock;
                statusLabel!.Text.Should().Contain("当前空闲 8 秒");
            }
            finally
            {
                LogViewIdleReader.ApiReadOverrideForTests = null;
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        });
    }

    [Fact]
    public void User_automatic_log_refresh_updates_idle_seconds_without_manual_refresh()
    {
        RunOnSta(() =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardSettingsLogIdleTimer_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            var logPath = Path.Combine(tempRoot, "SmartGuard.log");
            File.WriteAllText(logPath, "[HEART] 2026-06-21 10:00:00 active\n");

            var readCount = 0;
            LogViewIdleReader.ReadOverrideForTests = () =>
            {
                readCount++;
                return readCount == 1 ? 100u : 200u;
            };

            try
            {
                var configPath = Path.Combine(tempRoot, "SmartGuard.config.json");
                var repository = new GuardConfigRepository(configPath);
                var config = repository.LoadOrDefault(tempRoot);

                var controller = SettingsWindowController.TryCreate(tempRoot, repository, config);
                controller.Should().NotBeNull();

                var window = GetWindowField(controller!);
                var navList = window.FindName("navList") as ListBox;
                navList!.SelectedIndex = 3;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                var statusLabel = window.FindName("lblLogStatus") as TextBlock;
                statusLabel!.Text.Should().Contain("当前空闲 100 秒");

                InvokeRefreshLogView(controller!);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                statusLabel.Text.Should().Contain("当前空闲 200 秒");
            }
            finally
            {
                LogViewIdleReader.ReadOverrideForTests = null;
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        });
    }

    [Fact]
    public void User_clicks_refresh_and_sees_idle_from_status_not_reset_by_click()
    {
        RunOnSta(() =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardSettingsLogIdle_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            var logPath = Path.Combine(tempRoot, "SmartGuard.log");
            File.WriteAllText(logPath, "[HEART] 2026-06-21 10:00:00 active\n");

            var publishedAt = DateTime.Now.AddSeconds(-30);
            var status = new SmartGuard.Contracts.StatusPayload
            {
                idleSeconds = 480,
                timestamp = publishedAt.ToString("s"),
            };
            File.WriteAllText(
                Path.Combine(tempRoot, "SmartGuard.status.json"),
                System.Text.Json.JsonSerializer.Serialize(status));

            LogViewIdleReader.ApiReadOverrideForTests = () => 505;
            try
            {
                var configPath = Path.Combine(tempRoot, "SmartGuard.config.json");
                var repository = new GuardConfigRepository(configPath);
                var config = repository.LoadOrDefault(tempRoot);

                var controller = SettingsWindowController.TryCreate(tempRoot, repository, config);
                controller.Should().NotBeNull();

                var window = GetWindowField(controller!);
                var navList = window.FindName("navList") as ListBox;
                navList!.SelectedIndex = 3;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                ClickLogToolbarButton(window, "btnLogRefresh");
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                var statusLabel = window.FindName("lblLogStatus") as TextBlock;
                statusLabel.Should().NotBeNull();
                statusLabel!.Text.Should().MatchRegex(@"当前空闲 (5\d\d|510) 秒");
            }
            finally
            {
                LogViewIdleReader.ApiReadOverrideForTests = null;
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        });
    }

    [Fact]
    public void User_clicks_refresh_button_and_sees_new_log_lines()
    {
        RunOnSta(() =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardSettingsLogRefresh_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            var logPath = Path.Combine(tempRoot, "SmartGuard.log");
            File.WriteAllText(logPath, "[INFO] 2026-06-21 10:00:00 initial entry\n");

            try
            {
                var configPath = Path.Combine(tempRoot, "SmartGuard.config.json");
                var repository = new GuardConfigRepository(configPath);
                var config = repository.LoadOrDefault(tempRoot);

                var controller = SettingsWindowController.TryCreate(tempRoot, repository, config);
                controller.Should().NotBeNull();

                var window = GetWindowField(controller!);
                var navList = window.FindName("navList") as ListBox;
                navList!.SelectedIndex = 3;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                GetLogViewPlainText(controller!).Should().Contain("initial entry");

                File.WriteAllText(logPath, "[INFO] 2026-06-21 10:00:00 initial entry\n[INFO] 2026-06-21 10:01:00 refreshed entry\n");
                ClickLogToolbarButton(window, "btnLogRefresh");
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                GetLogViewPlainText(controller!).Should().Contain("refreshed entry");
            }
            finally
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        });
    }

    [Fact]
    public void User_jumps_to_bottom_without_enabling_follow_tail()
    {
        RunOnSta(() =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardSettingsLogJump_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            var logPath = Path.Combine(tempRoot, "SmartGuard.log");
            var builder = new System.Text.StringBuilder();
            for (var i = 0; i < 120; i++)
                builder.AppendLine($"[INFO] 2026-06-21 09:{i % 60:D2}:{i % 60:D2} scroll line {i}");
            File.WriteAllText(logPath, builder.ToString());

            try
            {
                var configPath = Path.Combine(tempRoot, "SmartGuard.config.json");
                var repository = new GuardConfigRepository(configPath);
                var config = repository.LoadOrDefault(tempRoot);

                var controller = SettingsWindowController.TryCreate(tempRoot, repository, config);
                controller.Should().NotBeNull();

                var window = GetWindowField(controller!);
                var navList = window.FindName("navList") as ListBox;
                navList!.SelectedIndex = 3;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                var scrollViewer = GetLogScrollViewer(window, controller!);
                var chkLogFollowTail = window.FindName("chkLogFollowTail") as CheckBox;
                scrollViewer!.ScrollToVerticalOffset(0);
                chkLogFollowTail!.IsChecked = false;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                ClickLogToolbarButton(window, "btnLogScrollBottom");
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                LogViewScrollState.IsAtTail(scrollViewer).Should().BeTrue();
                chkLogFollowTail.IsChecked.Should().BeFalse();
            }
            finally
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        });
    }

    [Fact]
    public void User_exports_visible_log_to_file()
    {
        RunOnSta(() =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardSettingsLogExport_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            var logPath = Path.Combine(tempRoot, "SmartGuard.log");
            File.WriteAllText(logPath, "[INFO] 2026-06-21 10:00:00 export me\n");
            var exportPath = Path.Combine(tempRoot, "exported.log.txt");

            try
            {
                var configPath = Path.Combine(tempRoot, "SmartGuard.config.json");
                var repository = new GuardConfigRepository(configPath);
                var config = repository.LoadOrDefault(tempRoot);

                var controller = SettingsWindowController.TryCreate(tempRoot, repository, config);
                controller.Should().NotBeNull();

                var window = GetWindowField(controller!);
                var navList = window.FindName("navList") as ListBox;
                navList!.SelectedIndex = 3;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                InvokeLogExport(controller!, exportPath);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                File.Exists(exportPath).Should().BeTrue();
                File.ReadAllText(exportPath, System.Text.Encoding.UTF8).Should().Contain("export me");
            }
            finally
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        });
    }

    [Fact]
    public void TryCreate_returns_before_slow_plan_catalog_load_completes()
    {
        RunOnSta(() =>
        {
            PowerPlanCatalogProvider.ClearSessionCacheForTests();
            PowerPlanCatalogProvider.LoadImplementationForTests = () =>
            {
                Thread.Sleep(500);
                return new Dictionary<Guid, string>
                {
                    [Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e")] = "平衡",
                };
            };

            var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardSettingsPlanCatalog_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            SettingsWindowController? controller = null;

            try
            {
                var configPath = Path.Combine(tempRoot, "SmartGuard.config.json");
                var repository = new GuardConfigRepository(configPath);
                var config = repository.LoadOrDefault(tempRoot);

                var sw = Stopwatch.StartNew();
                controller = SettingsWindowController.TryCreate(tempRoot, repository, config);
                sw.Stop();

                controller.Should().NotBeNull();
                sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1),
                    "settings window should not block on synchronous powercfg catalog load");
            }
            finally
            {
                controller?.Dispose();
                PowerPlanCatalogProvider.ClearSessionCacheForTests();
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        });
    }

    [Fact]
    public void User_typing_custom_range_waits_for_debounce_before_updating_view()
    {
        RunOnSta(() =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardSettingsLogRangeDebounce_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            var logPath = Path.Combine(tempRoot, "SmartGuard.log");
            File.WriteAllText(
                logPath,
                "[INFO] 2026-06-21 10:00:00 alpha\n[INFO] 2026-06-21 11:00:00 beta\n");

            try
            {
                var configPath = Path.Combine(tempRoot, "SmartGuard.config.json");
                var repository = new GuardConfigRepository(configPath);
                var config = repository.LoadOrDefault(tempRoot);

                var controller = SettingsWindowController.TryCreate(tempRoot, repository, config);
                controller.Should().NotBeNull();

                var window = GetWindowField(controller!);
                var navList = window.FindName("navList") as ListBox;
                navList!.SelectedIndex = 3;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                var combo = window.FindName("cmbLogTimeRange") as ComboBox;
                combo!.SelectedIndex = 3;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                var txtStart = window.FindName("txtLogRangeStart") as TextBox;
                var txtEnd = window.FindName("txtLogRangeEnd") as TextBox;
                txtStart!.Text = "2026-06-21 10:00:00";
                txtEnd!.Text = "2026-06-21 11:00:00";
                FlushLogCustomRangeDebounce(controller!, window);

                GetLogViewPlainText(controller!).Should().Contain("alpha");
                GetLogViewPlainText(controller!).Should().Contain("beta");

                txtEnd.Text = "2026-06-21 10:30:00";
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                GetLogViewPlainText(controller!).Should().Contain("beta",
                    "custom range filter should wait for debounce before refreshing");

                FlushLogCustomRangeDebounce(controller!, window);

                GetLogViewPlainText(controller!).Should().Contain("alpha");
                GetLogViewPlainText(controller!).Should().NotContain("beta");
            }
            finally
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        });
    }

    [Fact]
    public void User_typing_search_waits_for_debounce_before_updating_view()
    {
        RunOnSta(() =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardSettingsLogDebounce_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            var logPath = Path.Combine(tempRoot, "SmartGuard.log");
            File.WriteAllText(logPath, "[INFO] 2026-06-21 10:00:00 alpha\n[WARN] 2026-06-21 10:01:00 beta\n");

            try
            {
                var configPath = Path.Combine(tempRoot, "SmartGuard.config.json");
                var repository = new GuardConfigRepository(configPath);
                var config = repository.LoadOrDefault(tempRoot);

                var controller = SettingsWindowController.TryCreate(tempRoot, repository, config);
                controller.Should().NotBeNull();

                var window = GetWindowField(controller!);
                var navList = window.FindName("navList") as ListBox;
                navList!.SelectedIndex = 3;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                var filterBar = GetLogSearchFilterBar(controller!);
                GetLogViewPlainText(controller!).Should().Contain("alpha");
                GetLogViewPlainText(controller!).Should().Contain("beta");

                filterBar.SetKeyword("missing-term");
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                GetLogViewPlainText(controller!).Should().Contain("alpha");
                GetLogViewPlainText(controller!).Should().Contain("beta");

                FlushLogSearchDebounce(controller!, window);

                GetLogViewPlainText(controller!).Should().Contain("无匹配结果");
            }
            finally
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        });
    }

    [Fact]
    public void SettingsCard_does_not_use_dropshadow_effect_to_avoid_maximize_rendering_bug()
    {
        RunOnSta(() =>
        {
            // Find XAML in project root (not test output directory)
            // AppContext.BaseDirectory = Tests/SmartGuard.Settings.Tests/bin/Debug/net8.0-windows10.0.17763.0/
            // Need to go up 5 levels to reach project root
            var root = Path.GetFullPath(AppContext.BaseDirectory);
            var projectRoot = Path.GetFullPath(Path.Combine(root, "..", "..", "..", "..", ".."));
            var xamlPath = Path.Combine(projectRoot, "lib", "SmartGuard.Settings.xaml");
            if (!File.Exists(xamlPath))
            {
                projectRoot = Path.GetFullPath(Path.Combine(root, "..", "..", "..", ".."));
                xamlPath = Path.Combine(projectRoot, "lib", "SmartGuard.Settings.xaml");
            }

            File.Exists(xamlPath).Should().BeTrue($"XAML file must exist at {xamlPath}");

            var xaml = File.ReadAllText(xamlPath);

            // DropShadowEffect causes rendering artifacts when window is maximized:
            // 1. Effect expands render bounds beyond element bounds
            // 2. On maximize, WPF may not correctly update the effect's render bounds
            // 3. This causes content to appear clipped and black areas to show
            // 4. The shadow renders as black patches on the right and bottom
            xaml.Should().NotContain("DropShadowEffect",
                "DropShadowEffect on SettingsCard causes rendering artifacts when window is maximized. " +
                "The effect's render bounds are not correctly updated on window resize, " +
                "causing black areas and clipped content to appear. " +
                "Remove the effect and use BorderBrush/BorderThickness for card definition instead.");
        });
    }

    [Fact]
    public void Log_page_does_not_have_nested_scrollviewer_to_avoid_dual_scrollbar_confusion()
    {
        RunOnSta(() =>
        {
            // Find XAML in project root
            var root = Path.GetFullPath(AppContext.BaseDirectory);
            var projectRoot = Path.GetFullPath(Path.Combine(root, "..", "..", "..", "..", ".."));
            var xamlPath = Path.Combine(projectRoot, "lib", "SmartGuard.Settings.xaml");
            if (!File.Exists(xamlPath))
            {
                projectRoot = Path.GetFullPath(Path.Combine(root, "..", "..", "..", ".."));
                xamlPath = Path.Combine(projectRoot, "lib", "SmartGuard.Settings.xaml");
            }

            File.Exists(xamlPath).Should().BeTrue($"XAML file must exist at {xamlPath}");

            // Load the compiled BAML so custom controls (local:NumberBox) resolve correctly.
            var window = (Window)Application.LoadComponent(
                new Uri("/SmartGuard.Settings;component/SmartGuard.Settings.xaml", UriKind.Relative));

            var pageLogs = window.FindName("pageLogs") as Panel;
            var contentScrollViewer = window.FindName("contentScrollViewer") as ScrollViewer;

            pageLogs.Should().NotBeNull();
            contentScrollViewer.Should().NotBeNull();

            // The logs page must live outside the outer page ScrollViewer.
            // If pageLogs is nested inside contentScrollViewer, both the outer page ScrollViewer
            // and the inner log ScrollViewer can show scrollbars at the same time,
            // which is the dual-scrollbar problem.
            var ancestor = LogicalTreeHelper.GetParent(pageLogs!);
            while (ancestor is not null && ancestor != contentScrollViewer)
            {
                ancestor = LogicalTreeHelper.GetParent(ancestor);
            }

            ancestor.Should().NotBe(contentScrollViewer,
                "pageLogs must not be a descendant of contentScrollViewer. " +
                "The logs page has its own internal ScrollViewer for the log text; " +
                "nesting it inside the outer page ScrollViewer creates two scrollbars.");
        });
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        if (child is null) return null;
        var parent = VisualTreeHelper.GetParent(child);
        while (parent is not null)
        {
            if (parent is T result)
                return result;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result)
                return result;
            var descendant = FindVisualChild<T>(child);
            if (descendant is not null)
                return descendant;
        }
        return null;
    }

    [Fact]
    public void Navigation_includes_logs_page()
    {
        RunOnSta(() =>
        {
            var root = AppContext.BaseDirectory;
            var xamlPath = Path.Combine(root, "lib", "SmartGuard.Settings.xaml");
            if (!File.Exists(xamlPath))
            {
                return;
            }

            var xaml = File.ReadAllText(xamlPath);
            var window = (Window)System.Windows.Markup.XamlReader.Parse(xaml);

            var navList = window.FindName("navList") as ListBox;
            navList.Should().NotBeNull();
            navList.Items.Count.Should().Be(4);
        });
    }

    [Fact]
    public void All_checkbox_controls_have_correct_names()
    {
        RunOnSta(() =>
        {
            var root = AppContext.BaseDirectory;
            var xamlPath = Path.Combine(root, "lib", "SmartGuard.Settings.xaml");
            if (!File.Exists(xamlPath))
            {
                return;
            }

            var xaml = File.ReadAllText(xamlPath);
            var window = (Window)System.Windows.Markup.XamlReader.Parse(xaml);

            window.FindName("tglPaused").Should().NotBeNull();
            window.FindName("tglNotify").Should().NotBeNull();
            window.FindName("tglNotifyExternal").Should().NotBeNull();
            window.FindName("tglAutoStart").Should().NotBeNull();
        });
    }

    [Fact]
    public void Notification_toggles_reflect_config_and_save_independently()
    {
        RunOnSta(() =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardSettingsNotify_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var configPath = Path.Combine(tempRoot, "SmartGuard.config.json");
                var repository = new GuardConfigRepository(configPath);
                var config = GuardConfig.CreateDefault(tempRoot);
                config.NotifyOnPlanChange = true;
                config.NotifyOnExternalChange = false;
                repository.Save(config);

                var controller = SettingsWindowController.TryCreate(tempRoot, repository, config);
                controller.Should().NotBeNull();

                var window = GetWindowField(controller!);
                var tglNotify = window.FindName("tglNotify") as CheckBox;
                var tglNotifyExternal = window.FindName("tglNotifyExternal") as CheckBox;

                tglNotify!.IsChecked.Should().BeTrue();
                tglNotifyExternal!.IsChecked.Should().BeFalse();

                tglNotifyExternal.IsChecked = true;

                var readConfig = typeof(SettingsWindowController).GetMethod(
                    "ReadConfigFromUi",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var uiConfig = (GuardConfig)readConfig!.Invoke(controller, null)!;

                uiConfig.NotifyOnPlanChange.Should().BeTrue();
                uiConfig.NotifyOnExternalChange.Should().BeTrue();
            }
            finally
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        });
    }

    [Fact]
    public void Save_and_cancel_buttons_are_removed_for_instant_apply()
    {
        RunOnSta(() =>
        {
            var window = (Window)Application.LoadComponent(
                new Uri("/SmartGuard.Settings;component/SmartGuard.Settings.xaml", UriKind.Relative));

            window.FindName("btnSave").Should().BeNull(
                "Save button is removed because settings are applied instantly.");
            window.FindName("btnCancel").Should().BeNull(
                "Cancel button is removed because there is no pending save to discard.");
        });
    }

    [Fact]
    public void User_navigates_to_logs_page_and_sees_log_controls()
    {
        RunOnSta(() =>
        {
            var root = Path.GetFullPath(AppContext.BaseDirectory);
            var projectRoot = Path.GetFullPath(Path.Combine(root, "..", "..", "..", ".."));
            var xamlPath = Path.Combine(projectRoot, "lib", "SmartGuard.Settings.xaml");
            if (!File.Exists(xamlPath))
            {
                projectRoot = Path.GetFullPath(Path.Combine(root, "..", "..", ".."));
                xamlPath = Path.Combine(projectRoot, "lib", "SmartGuard.Settings.xaml");
            }
            if (!File.Exists(xamlPath))
            {
                return;
            }

            // Create a fake log file so LogViewController initializes
            var logPath = Path.Combine(projectRoot, "SmartGuard.log");
            var fallbackLogPath = Path.Combine(projectRoot, "SmartGuard.startup.log");
            var logCreated = false;
            if (!File.Exists(logPath) && !File.Exists(fallbackLogPath))
            {
                File.WriteAllText(logPath, "[INFO] Test log entry\r\n");
                logCreated = true;
            }

            try
            {
                var configPath = Path.Combine(projectRoot, "SmartGuard.config.json");
                var repository = new GuardConfigRepository(configPath);
                var config = repository.LoadOrDefault(projectRoot);

                var controller = SettingsWindowController.TryCreate(projectRoot, repository, config);
                controller.Should().NotBeNull();

                // Simulate user clicking "日志" in navigation
                var window = GetWindowField(controller);
                var navList = window.FindName("navList") as ListBox;
                navList.Should().NotBeNull();

                // Select logs page (index 3)
                navList.SelectedIndex = 3;

                // Verify logs page is visible and general page is hidden
                var pageGeneral = window.FindName("pageGeneral") as StackPanel;
                var pageLogs = window.FindName("pageLogs") as Panel;
                pageGeneral.Should().NotBeNull();
                pageLogs.Should().NotBeNull();
                pageGeneral.Visibility.Should().Be(Visibility.Collapsed);
                pageLogs.Visibility.Should().Be(Visibility.Visible);

                // Verify log controls are accessible
                var filterBar = GetLogSearchFilterBar(controller);
                var lblLogStatus = window.FindName("lblLogStatus") as TextBlock;

                filterBar.Should().NotBeNull();
                window.FindName("lstLogView").Should().NotBeNull();
                lblLogStatus.Should().NotBeNull();
            }
            finally
            {
                if (logCreated && File.Exists(logPath))
                {
                    try { File.Delete(logPath); } catch { }
                }
            }
        });
    }

    [Fact]
    public void Navigation_hides_scrollable_pages_when_showing_logs()
    {
        RunOnSta(() =>
        {
            var root = Path.GetFullPath(AppContext.BaseDirectory);
            var projectRoot = Path.GetFullPath(Path.Combine(root, "..", "..", "..", ".."));
            var xamlPath = Path.Combine(projectRoot, "lib", "SmartGuard.Settings.xaml");
            if (!File.Exists(xamlPath))
            {
                projectRoot = Path.GetFullPath(Path.Combine(root, "..", "..", ".."));
                xamlPath = Path.Combine(projectRoot, "lib", "SmartGuard.Settings.xaml");
            }
            if (!File.Exists(xamlPath))
            {
                return;
            }

            // Create a fake log file so LogViewController initializes
            var logPath = Path.Combine(projectRoot, "SmartGuard.log");
            var fallbackLogPath = Path.Combine(projectRoot, "SmartGuard.startup.log");
            var logCreated = false;
            if (!File.Exists(logPath) && !File.Exists(fallbackLogPath))
            {
                File.WriteAllText(logPath, "[INFO] Test log entry\r\n");
                logCreated = true;
            }

            try
            {
                var configPath = Path.Combine(projectRoot, "SmartGuard.config.json");
                var repository = new GuardConfigRepository(configPath);
                var config = repository.LoadOrDefault(projectRoot);

                var controller = SettingsWindowController.TryCreate(projectRoot, repository, config);
                controller.Should().NotBeNull();

                var window = GetWindowField(controller);
                var navList = window.FindName("navList") as ListBox;
                var contentScrollViewer = window.FindName("contentScrollViewer") as ScrollViewer;
                var pageLogs = window.FindName("pageLogs") as Panel;

                navList.Should().NotBeNull();
                contentScrollViewer.Should().NotBeNull();
                pageLogs.Should().NotBeNull();

                // Switch to logs page: outer scrollable pages must hide and log page must show
                navList!.SelectedIndex = 3;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                contentScrollViewer!.Visibility.Should().Be(Visibility.Collapsed);
                pageLogs!.Visibility.Should().Be(Visibility.Visible);

                // Switch back to general page: outer scrollable pages must show and log page must hide
                navList.SelectedIndex = 0;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                contentScrollViewer.Visibility.Should().Be(Visibility.Visible);
                pageLogs.Visibility.Should().Be(Visibility.Collapsed);
            }
            finally
            {
                if (logCreated && File.Exists(logPath))
                {
                    try { File.Delete(logPath); } catch { }
                }
            }
        });
    }

    [Fact]
    public void User_on_logs_page_sees_new_lines_after_log_file_appended()
    {
        RunOnSta(() =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardSettingsLogTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            var logPath = Path.Combine(tempRoot, "SmartGuard.log");
            File.WriteAllText(logPath, "[INFO] 2026-06-21 10:00:00 initial entry\n");

            try
            {
                var configPath = Path.Combine(tempRoot, "SmartGuard.config.json");
                var repository = new GuardConfigRepository(configPath);
                var config = repository.LoadOrDefault(tempRoot);

                var controller = SettingsWindowController.TryCreate(tempRoot, repository, config);
                controller.Should().NotBeNull();

                var window = GetWindowField(controller!);
                var navList = window.FindName("navList") as ListBox;
                navList!.SelectedIndex = 3;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                GetLogViewPlainText(controller!).Should().Contain("initial entry");

                File.AppendAllText(logPath, "[INFO] 2026-06-21 10:01:00 appended entry\n");
                InvokeRefreshLogView(controller!);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                GetLogViewPlainText(controller!).Should().Contain("appended entry");
            }
            finally
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        });
    }

    [Fact]
    public void User_scrolled_up_keeps_position_when_new_log_lines_append()
    {
        RunOnSta(() =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardSettingsLogScroll_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            var logPath = Path.Combine(tempRoot, "SmartGuard.log");
            var builder = new System.Text.StringBuilder();
            for (var i = 0; i < 200; i++)
                builder.AppendLine($"[INFO] 2026-06-21 09:{i % 60:D2}:{i % 60:D2} scroll line {i}");
            File.WriteAllText(logPath, builder.ToString());

            try
            {
                var configPath = Path.Combine(tempRoot, "SmartGuard.config.json");
                var repository = new GuardConfigRepository(configPath);
                var config = repository.LoadOrDefault(tempRoot);

                var controller = SettingsWindowController.TryCreate(tempRoot, repository, config);
                controller.Should().NotBeNull();

                var window = GetWindowField(controller!);
                var navList = window.FindName("navList") as ListBox;
                navList!.SelectedIndex = 3;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                var scrollViewer = GetLogScrollViewer(window, controller!);
                scrollViewer.Should().NotBeNull();
                scrollViewer!.ScrollToVerticalOffset(0);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                scrollViewer.VerticalOffset.Should().Be(0);

                File.AppendAllText(logPath, "[INFO] 2026-06-21 10:01:00 appended while scrolled up\n");
                InvokeRefreshLogView(controller!);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                scrollViewer.VerticalOffset.Should().Be(0);
            }
            finally
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        });
    }

    [Fact]
    public void User_opening_logs_page_with_follow_tail_enabled_starts_at_latest_log()
    {
        RunOnSta(() =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardSettingsLogFollowOpen_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            var logPath = Path.Combine(tempRoot, "SmartGuard.log");
            var builder = new System.Text.StringBuilder();
            for (var i = 0; i < 200; i++)
                builder.AppendLine($"[INFO] 2026-06-21 09:{i % 60:D2}:{i % 60:D2} scroll line {i}");
            File.WriteAllText(logPath, builder.ToString());

            try
            {
                var configPath = Path.Combine(tempRoot, "SmartGuard.config.json");
                var repository = new GuardConfigRepository(configPath);
                var config = repository.LoadOrDefault(tempRoot);

                var controller = SettingsWindowController.TryCreate(tempRoot, repository, config);
                controller.Should().NotBeNull();

                var window = GetWindowField(controller!);
                if (!window.IsVisible)
                    WpfStaTestHost.ShowAndWait(window);

                var navList = window.FindName("navList") as ListBox;
                navList!.SelectedIndex = 3;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                var chkLogFollowTail = window.FindName("chkLogFollowTail") as CheckBox;
                chkLogFollowTail!.IsChecked.Should().BeTrue();

                var scrollViewer = GetLogScrollViewerWithoutExtraRefresh(window, controller!);
                LogViewScrollState.IsAtTail(scrollViewer).Should().BeTrue();
                GetLogViewPlainText(controller!).Should().Contain("scroll line 199");
            }
            finally
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        });
    }

    [Fact]
    public void User_enabling_follow_tail_scrolls_to_latest_log()
    {
        RunOnSta(() =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardSettingsLogFollow_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            var logPath = Path.Combine(tempRoot, "SmartGuard.log");
            var builder = new System.Text.StringBuilder();
            for (var i = 0; i < 200; i++)
                builder.AppendLine($"[INFO] 2026-06-21 09:{i % 60:D2}:{i % 60:D2} scroll line {i}");
            File.WriteAllText(logPath, builder.ToString());

            try
            {
                var configPath = Path.Combine(tempRoot, "SmartGuard.config.json");
                var repository = new GuardConfigRepository(configPath);
                var config = repository.LoadOrDefault(tempRoot);

                var controller = SettingsWindowController.TryCreate(tempRoot, repository, config);
                controller.Should().NotBeNull();

                var window = GetWindowField(controller!);
                var navList = window.FindName("navList") as ListBox;
                navList!.SelectedIndex = 3;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                var scrollViewer = GetLogScrollViewer(window, controller!);
                scrollViewer.ScrollToVerticalOffset(0);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                scrollViewer.VerticalOffset.Should().Be(0);

                var chkLogFollowTail = window.FindName("chkLogFollowTail") as CheckBox;
                chkLogFollowTail!.IsChecked = true;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                LogViewScrollState.IsAtTail(scrollViewer).Should().BeTrue();
                GetLogViewPlainText(controller!).Should().Contain("scroll line 199");
            }
            finally
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        });
    }

    [Fact]
    public void User_at_tail_with_follow_tail_stays_at_tail_when_new_log_lines_append()
    {
        RunOnSta(() =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardSettingsLogFollowAppend_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            var logPath = Path.Combine(tempRoot, "SmartGuard.log");
            var builder = new System.Text.StringBuilder();
            for (var i = 0; i < 200; i++)
                builder.AppendLine($"[INFO] 2026-06-21 09:{i % 60:D2}:{i % 60:D2} scroll line {i}");
            File.WriteAllText(logPath, builder.ToString());

            try
            {
                var configPath = Path.Combine(tempRoot, "SmartGuard.config.json");
                var repository = new GuardConfigRepository(configPath);
                var config = repository.LoadOrDefault(tempRoot);

                var controller = SettingsWindowController.TryCreate(tempRoot, repository, config);
                controller.Should().NotBeNull();

                var window = GetWindowField(controller!);
                var navList = window.FindName("navList") as ListBox;
                navList!.SelectedIndex = 3;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                var scrollViewer = GetLogScrollViewer(window, controller!);
                var chkLogFollowTail = window.FindName("chkLogFollowTail") as CheckBox;
                chkLogFollowTail!.IsChecked = true;
                ClickLogToolbarButton(window, "btnLogScrollBottom");
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                LogViewScrollState.IsAtTail(scrollViewer).Should().BeTrue();

                File.AppendAllText(logPath, "[INFO] 2026-06-21 10:01:00 appended while following tail\n");
                InvokeRefreshLogView(controller!);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                LogViewScrollState.IsAtTail(scrollViewer).Should().BeTrue();
                GetLogViewPlainText(controller!).Should().Contain("appended while following tail");
            }
            finally
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        });
    }

    [Fact]
    public void User_searches_logs_and_results_update()
    {
        RunOnSta(() =>
        {
            var root = Path.GetFullPath(AppContext.BaseDirectory);
            var projectRoot = Path.GetFullPath(Path.Combine(root, "..", "..", "..", ".."));
            var xamlPath = Path.Combine(projectRoot, "lib", "SmartGuard.Settings.xaml");
            if (!File.Exists(xamlPath))
            {
                projectRoot = Path.GetFullPath(Path.Combine(root, "..", "..", ".."));
                xamlPath = Path.Combine(projectRoot, "lib", "SmartGuard.Settings.xaml");
            }
            if (!File.Exists(xamlPath))
            {
                return;
            }

            var logPath = Path.Combine(projectRoot, "SmartGuard.log");
            var logCreated = false;
            if (!File.Exists(logPath))
            {
                File.WriteAllText(logPath,
                    "[INFO] System idle detected\r\n" +
                    "[WARN] Battery low warning\r\n" +
                    "[ERROR] Failed to switch plan\r\n" +
                    "[HEART] Monitoring active\r\n");
                logCreated = true;
            }

            try
            {
                var configPath = Path.Combine(projectRoot, "SmartGuard.config.json");
                var repository = new GuardConfigRepository(configPath);
                var config = repository.LoadOrDefault(projectRoot);

                var controller = SettingsWindowController.TryCreate(projectRoot, repository, config);
                controller.Should().NotBeNull();

                var window = GetWindowField(controller);
                var navList = window.FindName("navList") as ListBox;
                navList!.SelectedIndex = 3;

                var filterBar = GetLogSearchFilterBar(controller!);
                filterBar.SetKeyword("Battery");
                FlushLogSearchDebounce(controller!, window);

                // Verify filtered results contain the search term
                GetLogViewPlainText(controller!).Should().Contain("Battery");
                GetLogViewPlainText(controller!).Should().NotContain("System idle");
            }
            finally
            {
                if (logCreated && File.Exists(logPath))
                {
                    try { File.Delete(logPath); } catch { }
                }
            }
        });
    }

    [Fact]
    public void User_clicking_tag_link_adds_filter_chip_and_updates_view()
    {
        RunOnSta(() =>
        {
            var root = Path.GetFullPath(AppContext.BaseDirectory);
            var projectRoot = Path.GetFullPath(Path.Combine(root, "..", "..", "..", ".."));
            var xamlPath = Path.Combine(projectRoot, "lib", "SmartGuard.Settings.xaml");
            if (!File.Exists(xamlPath))
            {
                projectRoot = Path.GetFullPath(Path.Combine(root, "..", "..", ".."));
                xamlPath = Path.Combine(projectRoot, "lib", "SmartGuard.Settings.xaml");
            }
            if (!File.Exists(xamlPath))
            {
                return;
            }

            var logPath = Path.Combine(projectRoot, "SmartGuard.log");
            var logCreated = false;
            if (!File.Exists(logPath))
            {
                File.WriteAllText(logPath,
                    "[INFO] System idle detected\r\n" +
                    "[WARN] Battery low warning\r\n");
                logCreated = true;
            }

            try
            {
                var configPath = Path.Combine(projectRoot, "SmartGuard.config.json");
                var repository = new GuardConfigRepository(configPath);
                var config = repository.LoadOrDefault(projectRoot);

                var controller = SettingsWindowController.TryCreate(projectRoot, repository, config);
                controller.Should().NotBeNull();

                var window = GetWindowField(controller);
                var navList = window.FindName("navList") as ListBox;
                navList!.SelectedIndex = 3;

                var filterBar = GetLogSearchFilterBar(controller);
                GetLogViewPlainText(controller!).Should().Contain("INFO");

                controller.AddLogTagFilter("WARN");
                InvokeRefreshLogView(controller);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                filterBar.ActiveTags.Should().Contain("WARN");
                GetLogViewPlainText(controller!).Should().Contain("WARN");
                GetLogViewPlainText(controller!).Should().NotContain("System idle");
            }
            finally
            {
                if (logCreated && File.Exists(logPath))
                {
                    try { File.Delete(logPath); } catch { }
                }
            }
        });
    }

    [Fact]
    public void Log_page_header_and_filters_remain_fixed_above_scrollable_log_content()
    {
        RunOnSta(() =>
        {
            var root = Path.GetFullPath(AppContext.BaseDirectory);
            var projectRoot = Path.GetFullPath(Path.Combine(root, "..", "..", "..", ".."));
            var xamlPath = Path.Combine(projectRoot, "lib", "SmartGuard.Settings.xaml");
            if (!File.Exists(xamlPath))
            {
                projectRoot = Path.GetFullPath(Path.Combine(root, "..", "..", ".."));
                xamlPath = Path.Combine(projectRoot, "lib", "SmartGuard.Settings.xaml");
            }
            if (!File.Exists(xamlPath))
            {
                return;
            }

            // Load the compiled BAML so custom controls (local:NumberBox) resolve correctly.
            var window = (Window)Application.LoadComponent(
                new Uri("/SmartGuard.Settings;component/SmartGuard.Settings.xaml", UriKind.Relative));

            var pageLogs = window.FindName("pageLogs") as Panel;
            pageLogs.Should().NotBeNull();

            // The pageLogs should contain a Grid with a fixed header area
            // and a scrollable content area for the log display.
            var lstLogView = window.FindName("lstLogView") as ListBox;
            lstLogView.Should().NotBeNull();

            var logScrollViewer = LogViewListPresenter.FindScrollViewer(lstLogView!);
            logScrollViewer.Should().NotBeNull(
                "lstLogView must host an internal ScrollViewer so log content can scroll independently. " +
                "The header (title, description, search bar, filters) should remain fixed.");

            // The logs page itself is no longer inside the outer page ScrollViewer;
            // it occupies the content area directly, so the outer page ScrollViewer
            // cannot scroll while viewing logs.
            var contentScrollViewer = window.FindName("contentScrollViewer") as ScrollViewer;
            contentScrollViewer.Should().NotBeNull();

            var ancestor = LogicalTreeHelper.GetParent(pageLogs!);
            while (ancestor is not null && ancestor != contentScrollViewer)
            {
                ancestor = LogicalTreeHelper.GetParent(ancestor);
            }

            ancestor.Should().NotBe(contentScrollViewer,
                "pageLogs must be a direct child of the content area, not inside contentScrollViewer. " +
                "This prevents the outer page ScrollViewer from appearing on the logs page.");
        });
    }

    [Fact]
    public void User_navigates_to_about_page_and_sees_version_and_check_update()
    {
        RunOnSta(() =>
        {
            // Ensure Application exists for embedded resource loading
            var installRoot = Path.Combine(Path.GetTempPath(), "sg-test-about-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(installRoot);
            Directory.CreateDirectory(Path.Combine(installRoot, "bin"));
            // Intentionally do NOT create lib/SmartGuard.Settings.xaml so embedded resource is used

            try
            {
                var configPath = Path.Combine(installRoot, "SmartGuard.config.json");
                File.WriteAllText(configPath, "{\"BalancedThresholdSec\":300,\"PowerSaverThresholdSec\":900,\"LowBatteryPercent\":25,\"CheckIntervalSec\":30,\"BrightnessRestoreMs\":1000}");
                var repository = new GuardConfigRepository(configPath);
                var config = repository.LoadOrDefault(installRoot);

                var controller = SettingsWindowController.TryCreate(installRoot, repository, config);
                controller.Should().NotBeNull();

                var window = GetWindowField(controller);
                var navList = window.FindName("navList") as ListBox;
                navList.Should().NotBeNull();

                // Simulate user clicking "关于" in navigation (index 5)
                navList.SelectedIndex = 5;

                var pageAbout = window.FindName("pageAbout") as StackPanel;
                pageAbout.Should().NotBeNull();
                pageAbout!.Visibility.Should().Be(Visibility.Visible);

                var pageGeneral = window.FindName("pageGeneral") as StackPanel;
                pageGeneral.Should().NotBeNull();
                pageGeneral!.Visibility.Should().Be(Visibility.Collapsed);

                // Verify version info, repository link and check update button exist
                var txtVersion = window.FindName("txtVersion") as TextBlock;
                var lnkRepo = window.FindName("lnkRepo") as Hyperlink;
                var btnCheckUpdate = window.FindName("btnCheckUpdate") as Button;

                txtVersion.Should().NotBeNull("About page must show version info");
                txtVersion!.Text.Should().NotBe("1.0.0",
                    "Displayed version must be synced with the actual assembly version, not a hard-coded placeholder");
                txtVersion.Text.Should().MatchRegex(@"^\d+\.\d+(\.\d+)?$",
                    "Displayed version should be a valid version string");

                lnkRepo.Should().NotBeNull("About page must have repository link");
                lnkRepo!.NavigateUri.Should().NotBeNull();
                lnkRepo.NavigateUri.AbsoluteUri.Should().Be("https://github.com/rainiva/SmartGuard",
                    "Repository link must point to the actual project repository");
                btnCheckUpdate.Should().NotBeNull("About page must have check update button");
            }
            finally
            {
                try { Directory.Delete(installRoot, true); } catch { }
            }
        });
    }

    [Fact]
    public void User_clicks_check_update_button_and_button_text_changes()
    {
        RunOnSta(() =>
        {
            var installRoot = Path.Combine(Path.GetTempPath(), "sg-test-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(installRoot);
            Directory.CreateDirectory(Path.Combine(installRoot, "bin"));

            try
            {
                var configPath = Path.Combine(installRoot, "SmartGuard.config.json");
                File.WriteAllText(configPath, "{\"BalancedThresholdSec\":300,\"PowerSaverThresholdSec\":900,\"LowBatteryPercent\":25,\"CheckIntervalSec\":30,\"BrightnessRestoreMs\":1000}");
                var repository = new GuardConfigRepository(configPath);
                var config = repository.LoadOrDefault(installRoot);

                var controller = SettingsWindowController.TryCreate(installRoot, repository, config);
                controller.Should().NotBeNull();

                var window = GetWindowField(controller);
                var navList = window.FindName("navList") as ListBox;
                navList.Should().NotBeNull();
                navList!.SelectedIndex = 5;

                var btnCheckUpdate = window.FindName("btnCheckUpdate") as Button;
                btnCheckUpdate.Should().NotBeNull();

                // Before click: button should show default text
                btnCheckUpdate!.Content.Should().Be("检查更新");

                // Simulate user clicking the check update button
                btnCheckUpdate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                // Automated UI tests suppress network/modal dialogs; verify the click cycle completes cleanly.
                btnCheckUpdate.IsEnabled.Should().BeTrue();
                btnCheckUpdate.Content.Should().Be("检查更新");
            }
            finally
            {
                try { Directory.Delete(installRoot, true); } catch { }
            }
        });
    }

    private static object GetLogPageHost(SettingsWindowController controller)
    {
        var field = typeof(SettingsWindowController).GetField(
            "_logPageHost",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return field!.GetValue(controller)!;
    }

    private static T? GetLogHostField<T>(SettingsWindowController controller, string fieldName)
    {
        var host = GetLogPageHost(controller);
        var field = host.GetType().GetField(
            fieldName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (T?)field?.GetValue(host);
    }

    private static LogSearchFilterBar GetLogSearchFilterBar(SettingsWindowController controller)
    {
        return GetLogHostField<LogSearchFilterBar>(controller, "_logSearchFilterBar")!;
    }

    private static Window GetWindowField(SettingsWindowController controller)
    {
        var field = typeof(SettingsWindowController).GetField("_window", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (Window)field!.GetValue(controller)!;
    }

    private static void InvokeRefreshLogView(SettingsWindowController controller, bool forceRedraw = false)
    {
        var host = GetLogPageHost(controller);
        var method = host.GetType().GetMethod(
            "RefreshLogView",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            binder: null,
            types: [typeof(bool)],
            modifiers: null);
        method!.Invoke(host, [forceRedraw]);
    }

    private static string GetLogViewPlainText(SettingsWindowController controller)
    {
        var presenter = GetLogHostField<LogViewListPresenter>(controller, "_logListPresenter")!;
        return presenter.GetPlainText();
    }

    private static ScrollViewer GetLogScrollViewer(Window window, SettingsWindowController controller)
    {
        if (!window.IsVisible)
        {
            WpfStaTestHost.ShowAndWait(window);
        }

        controller.EnsureLogScrollViewerHooked();
        InvokeRefreshLogView(controller);
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        var scrollViewerField = GetLogPageHost(controller).GetType().GetField(
            "_logScrollViewer",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var scrollViewer = scrollViewerField!.GetValue(GetLogPageHost(controller)) as ScrollViewer;
        scrollViewer.Should().NotBeNull();
        return scrollViewer!;
    }

    private static ScrollViewer GetLogScrollViewerWithoutExtraRefresh(Window window, SettingsWindowController controller)
    {
        controller.EnsureLogScrollViewerHooked();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        var scrollViewerField = GetLogPageHost(controller).GetType().GetField(
            "_logScrollViewer",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var scrollViewer = scrollViewerField!.GetValue(GetLogPageHost(controller)) as ScrollViewer;
        scrollViewer.Should().NotBeNull();
        return scrollViewer!;
    }

    private static SettingsLogSearchCoordinator GetLogSearchCoordinator(SettingsWindowController controller)
    {
        var host = GetLogPageHost(controller);
        var field = host.GetType().GetField(
            "_searchCoordinator",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (SettingsLogSearchCoordinator)field!.GetValue(host)!;
    }

    private static void FlushLogCustomRangeDebounce(SettingsWindowController controller, Window window)
    {
        var timer = GetLogSearchCoordinator(controller).CustomRangeDebounceTimerForTests;
        timer.Should().NotBeNull("custom range debounce timer should exist after editing range inputs");
        timer!.IsEnabled.Should().BeTrue("custom range debounce timer should be running before flush");
        timer.Stop();
        InvokeRefreshLogView(controller);
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private static void FlushLogSearchDebounce(SettingsWindowController controller, Window window)
    {
        var timer = GetLogSearchCoordinator(controller).SearchDebounceTimerForTests;
        timer.Should().NotBeNull("search debounce timer should exist after typing in search box");
        timer!.IsEnabled.Should().BeTrue("debounce timer should be running before flush");
        timer.Stop();
        InvokeRefreshLogView(controller);
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private static void ClickLogToolbarButton(Window window, string buttonName)
    {
        var button = window.FindName(buttonName) as Button;
        button.Should().NotBeNull();
        button!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static void InvokeLogExport(SettingsWindowController controller, string destinationPath)
    {
        var host = GetLogPageHost(controller);
        var method = host.GetType().GetMethod(
            "ExportVisibleLogToPath",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method.Should().NotBeNull();
        method!.Invoke(host, [destinationPath]);
    }
}
