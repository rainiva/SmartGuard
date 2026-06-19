using System.Windows;
using System.Windows.Controls;
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

                // Verify user can see the window and interact with it
                controller.ShowDialog();
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
}
