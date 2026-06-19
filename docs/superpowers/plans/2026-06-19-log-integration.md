# 设置界面日志集成 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 SmartGuard 设置界面中新增"日志"页面，支持实时日志查看、关键词检索和多标签过滤。

**Architecture:** 复用现有 LogViewer 核心类（LogTailReader/LogLineDisplayFormatter/LogLineTagParser/LogViewerTagPalette）读取和格式化日志，在 WPF 设置界面中用 TextBox + 内联 Run 着色实现轻量日志显示。新增 LogViewController 管理刷新定时器和过滤状态。

**Tech Stack:** WPF, C# 12, .NET 8, xUnit, FluentAssertions

---

## File Map

| File | Action | Responsibility |
|------|--------|----------------|
| `lib/SmartGuard.Settings.xaml` | Modify | 新增 `pageLogs` 布局（搜索栏、标签筛选、日志显示区） |
| `src/SmartGuard.Settings/LogViewController.cs` | Create | 日志读取、格式化、过滤、刷新定时器 |
| `src/SmartGuard.Settings/SettingsWindowController.cs` | Modify | 新增日志页面导航和控制器初始化 |
| `Tests/SmartGuard.Settings.Tests/LogViewControllerTests.cs` | Create | LogViewController 单元测试 |
| `Tests/SmartGuard.Settings.Tests/SettingsWindowControllerTests.cs` | Modify | 新增日志页面 UI 测试 |

---

## Task 1: 创建 LogViewController 核心类

**Files:**
- Create: `src/SmartGuard.Settings/LogViewController.cs`
- Test: `Tests/SmartGuard.Settings.Tests/LogViewControllerTests.cs`

**目标：** 实现日志读取、格式化、关键词过滤、标签筛选。

- [ ] **Step 1: 写失败测试 - LogViewController 能读取并格式化日志**

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SmartGuard.Settings.Tests;

public class LogViewControllerTests
{
    [Fact]
    public void LogViewController_reads_and_formats_log_lines()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "[INFO] 2026-06-19 10:00:00 System started\n[WARN] 2026-06-19 10:01:00 Low battery\n");
            
            var controller = new LogViewController(tempFile, null);
            var lines = controller.GetFilteredLines();
            
            lines.Count.Should().Be(2);
            lines[0].Should().Contain("INFO");
            lines[1].Should().Contain("WARN");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

```bash
cd d:\Project\SmartGuard
dotnet test tests\SmartGuard.Settings.Tests\SmartGuard.Settings.Tests.csproj --filter "FullyQualifiedName~LogViewController_reads" --verbosity normal
```

Expected: FAIL - `LogViewController` 类型未定义

- [ ] **Step 3: 实现 LogViewController 最小代码**

```csharp
using System.IO;
using System.Windows.Media;
using SmartGuard.LogViewer;

namespace SmartGuard.Settings;

public sealed class LogViewController
{
    private readonly string _logPath;
    private readonly string? _fallbackLogPath;
    private long _fileLength = -1;
    private string _currentText = string.Empty;

    public string SearchKeyword { get; set; } = string.Empty;
    public bool ShowInfo { get; set; } = true;
    public bool ShowWarn { get; set; } = true;
    public bool ShowError { get; set; } = true;
    public bool ShowHeart { get; set; } = true;

    public LogViewController(string logPath, string? fallbackLogPath)
    {
        _logPath = logPath;
        _fallbackLogPath = fallbackLogPath;
    }

    public IReadOnlyList<string> GetFilteredLines()
    {
        RefreshIfNeeded();
        return ApplyFilters(_currentText);
    }

    public void RefreshIfNeeded()
    {
        var snapshot = LogTailReader.ReadFromOffset(_logPath, 0);
        if (snapshot.Length <= 0 && string.IsNullOrEmpty(snapshot.Text))
        {
            _currentText = string.Empty;
            _fileLength = 0;
            return;
        }

        if (_fileLength < 0 || snapshot.Length < _fileLength)
        {
            var initial = LogTailReader.ReadInitialView(_logPath, _fallbackLogPath);
            _currentText = LogLineDisplayFormatter.FormatText(initial.Text);
            _fileLength = snapshot.Length > 0 ? snapshot.Length : initial.FileLength;
        }
        else if (snapshot.Length > _fileLength)
        {
            var delta = LogTailReader.ReadFromOffset(_logPath, _fileLength);
            var formattedDelta = LogLineDisplayFormatter.FormatText(
                LogViewerDelta.PrepareForAppend(_currentText, delta.Text));
            _currentText += formattedDelta;
            _fileLength = snapshot.Length;
        }
    }

    public void ForceRefresh()
    {
        _fileLength = -1;
        RefreshIfNeeded();
    }

    private IReadOnlyList<string> ApplyFilters(string text)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<string>();

        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>();
        var keyword = SearchKeyword.Trim();

        foreach (var line in lines)
        {
            if (!string.IsNullOrEmpty(keyword) && !line.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                continue;

            if (LogLineTagParser.TryParse(line, out var tag, out _))
            {
                if (!IsTagEnabled(tag)) continue;
            }

            result.Add(line);
        }

        return result;
    }

    private bool IsTagEnabled(string tag)
    {
        return tag switch
        {
            "INFO" => ShowInfo,
            "WARN" => ShowWarn,
            "ERROR" => ShowError,
            "HEART" => ShowHeart,
            _ => true,
        };
    }

    public static Brush GetTagBrush(string tag)
    {
        return LogViewerTagPalette.GetTagColor(tag) switch
        {
            var c when c == LogViewerTagPalette.InfoColor => new SolidColorBrush(Color.FromRgb(0, 128, 0)),
            var c when c == LogViewerTagPalette.WarnColor => new SolidColorBrush(Color.FromRgb(196, 122, 0)),
            var c when c == LogViewerTagPalette.ErrorColor => new SolidColorBrush(Color.FromRgb(198, 40, 40)),
            var c when c == LogViewerTagPalette.HeartColor => new SolidColorBrush(Color.FromRgb(95, 107, 122)),
            _ => new SolidColorBrush(Color.FromRgb(26, 26, 26)),
        };
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

```bash
cd d:\Project\SmartGuard
dotnet test tests\SmartGuard.Settings.Tests\SmartGuard.Settings.Tests.csproj --filter "FullyQualifiedName~LogViewController_reads" --verbosity normal
```

Expected: PASS

- [ ] **Step 5: 写失败测试 - 关键词过滤**

```csharp
[Fact]
public void LogViewController_filters_by_keyword()
{
    var tempFile = Path.GetTempFileName();
    try
    {
        File.WriteAllText(tempFile, "[INFO] 2026-06-19 10:00:00 System started\n[WARN] 2026-06-19 10:01:00 Low battery\n");
        
        var controller = new LogViewController(tempFile, null);
        controller.SearchKeyword = "battery";
        var lines = controller.GetFilteredLines();
        
        lines.Count.Should().Be(1);
        lines[0].Should().Contain("battery");
    }
    finally
    {
        File.Delete(tempFile);
    }
}
```

- [ ] **Step 6: 运行测试确认通过**

```bash
cd d:\Project\SmartGuard
dotnet test tests\SmartGuard.Settings.Tests\SmartGuard.Settings.Tests.csproj --filter "FullyQualifiedName~filters_by_keyword" --verbosity normal
```

Expected: PASS

- [ ] **Step 7: 写失败测试 - 标签筛选**

```csharp
[Fact]
public void LogViewController_filters_by_tag()
{
    var tempFile = Path.GetTempFileName();
    try
    {
        File.WriteAllText(tempFile, "[INFO] 2026-06-19 10:00:00 System started\n[WARN] 2026-06-19 10:01:00 Low battery\n[ERROR] 2026-06-19 10:02:00 Failed\n");
        
        var controller = new LogViewController(tempFile, null);
        controller.ShowInfo = false;
        controller.ShowWarn = false;
        var lines = controller.GetFilteredLines();
        
        lines.Count.Should().Be(1);
        lines[0].Should().Contain("ERROR");
    }
    finally
    {
        File.Delete(tempFile);
    }
}
```

- [ ] **Step 8: 运行测试确认通过**

```bash
cd d:\Project\SmartGuard
dotnet test tests\SmartGuard.Settings.Tests\SmartGuard.Settings.Tests.csproj --filter "FullyQualifiedName~filters_by_tag" --verbosity normal
```

Expected: PASS

- [ ] **Step 9: Commit**

```bash
cd d:\Project\SmartGuard
git add src/SmartGuard.Settings/LogViewController.cs tests/SmartGuard.Settings.Tests/LogViewControllerTests.cs
git commit -m "feat: add LogViewController with read, format, keyword filter, tag filter"
```

---

## Task 2: 在 XAML 中添加日志页面布局

**Files:**
- Modify: `lib/SmartGuard.Settings.xaml`

**目标：** 在左侧导航新增"日志"项，右侧添加日志页面 UI。

- [ ] **Step 1: 修改导航列表，新增日志项**

在 `lib/SmartGuard.Settings.xaml` 中，找到 `navList` ListBox，在 `navNotifications` 之后添加：

```xml
<ListBoxItem x:Name="navLogs">
  <StackPanel Orientation="Horizontal">
    <TextBlock Text="&#xE9C8;" FontFamily="Segoe Fluent Icons, Segoe MDL2 Assets" FontSize="16" Margin="0,0,12,0" VerticalAlignment="Center"/>
    <TextBlock Text="日志" VerticalAlignment="Center"/>
  </StackPanel>
</ListBoxItem>
```

- [ ] **Step 2: 在 ScrollViewer 的 StackPanel 中新增日志页面**

在 `pageNotifications` 之后添加：

```xml
<!-- Logs Page -->
<StackPanel x:Name="pageLogs" Visibility="Collapsed">
  <TextBlock Text="日志" Style="{StaticResource SectionTitle}"/>

  <Border Style="{StaticResource SettingsCard}">
    <StackPanel>
      <TextBlock Text="实时日志" FontSize="14" FontWeight="SemiBold" Foreground="{DynamicResource TextPrimary}" Margin="0,0,0,4"/>
      <TextBlock Text="查看 SmartGuard 运行日志，支持搜索和筛选" FontSize="12" Foreground="{DynamicResource TextSecondary}" Margin="0,0,0,12"/>

      <!-- Search Bar -->
      <Border Background="{DynamicResource NumberBoxBackground}" BorderBrush="{DynamicResource NumberBoxBorderBrush}" BorderThickness="1" CornerRadius="4" Padding="8,6" Margin="0,0,0,12">
        <Grid>
          <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="*"/>
          </Grid.ColumnDefinitions>
          <TextBlock Text="&#xE721;" FontFamily="Segoe Fluent Icons, Segoe MDL2 Assets" FontSize="14" Foreground="{DynamicResource TextTertiary}" Margin="0,0,8,0" VerticalAlignment="Center"/>
          <TextBox x:Name="txtLogSearch" Grid.Column="1" BorderThickness="0" Background="Transparent" Padding="0" FontSize="13" Foreground="{DynamicResource TextPrimary}" Text="" PlaceholderText="搜索日志..."/>
        </Grid>
      </Border>

      <!-- Tag Filters -->
      <StackPanel Orientation="Horizontal" Margin="0,0,0,12">
        <CheckBox x:Name="chkInfo" Content="INFO" IsChecked="True" Foreground="{DynamicResource TextPrimary}" Margin="0,0,16,0"/>
        <CheckBox x:Name="chkWarn" Content="WARN" IsChecked="True" Foreground="{DynamicResource TextPrimary}" Margin="0,0,16,0"/>
        <CheckBox x:Name="chkError" Content="ERROR" IsChecked="True" Foreground="{DynamicResource TextPrimary}" Margin="0,0,16,0"/>
        <CheckBox x:Name="chkHeart" Content="HEART" IsChecked="True" Foreground="{DynamicResource TextPrimary}"/>
      </StackPanel>

      <!-- Log Display -->
      <Border Background="{DynamicResource NumberBoxBackground}" BorderBrush="{DynamicResource NumberBoxBorderBrush}" BorderThickness="1" CornerRadius="4" Padding="12">
        <ScrollViewer VerticalScrollBarVisibility="Auto" MaxHeight="400">
          <TextBox x:Name="txtLogView" IsReadOnly="True" BorderThickness="0" Background="Transparent" FontFamily="Consolas, Microsoft YaHei Mono" FontSize="12" TextWrapping="Wrap" Foreground="{DynamicResource TextPrimary}" Text=""/>
        </ScrollViewer>
      </Border>

      <!-- Status Bar -->
      <TextBlock x:Name="lblLogStatus" Text="暂无日志" FontSize="11" Foreground="{DynamicResource TextTertiary}" Margin="0,8,0,0"/>
    </StackPanel>
  </Border>
</StackPanel>
```

- [ ] **Step 3: Commit**

```bash
cd d:\Project\SmartGuard
git add lib/SmartGuard.Settings.xaml
git commit -m "feat: add logs page layout to Settings XAML"
```

---

## Task 3: 在 SettingsWindowController 中集成日志控制器

**Files:**
- Modify: `src/SmartGuard.Settings/SettingsWindowController.cs`

**目标：** 初始化 LogViewController，绑定搜索和筛选事件，设置定时刷新。

- [ ] **Step 1: 修改 SettingsWindowController 构造函数和字段**

在 `SettingsWindowController.cs` 中：

1. 新增字段：
```csharp
private readonly LogViewController? _logController;
private readonly System.Windows.Threading.DispatcherTimer? _logTimer;
```

2. 在 `TryCreate` 方法中，找到 `SetupNavigation` 调用之后，添加日志初始化：

```csharp
// Log view initialization
var logPath = Path.Combine(root, "SmartGuard.log");
var fallbackLogPath = Path.Combine(root, "SmartGuard.startup.log");
if (File.Exists(logPath) || File.Exists(fallbackLogPath))
{
    var logController = new LogViewController(logPath, fallbackLogPath);
    var txtLogSearch = Require<TextBox>(window, "txtLogSearch");
    var chkInfo = Require<CheckBox>(window, "chkInfo");
    var chkWarn = Require<CheckBox>(window, "chkWarn");
    var chkError = Require<CheckBox>(window, "chkError");
    var chkHeart = Require<CheckBox>(window, "chkHeart");
    var txtLogView = Require<TextBox>(window, "txtLogView");
    var lblLogStatus = Require<TextBlock>(window, "lblLogStatus");

    void RefreshLogView()
    {
        logController.SearchKeyword = txtLogSearch.Text;
        logController.ShowInfo = chkInfo.IsChecked == true;
        logController.ShowWarn = chkWarn.IsChecked == true;
        logController.ShowError = chkError.IsChecked == true;
        logController.ShowHeart = chkHeart.IsChecked == true;

        var lines = logController.GetFilteredLines();
        txtLogView.Text = string.Join(Environment.NewLine, lines);
        lblLogStatus.Text = $"{lines.Count} 行 | 刷新: {DateTime.Now:HH:mm:ss}";
    }

    txtLogSearch.TextChanged += (_, _) => RefreshLogView();
    chkInfo.Checked += (_, _) => RefreshLogView();
    chkInfo.Unchecked += (_, _) => RefreshLogView();
    chkWarn.Checked += (_, _) => RefreshLogView();
    chkWarn.Unchecked += (_, _) => RefreshLogView();
    chkError.Checked += (_, _) => RefreshLogView();
    chkError.Unchecked += (_, _) => RefreshLogView();
    chkHeart.Checked += (_, _) => RefreshLogView();
    chkHeart.Unchecked += (_, _) => RefreshLogView();

    var logTimer = new System.Windows.Threading.DispatcherTimer
    {
        Interval = TimeSpan.FromSeconds(2),
    };
    logTimer.Tick += (_, _) => RefreshLogView();
    logTimer.Start();

    controller._logController = logController;
    controller._logTimer = logTimer;

    // Initial refresh
    RefreshLogView();
}
```

3. 在 `SetupNavigation` 方法中，新增日志页面导航：

```csharp
var pageLogs = window.FindName("pageLogs") as StackPanel;
// ... existing navList.SelectionChanged handler
navList.SelectionChanged += (_, e) =>
{
    if (pageGeneral != null) pageGeneral.Visibility = Visibility.Collapsed;
    if (pageAdvanced != null) pageAdvanced.Visibility = Visibility.Collapsed;
    if (pageNotifications != null) pageNotifications.Visibility = Visibility.Collapsed;
    if (pageLogs != null) pageLogs.Visibility = Visibility.Collapsed;

    var selected = navList.SelectedIndex;
    switch (selected)
    {
        case 0:
            if (pageGeneral != null) pageGeneral.Visibility = Visibility.Visible;
            break;
        case 1:
            if (pageAdvanced != null) pageAdvanced.Visibility = Visibility.Visible;
            break;
        case 2:
            if (pageNotifications != null) pageNotifications.Visibility = Visibility.Visible;
            break;
        case 3:
            if (pageLogs != null) pageLogs.Visibility = Visibility.Visible;
            break;
    }
};
```

4. 在 `Dispose` 或清理逻辑中（如果没有，在 `ShowDialog` 之后），停止定时器：

```csharp
public void StopLogTimer()
{
    _logTimer?.Stop();
}
```

- [ ] **Step 2: Commit**

```bash
cd d:\Project\SmartGuard
git add src/SmartGuard.Settings/SettingsWindowController.cs
git commit -m "feat: integrate LogViewController into SettingsWindowController"
```

---

## Task 4: 写 UI 测试验证日志页面

**Files:**
- Modify: `Tests/SmartGuard.Settings.Tests/SettingsWindowControllerTests.cs`

**目标：** 验证日志页面控件存在，且用户操作流程正常。

- [ ] **Step 1: 写失败测试 - 日志页面控件存在**

```csharp
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
```

- [ ] **Step 2: 运行测试确认通过**

```bash
cd d:\Project\SmartGuard
dotnet test tests\SmartGuard.Settings.Tests\SmartGuard.Settings.Tests.csproj --filter "FullyQualifiedName~Log_page_controls" --verbosity normal
```

Expected: PASS

- [ ] **Step 3: 写失败测试 - 导航到日志页面**

```csharp
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
        navList.Items.Count.Should().Be(4); // General, Advanced, Notifications, Logs
    });
}
```

- [ ] **Step 4: 运行测试确认通过**

```bash
cd d:\Project\SmartGuard
dotnet test tests\SmartGuard.Settings.Tests\SmartGuard.Settings.Tests.csproj --filter "FullyQualifiedName~Navigation_includes_logs" --verbosity normal
```

Expected: PASS

- [ ] **Step 5: Commit**

```bash
cd d:\Project\SmartGuard
git add tests/SmartGuard.Settings.Tests/SettingsWindowControllerTests.cs
git commit -m "test: add log page UI existence and navigation tests"
```

---

## Task 5: 运行全部测试并构建

- [ ] **Step 1: 运行全部测试**

```bash
cd d:\Project\SmartGuard
dotnet test --verbosity normal
```

Expected: 全部测试通过

- [ ] **Step 2: 构建 Release**

```bash
cd d:\Project\SmartGuard
dotnet publish "src\SmartGuard.Settings\SmartGuard.Settings.csproj" -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -o "bin"
```

- [ ] **Step 3: 构建安装包**

```bash
cd d:\Project\SmartGuard\installer
powershell -NoProfile -ExecutionPolicy Bypass -File Build-Staging.ps1 -Configuration Release -Root "d:\Project\SmartGuard" -StagingDir "d:\Project\SmartGuard\installer\staging" -SkipRedistDownload
powershell -NoProfile -ExecutionPolicy Bypass -File Build-Installer.ps1 -Configuration Release -Root "d:\Project\SmartGuard" -StagingDir "d:\Project\SmartGuard\installer\staging" -SkipStaging
```

- [ ] **Step 4: Commit**

```bash
cd d:\Project\SmartGuard
git add -A
git commit -m "feat: integrate log viewer into settings page with search and tag filtering"
```

---

## Spec Coverage Check

| 需求 | 任务 |
|------|------|
| 新增"日志"导航页 | Task 2 (XAML), Task 3 (导航逻辑) |
| 关键词检索过滤 | Task 1 (LogViewController.SearchKeyword), Task 3 (TextChanged事件) |
| 多标签(INFO/WARN/ERROR/HEART)筛选 | Task 1 (LogViewController.Show*), Task 3 (CheckBox事件) |
| 实时刷新日志 | Task 3 (DispatcherTimer 2秒) |
| 复用现有LogViewer核心类 | Task 1 (LogTailReader/LogLineDisplayFormatter/LogLineTagParser) |
| 保持WinUI3风格一致 | Task 2 (XAML使用现有Style资源) |
| TDD开发流程 | 所有Task遵循RED-GREEN-COMMIT |

## Placeholder Scan

- 无 TBD/TODO
- 所有代码块包含完整实现
- 所有测试包含具体断言
- 文件路径准确

## Type Consistency

- `LogViewController` 字段和属性命名一致
- `SettingsWindowController` 中 `_logController` 和 `_logTimer` 命名一致
- XAML 控件 `x:Name` 与 C# `FindName` 调用一致
