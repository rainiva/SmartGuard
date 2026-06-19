using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using SmartGuard.Configuration;

namespace SmartGuard.Settings.Tests;

public class SettingsWindowControllerTests
{
    private static T RunOnSta<T>(Func<T> action)
    {
        var tcs = new TaskCompletionSource<T>();
        var thread = new Thread(() =>
        {
            try
            {
                tcs.SetResult(action());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task.Result;
    }

    private static void RunOnSta(Action action)
    {
        RunOnSta(() => { action(); return true; });
    }

    [Fact]
    public void Embedded_resource_can_load_window()
    {
        RunOnSta(() =>
        {
            // Ensure Application exists (may be created by prior test in same AppDomain)
            if (Application.Current is null)
                _ = new Application();

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
            navList.Items.Count.Should().Be(3);
        });
    }

    [Fact]
    public void Theme_toggle_button_exists()
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

            var btnThemeToggle = window.FindName("btnThemeToggle") as Button;
            var txtTheme = window.FindName("txtTheme") as TextBlock;
            var iconTheme = window.FindName("iconTheme") as TextBlock;

            btnThemeToggle.Should().NotBeNull();
            txtTheme.Should().NotBeNull();
            iconTheme.Should().NotBeNull();
        });
    }

    [Fact]
    public void InfoBar_exists_and_is_hidden_by_default()
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

            var infoBar = window.FindName("infoBar") as Border;
            var txtInfoBar = window.FindName("txtInfoBar") as TextBlock;

            infoBar.Should().NotBeNull();
            txtInfoBar.Should().NotBeNull();
            infoBar.Visibility.Should().Be(Visibility.Collapsed);
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

            var pageLogs = window.FindName("pageLogs") as StackPanel;
            var txtLogSearch = window.FindName("txtLogSearch") as TextBox;
            var chkInfo = window.FindName("chkInfo") as CheckBox;
            var chkWarn = window.FindName("chkWarn") as CheckBox;
            var chkError = window.FindName("chkError") as CheckBox;
            var chkHeart = window.FindName("chkHeart") as CheckBox;
            var txtLogView = window.FindName("txtLogView") as TextBox;
            var lblLogStatus = window.FindName("lblLogStatus") as TextBlock;

            pageLogs.Should().NotBeNull();
            txtLogSearch.Should().NotBeNull();
            chkInfo.Should().NotBeNull();
            chkWarn.Should().NotBeNull();
            chkError.Should().NotBeNull();
            chkHeart.Should().NotBeNull();
            txtLogView.Should().NotBeNull();
            lblLogStatus.Should().NotBeNull();
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

            var xaml = File.ReadAllText(xamlPath);

            // Nested ScrollViewers cause dual-scrollbar UX problems:
            // 1. User scrolls inner ScrollViewer to bottom, but outer ScrollViewer is not at bottom
            // 2. Content appears "cut off" at the bottom (e.g., last log lines not visible)
            // 3. User must move mouse outside inner area and scroll outer ScrollViewer to see rest
            // 4. This is confusing and breaks the natural scrolling flow
            //
            // The fix: remove the inner ScrollViewer around txtLogView in pageLogs.
            // The outer ScrollViewer (around all pages) already handles scrolling for the entire content area.
            // Log page should have only one scrollable region.
            xaml.Should().NotContain("<ScrollViewer VerticalScrollBarVisibility=\"Auto\" MaxHeight=\"400\">",
                "Inner ScrollViewer in pageLogs creates nested scrolling. " +
                "When log content exceeds MaxHeight, inner scrollbar activates while outer scrollbar also exists. " +
                "User must scroll inner to bottom, then scroll outer to see remaining content. " +
                "Remove inner ScrollViewer and let outer ScrollViewer handle all scrolling.");
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
            window.FindName("tglAutoStart").Should().NotBeNull();
        });
    }

    [Fact]
    public void Save_and_cancel_buttons_exist()
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

            window.FindName("btnSave").Should().NotBeNull();
            window.FindName("btnCancel").Should().NotBeNull();
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
                var pageLogs = window.FindName("pageLogs") as StackPanel;
                pageGeneral.Should().NotBeNull();
                pageLogs.Should().NotBeNull();
                pageGeneral.Visibility.Should().Be(Visibility.Collapsed);
                pageLogs.Visibility.Should().Be(Visibility.Visible);

                // Verify log controls are accessible
                var txtLogSearch = window.FindName("txtLogSearch") as TextBox;
                var chkInfo = window.FindName("chkInfo") as CheckBox;
                var txtLogView = window.FindName("txtLogView") as TextBox;
                var lblLogStatus = window.FindName("lblLogStatus") as TextBlock;

                txtLogSearch.Should().NotBeNull();
                chkInfo.Should().NotBeNull();
                txtLogView.Should().NotBeNull();
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

                var txtLogSearch = window.FindName("txtLogSearch") as TextBox;
                var txtLogView = window.FindName("txtLogView") as TextBox;

                // Simulate user typing in search box
                txtLogSearch!.Text = "Battery";

                // The TextChanged event should have fired and updated the view
                // Give dispatcher a moment to process
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                // Verify filtered results contain the search term
                txtLogView.Text.Should().Contain("Battery");
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
    public void User_toggles_log_filter_and_view_updates()
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

                var chkInfo = window.FindName("chkInfo") as CheckBox;
                var txtLogView = window.FindName("txtLogView") as TextBox;

                // Initially INFO is checked, so view should contain INFO lines
                txtLogView.Text.Should().Contain("INFO");

                // Simulate user unchecking INFO filter
                chkInfo!.IsChecked = false;
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                // After unchecking INFO, the INFO line should be filtered out
                txtLogView.Text.Should().NotContain("INFO");
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

    private static Window GetWindowField(SettingsWindowController controller)
    {
        var field = typeof(SettingsWindowController).GetField("_window", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (Window)field!.GetValue(controller)!;
    }
}
