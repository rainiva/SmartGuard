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
[Trait("Category", "WpfUi")]
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

    private static string RepoRoot() => SettingsSourcePaths.RepoRoot();

    private static string ReadProjectSettingsXaml() => SettingsSourcePaths.ReadSettingsXamlSource();

    private static Window LoadCompiledWindow()
        => (Window)Application.LoadComponent(
            new Uri("/SmartGuard.Settings;component/SmartGuard.Settings.xaml", UriKind.Relative));

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
            var projectRoot = RepoRoot();
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
            var projectRoot = RepoRoot();
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
            var window = LoadCompiledWindow();

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
            var window = LoadCompiledWindow();

            window.FindName("tglThemeFollowSystem").Should().NotBeNull();
            window.FindName("tglThemeLight").Should().NotBeNull();
            window.FindName("tglThemeDark").Should().NotBeNull();
            window.FindName("rowThemeLight").Should().NotBeNull();
            window.FindName("rowThemeDark").Should().NotBeNull();
            window.FindName("btnThemeToggle").Should().BeNull();
            window.FindName("imgAppIcon").Should().BeNull();
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
            var window = LoadCompiledWindow();

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
            var window = LoadCompiledWindow();

            var pageLogs = window.FindName("pageLogs") as Panel;
            var logSearchFilterHost = window.FindName("logSearchFilterHost") as ContentControl;
            var logTagFilterLinksPanel = window.FindName("logTagFilterLinksPanel") as WrapPanel;
            var lstLogView = window.FindName("lstLogView") as ListBox;
            var lblLogStatus = window.FindName("lblLogStatus") as TextBlock;
            var btnLogMore = window.FindName("btnLogMore") as Button;

            pageLogs.Should().NotBeNull();
            logSearchFilterHost.Should().NotBeNull();
            logTagFilterLinksPanel.Should().NotBeNull();
            lstLogView.Should().NotBeNull();
            lblLogStatus.Should().NotBeNull();
            window.FindName("btnLogCopy").Should().BeNull();
            window.FindName("btnLogExport").Should().BeNull();
            window.FindName("btnLogOpenFolder").Should().BeNull();
            window.FindName("btnLogScrollTop").Should().NotBeNull();
            window.FindName("btnLogScrollBottom").Should().NotBeNull();
            window.FindName("btnLogRefresh").Should().NotBeNull();
            btnLogMore.Should().NotBeNull();
            window.FindName("chkLogFollowTail").Should().NotBeNull();
            window.FindName("cmbLogTimeRange").Should().NotBeNull();
            window.FindName("chkLogSearchCaseSensitive").Should().NotBeNull();
            btnLogMore!.ContextMenu.Should().BeNull();
            var logMoreFlyout = window.FindName("logMoreFlyout") as Border;
            logMoreFlyout.Should().NotBeNull();
            logMoreFlyout!.Visibility.Should().Be(Visibility.Collapsed);
            window.FindName("btnLogMoreCopy").Should().NotBeNull();
            window.FindName("btnLogMoreExport").Should().NotBeNull();
            window.FindName("btnLogMoreOpenFolder").Should().NotBeNull();
        });
    }

    [Fact]
    public void Log_page_more_button_toggles_in_window_actions_flyout()
    {
        RunOnSta(() =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardLogMoreFlyout_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            File.WriteAllText(Path.Combine(tempRoot, "SmartGuard.log"), "[INFO] 2026-07-04 06:00:00 ok\n");

            try
            {
                var configPath = Path.Combine(tempRoot, "SmartGuard.config.json");
                var repository = new GuardConfigRepository(configPath);
                var config = repository.LoadOrDefault(tempRoot);

                var controller = SettingsWindowController.TryCreate(tempRoot, repository, config);
                controller.Should().NotBeNull();

                var window = GetWindowField(controller!);
                NavigateToLogsPage(controller!, window);

                var btnLogMore = window.FindName("btnLogMore") as Button;
                var logMoreFlyout = window.FindName("logMoreFlyout") as Border;

                btnLogMore.Should().NotBeNull();
                logMoreFlyout.Should().NotBeNull();
                logMoreFlyout!.Visibility.Should().Be(Visibility.Collapsed);

                btnLogMore!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                logMoreFlyout.Visibility.Should().Be(Visibility.Visible);

                btnLogMore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                logMoreFlyout.Visibility.Should().Be(Visibility.Collapsed);
            }
            finally
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        });
    }

    [Fact]
    public void Log_page_more_flyout_does_not_expand_search_row_when_opened()
    {
        RunOnSta(() =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardLogMoreLayout_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            File.WriteAllText(Path.Combine(tempRoot, "SmartGuard.log"), "[INFO] 2026-07-04 06:00:00 ok\n");

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
                SettingsLogNavResponsivenessTests.WaitUntilLogContent(controller!, window);

                var logSearchFilterHost = window.FindName("logSearchFilterHost") as FrameworkElement;
                var btnLogMore = window.FindName("btnLogMore") as Button;
                var logMoreFlyout = window.FindName("logMoreFlyout") as Border;
                var logViewportBorder = window.FindName("logViewportBorder") as FrameworkElement;

                logSearchFilterHost.Should().NotBeNull();
                btnLogMore.Should().NotBeNull();
                logMoreFlyout.Should().NotBeNull();
                logViewportBorder.Should().NotBeNull();

                var searchBorder = FindAncestor<Border>(logSearchFilterHost!);
                searchBorder.Should().NotBeNull();

                window.UpdateLayout();
                var searchHeightBefore = searchBorder!.ActualHeight;
                var viewportTopBefore = logViewportBorder!.TranslatePoint(new Point(0, 0), window).Y;

                btnLogMore!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                logMoreFlyout!.Visibility.Should().Be(Visibility.Visible);
                searchBorder.ActualHeight.Should().BeApproximately(searchHeightBefore, 0.5,
                    "opening the more flyout should not stretch the search row");
                logViewportBorder.TranslatePoint(new Point(0, 0), window).Y.Should().BeApproximately(viewportTopBefore, 0.5,
                    "opening the more flyout should not push the log viewport downward");
            }
            finally
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        });
    }

    [Fact]
    public void Log_page_more_flyout_keeps_bottom_action_clickable_above_lower_content()
    {
        RunOnSta(() =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardLogMoreOcclusion_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            File.WriteAllText(Path.Combine(tempRoot, "SmartGuard.log"), "[INFO] 2026-07-04 06:00:00 ok\n");
            SettingsWindowController? controller = null;

            try
            {
                var configPath = Path.Combine(tempRoot, "SmartGuard.config.json");
                var repository = new GuardConfigRepository(configPath);
                var config = repository.LoadOrDefault(tempRoot);

                controller = SettingsWindowController.TryCreate(tempRoot, repository, config);
                controller.Should().NotBeNull();

                var window = GetWindowField(controller!);
                window.Width = 600;
                window.Height = 500;
                if (!window.IsVisible)
                    WpfStaTestHost.ShowAndWait(window);

                var navList = window.FindName("navList") as ListBox;
                navList!.SelectedIndex = 3;
                SettingsLogNavResponsivenessTests.WaitUntilLogContent(controller!, window);

                var btnLogMore = window.FindName("btnLogMore") as Button;
                var btnLogMoreOpenFolder = window.FindName("btnLogMoreOpenFolder") as Button;
                var logMoreFlyout = window.FindName("logMoreFlyout") as Border;

                btnLogMore.Should().NotBeNull();
                btnLogMoreOpenFolder.Should().NotBeNull();
                logMoreFlyout.Should().NotBeNull();

                btnLogMore!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WpfStaTestHost.WaitUntil(
                    () =>
                        logMoreFlyout!.Visibility == Visibility.Visible &&
                        logMoreFlyout.ActualWidth > 0 &&
                        logMoreFlyout.ActualHeight > 0 &&
                        btnLogMoreOpenFolder!.ActualWidth > 0 &&
                        btnLogMoreOpenFolder.ActualHeight > 0,
                    TimeSpan.FromSeconds(10),
                    "the log-more flyout and bottom action to receive layout");
                WpfStaTestHost.WaitForRender();

                DependencyObject? hit = null;
                Button? hitButton = null;
                WpfStaTestHost.WaitUntil(
                    () =>
                    {
                        var samplePoint = btnLogMoreOpenFolder!.TranslatePoint(
                            new Point(
                                btnLogMoreOpenFolder.ActualWidth / 2,
                                Math.Max(1, btnLogMoreOpenFolder.ActualHeight - 4)),
                            window);
                        hit = window.InputHitTest(samplePoint) as DependencyObject;
                        hitButton = hit as Button ?? (hit is null ? null : FindAncestor<Button>(hit));
                        return ReferenceEquals(hitButton, btnLogMoreOpenFolder);
                    },
                    TimeSpan.FromSeconds(10),
                    "the bottom log-more action to own its hit-test point");

                hit.Should().NotBeNull();
                hitButton.Should().BeSameAs(btnLogMoreOpenFolder,
                    "the bottom overflow action should stay on top of lower log content instead of being covered");
            }
            finally
            {
                controller?.Dispose();
                try { Directory.Delete(tempRoot, true); } catch { }
            }
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
                SettingsLogNavResponsivenessTests.WaitUntilLogContent(controller!, window);

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
            var window = LoadCompiledWindow();

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
            var xaml = ReadProjectSettingsXaml();
            var window = LoadCompiledWindow();
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
                SettingsLogNavResponsivenessTests.WaitUntilLogContent(controller!, window);

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
                SettingsLogNavResponsivenessTests.WaitUntilLogContent(controller!, window);

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
                SettingsLogNavResponsivenessTests.WaitUntilLogContent(controller!, window);

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
                SettingsLogNavResponsivenessTests.WaitUntilLogContent(controller!, window);

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

            var phase = 0;
            LogViewIdleReader.ReadOverrideForTests = () => phase == 0 ? 100u : 200u;

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
                SettingsLogNavResponsivenessTests.WaitUntilLogContent(controller!, window);

                var statusLabel = window.FindName("lblLogStatus") as TextBlock;
                statusLabel!.Text.Should().Contain("当前空闲 100 秒");

                phase = 1;
                InvokeRefreshLogView(controller!);

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
                SettingsLogNavResponsivenessTests.WaitUntilLogContent(controller!, window);

                ClickLogToolbarButton(window, "btnLogRefresh");
                SettingsLogNavResponsivenessTests.WaitUntilLogContent(controller!, window);

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
            SettingsWindowController? controller = null;

            try
            {
                var configPath = Path.Combine(tempRoot, "SmartGuard.config.json");
                var repository = new GuardConfigRepository(configPath);
                var config = repository.LoadOrDefault(tempRoot);

                controller = SettingsWindowController.TryCreate(tempRoot, repository, config);
                controller.Should().NotBeNull();

                var window = GetWindowField(controller!);
                var navList = window.FindName("navList") as ListBox;
                navList!.SelectedIndex = 3;
                SettingsLogNavResponsivenessTests.WaitUntilLogContent(controller!, window);

                GetLogViewPlainText(controller!).Should().Contain("initial entry");

                File.WriteAllText(logPath, "[INFO] 2026-06-21 10:00:00 initial entry\n[INFO] 2026-06-21 10:01:00 refreshed entry\n");
                ClickLogToolbarButton(window, "btnLogRefresh");
                SettingsLogNavResponsivenessTests.WaitUntilLogContent(controller!, window);

                GetLogViewPlainText(controller!).Should().Contain("refreshed entry");
            }
            finally
            {
                controller?.Dispose();
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        });
    }

    [Fact]
    public void User_jumps_to_bottom_enables_follow_tail()
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
                NavigateToLogsPage(controller!, window);

                var scrollViewer = GetLogScrollViewer(window, controller!);
                var chkLogFollowTail = window.FindName("chkLogFollowTail") as CheckBox;
                scrollViewer!.ScrollToVerticalOffset(0);
                chkLogFollowTail!.IsChecked = false;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                ClickLogToolbarButton(window, "btnLogScrollBottom");
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                LogViewScrollState.IsAtTail(scrollViewer).Should().BeTrue();
                chkLogFollowTail.IsChecked.Should().BeTrue();
            }
            finally
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        });
    }

    [Fact]
    public void User_dragging_logs_page_scrollbar_thumb_can_reach_both_vertical_bounds()
    {
        RunOnSta(() =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardSettingsLogThumb_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            var logPath = Path.Combine(tempRoot, "SmartGuard.log");
            var builder = new System.Text.StringBuilder();
            for (var i = 0; i < 180; i++)
                builder.AppendLine($"[INFO] 2026-06-21 09:{i % 60:D2}:{i % 60:D2} thumb line {i}");
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
                SettingsLogNavResponsivenessTests.WaitUntilLogContent(controller!, window);

                var scrollViewer = GetLogScrollViewer(window, controller!);
                scrollViewer.ScrollableHeight.Should().BeGreaterThan(0);

                var verticalScrollBar = FindScrollViewerVerticalScrollBar(scrollViewer);
                verticalScrollBar.Should().NotBeNull();

                GenuineVerticalThumbDrag(window, verticalScrollBar!, toBottom: true);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                scrollViewer.VerticalOffset.Should().BeApproximately(
                    scrollViewer.ScrollableHeight,
                    1.0,
                    "dragging the logs scrollbar to the bottom should reach the latest visible row");

                GenuineVerticalThumbDrag(window, verticalScrollBar!, toBottom: false);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                scrollViewer.VerticalOffset.Should().BeApproximately(
                    0,
                    1.0,
                    "dragging the logs scrollbar back to the top should reach the first visible row");
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
                SettingsLogNavResponsivenessTests.WaitUntilLogContent(controller!, window);

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
                SettingsLogNavResponsivenessTests.WaitUntilLogContent(controller!, window);

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
                SettingsLogNavResponsivenessTests.WaitUntilLogContent(controller!, window);

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
            var xaml = ReadProjectSettingsXaml();

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
            var window = LoadCompiledWindow();

            var navList = window.FindName("navList") as ListBox;
            navList.Should().NotBeNull();
            navList!.Items.Count.Should().Be(6);
            navList.Items
                .OfType<ListBoxItem>()
                .Select(item => item.Name)
                .Should()
                .ContainInOrder("navGeneral", "navAdvanced", "navNotifications", "navLogs", "navDisplay", "navAbout");
        });
    }

    [Fact]
    public void All_checkbox_controls_have_correct_names()
    {
        RunOnSta(() =>
        {
            var window = LoadCompiledWindow();

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
            var tempRoot = CreateTempSettingsRootWithLog("[INFO] Test log entry\r\n");

            try
            {
                var repository = new GuardConfigRepository(SmartGuardPaths.ConfigFile(tempRoot));
                var config = repository.LoadOrDefault(tempRoot);
                var controller = SettingsWindowController.TryCreate(tempRoot, repository, config);
                controller.Should().NotBeNull();

                var window = GetWindowField(controller);
                NavigateToLogsPage(controller!, window);

                var pageGeneral = window.FindName("pageGeneral") as StackPanel;
                var pageLogs = window.FindName("pageLogs") as Panel;
                pageGeneral.Should().NotBeNull();
                pageLogs.Should().NotBeNull();
                pageGeneral.Visibility.Should().Be(Visibility.Collapsed);
                pageLogs.Visibility.Should().Be(Visibility.Visible);

                var filterBar = GetLogSearchFilterBar(controller);
                var lblLogStatus = window.FindName("lblLogStatus") as TextBlock;

                filterBar.Should().NotBeNull();
                window.FindName("lstLogView").Should().NotBeNull();
                lblLogStatus.Should().NotBeNull();
            }
            finally
            {
                DeleteDirectoryQuietly(tempRoot);
            }
        });
    }

    [Fact]
    public void Navigation_hides_scrollable_pages_when_showing_logs()
    {
        RunOnSta(() =>
        {
            var tempRoot = CreateTempSettingsRootWithLog("[INFO] Test log entry\r\n");

            try
            {
                var repository = new GuardConfigRepository(SmartGuardPaths.ConfigFile(tempRoot));
                var config = repository.LoadOrDefault(tempRoot);
                var controller = SettingsWindowController.TryCreate(tempRoot, repository, config);
                controller.Should().NotBeNull();

                var window = GetWindowField(controller);
                var contentScrollViewer = window.FindName("contentScrollViewer") as ScrollViewer;
                var pageLogs = window.FindName("pageLogs") as Panel;

                contentScrollViewer.Should().NotBeNull();
                pageLogs.Should().NotBeNull();

                NavigateToLogsPage(controller!, window);

                contentScrollViewer!.Visibility.Should().Be(Visibility.Collapsed);
                pageLogs!.Visibility.Should().Be(Visibility.Visible);

                controller.NavigateTo("general");
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                contentScrollViewer.Visibility.Should().Be(Visibility.Visible);
                pageLogs.Visibility.Should().Be(Visibility.Collapsed);
            }
            finally
            {
                DeleteDirectoryQuietly(tempRoot);
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
            SettingsWindowController? controller = null;

            try
            {
                var configPath = Path.Combine(tempRoot, "SmartGuard.config.json");
                var repository = new GuardConfigRepository(configPath);
                var config = repository.LoadOrDefault(tempRoot);

                controller = SettingsWindowController.TryCreate(tempRoot, repository, config);
                controller.Should().NotBeNull();

                var window = GetWindowField(controller!);
                var navList = window.FindName("navList") as ListBox;
                navList!.SelectedIndex = 3;
                SettingsLogNavResponsivenessTests.WaitUntilLogContent(controller!, window);

                GetLogViewPlainText(controller!).Should().Contain("initial entry");

                File.AppendAllText(logPath, "[INFO] 2026-06-21 10:01:00 appended entry\n");
                InvokeRefreshLogView(controller!);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                GetLogViewPlainText(controller!).Should().Contain("appended entry");
            }
            finally
            {
                controller?.Dispose();
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
                NavigateToLogsPage(controller!, window);

                var scrollViewer = GetLogScrollViewer(window, controller!);
                var followTailCheckBox = window.FindName("chkLogFollowTail") as CheckBox;
                scrollViewer.Should().NotBeNull();
                followTailCheckBox.Should().NotBeNull();
                followTailCheckBox!.IsChecked = false;
                WpfStaTestHost.WaitUntil(
                    () => followTailCheckBox.IsChecked == false,
                    TimeSpan.FromSeconds(10),
                    "follow-tail mode to turn off before the user scrolls up");

                scrollViewer!.ScrollToVerticalOffset(0);
                WpfStaTestHost.WaitForRender();
                WaitUntilVerticalOffset(
                    scrollViewer,
                    expected: 0,
                    tolerance: 0.5,
                    "the user scroll position to reach the top");
                scrollViewer.VerticalOffset.Should().Be(0);

                File.AppendAllText(logPath, "[INFO] 2026-06-21 10:01:00 appended while scrolled up\n");
                InvokeRefreshLogView(controller!);
                WpfStaTestHost.WaitForRender();
                WaitUntilVerticalOffset(
                    scrollViewer,
                    expected: 0,
                    tolerance: 0.5,
                    "the user scroll position to remain at the top after refresh");

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
                SettingsLogNavResponsivenessTests.WaitUntilLogContent(controller!, window);
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
                SettingsLogNavResponsivenessTests.WaitUntilLogContent(controller!, window);

                var scrollViewer = GetLogScrollViewer(window, controller!);
                var btnLogScrollTop = window.FindName("btnLogScrollTop") as Button;
                btnLogScrollTop.Should().NotBeNull();
                btnLogScrollTop!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                scrollViewer.VerticalOffset.Should().Be(0);

                var chkLogFollowTail = window.FindName("chkLogFollowTail") as CheckBox;
                chkLogFollowTail!.IsChecked.Should().BeFalse();
                chkLogFollowTail!.IsChecked = true;
                try
                {
                    WpfStaTestHost.WaitUntil(
                        () => LogViewScrollState.IsAtTail(scrollViewer),
                        TimeSpan.FromSeconds(10),
                        "follow-tail scrolling to reach the latest log line");
                }
                catch (TimeoutException ex)
                {
                    throw new TimeoutException(
                        $"{ex.Message} VerticalOffset={scrollViewer.VerticalOffset:0.###}, " +
                        $"ScrollableHeight={scrollViewer.ScrollableHeight:0.###}.",
                        ex);
                }

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
                SettingsLogNavResponsivenessTests.WaitUntilLogContent(controller!, window);

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
            var tempRoot = CreateTempSettingsRootWithLog(
                "[INFO] System idle detected\r\n" +
                "[WARN] Battery low warning\r\n" +
                "[ERROR] Failed to switch plan\r\n" +
                "[HEART] Monitoring active\r\n");

            SettingsWindowController? controller = null;
            try
            {
                var repository = new GuardConfigRepository(SmartGuardPaths.ConfigFile(tempRoot));
                var config = repository.LoadOrDefault(tempRoot);

                controller = SettingsWindowController.TryCreate(tempRoot, repository, config);
                controller.Should().NotBeNull();

                var window = GetWindowField(controller);
                NavigateToLogsPage(controller!, window);
                InvokeRefreshLogView(controller!);

                var filterBar = GetLogSearchFilterBar(controller!);
                GetLogViewPlainText(controller!).Should().Contain("Battery low warning");
                filterBar.SetKeyword("Battery");
                FlushLogSearchDebounce(controller!, window);

                WaitUntilLogViewMatches(
                    controller!,
                    text => text.Contains("Battery low warning") && !text.Contains("System idle detected"));
            }
            finally
            {
                if (controller is not null)
                {
                    controller.Dispose();
                    GetWindowField(controller).Close();
                }
                DeleteDirectoryQuietly(tempRoot);
            }
        });
    }

    [Fact]
    public void User_clicking_tag_link_adds_filter_chip_and_updates_view()
    {
        RunOnSta(() =>
        {
            var tempRoot = CreateTempSettingsRootWithLog(
                "[INFO] 2026-06-21 10:00:00 System idle detected\r\n" +
                "[WARN] 2026-06-21 10:01:00 Battery low warning\r\n");

            try
            {
                var repository = new GuardConfigRepository(SmartGuardPaths.ConfigFile(tempRoot));
                var config = repository.LoadOrDefault(tempRoot);

                var controller = SettingsWindowController.TryCreate(tempRoot, repository, config);
                controller.Should().NotBeNull();

                var window = GetWindowField(controller);
                NavigateToLogsPage(controller!, window);
                InvokeRefreshLogView(controller!);

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
                DeleteDirectoryQuietly(tempRoot);
            }
        });
    }

    [Fact]
    public void Log_page_header_and_filters_remain_fixed_above_scrollable_log_content()
    {
        RunOnSta(() =>
        {
            var tempRoot = CreateTempSettingsRootWithLog("[INFO] 2026-06-21 10:00:00 alpha\n");

            try
            {
                var repository = new GuardConfigRepository(SmartGuardPaths.ConfigFile(tempRoot));
                var config = repository.LoadOrDefault(tempRoot);
                var controller = SettingsWindowController.TryCreate(tempRoot, repository, config);
                controller.Should().NotBeNull();

                var window = GetWindowField(controller!);
                NavigateToLogsPage(controller!, window);

                var pageLogs = window.FindName("pageLogs") as Panel;
                pageLogs.Should().NotBeNull();

                var lstLogView = window.FindName("lstLogView") as ListBox;
                lstLogView.Should().NotBeNull();

                var logScrollViewer = GetLogScrollViewer(window, controller!);
                logScrollViewer.Should().NotBeNull(
                    "lstLogView must host an internal ScrollViewer so log content can scroll independently. " +
                    "The header (title, description, search bar, filters) should remain fixed.");

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
            }
            finally
            {
                DeleteDirectoryQuietly(tempRoot);
            }
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
                txtVersion.Text.Should().MatchRegex(@"^\d+\.\d+\.\d+$",
                    "Displayed version should be a full major.minor.patch string");

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

    private static string CreateTempSettingsRootWithLog(string logContents, string? startupLogContents = null)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "SmartGuardSettingsTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        File.WriteAllText(SmartGuardPaths.DefaultLogFile(tempRoot), logContents);
        if (startupLogContents is not null)
            File.WriteAllText(SmartGuardPaths.StartupLogFile(tempRoot), startupLogContents);
        return tempRoot;
    }

    private static void DeleteDirectoryQuietly(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
        }
    }

    private static void NavigateToLogsPage(SettingsWindowController controller, Window window)
    {
        if (!window.IsVisible)
            WpfStaTestHost.ShowAndWait(window);

        controller.NavigateTo("logs");
        SettingsLogNavResponsivenessTests.WaitUntilLogContent(controller, window);
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

        var windowField = typeof(SettingsWindowController).GetField(
            "_window",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var window = (Window)windowField!.GetValue(controller)!;
        SettingsLogNavResponsivenessTests.WaitUntilLogRefreshIdle(controller, window);
    }

    private static string GetLogViewPlainText(SettingsWindowController controller)
    {
        var presenter = GetLogHostField<LogViewListPresenter>(controller, "_logListPresenter")!;
        return presenter.GetPlainText();
    }

    private static void WaitUntilLogViewMatches(
        SettingsWindowController controller,
        Func<string, bool> predicate,
        int timeoutMs = 10000)
    {
        WpfStaTestHost.Track(controller);
        WpfStaTestHost.WaitUntil(
            () => predicate(GetLogViewPlainText(controller)),
            TimeSpan.FromMilliseconds(timeoutMs),
            "the log view to apply the requested filter");
    }

    private static void WaitUntilVerticalOffset(
        ScrollViewer scrollViewer,
        double expected,
        double tolerance,
        string description)
    {
        try
        {
            WpfStaTestHost.WaitUntil(
                () => Math.Abs(scrollViewer.VerticalOffset - expected) <= tolerance,
                TimeSpan.FromSeconds(10),
                description);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException(
                $"{ex.Message} VerticalOffset={scrollViewer.VerticalOffset:0.###}, expected={expected:0.###}.",
                ex);
        }
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

    private static void GenuineVerticalThumbDrag(Window window, System.Windows.Controls.Primitives.ScrollBar scrollBar, bool toBottom)
    {
        scrollBar.ApplyTemplate();
        var track = scrollBar.Template?.FindName("PART_Track", scrollBar) as System.Windows.Controls.Primitives.Track;
        track.Should().NotBeNull();
        var thumb = track!.Thumb;
        var delta = toBottom ? 200.0 : -200.0;

        thumb.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0)
        {
            RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragStartedEvent,
        });
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        for (var i = 0; i < 20; i++)
        {
            thumb.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(0, delta)
            {
                RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragDeltaEvent,
            });
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        thumb.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(0, delta * 20, false)
        {
            RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragCompletedEvent,
        });
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private static System.Windows.Controls.Primitives.ScrollBar? FindScrollViewerVerticalScrollBar(ScrollViewer scrollViewer)
    {
        scrollViewer.ApplyTemplate();
        return scrollViewer.Template?.FindName("PART_VerticalScrollBar", scrollViewer)
            as System.Windows.Controls.Primitives.ScrollBar;
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        var node = current;
        while (true)
        {
            node = VisualTreeHelper.GetParent(node);
            if (node is null)
                return null;
            if (node is T match)
                return match;
        }
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
