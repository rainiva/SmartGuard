namespace SmartGuard.Tray;

internal static class TrayContextMenuFactory
{
  internal sealed record TrayContextMenuParts(
    ContextMenuStrip Menu,
    ToolStripMenuItem StatusItem,
    ToolStripMenuItem PauseItem);

  internal static TrayContextMenuParts Create(
    EventHandler pauseClick,
    EventHandler switchHighPerformanceClick,
    EventHandler openLogClick,
    EventHandler openSettingsClick,
    EventHandler exitClick)
  {
    var menu = new ContextMenuStrip { ShowImageMargin = false, AutoSize = true };
    menu.Font = SystemInformation.MenuFont;

    var statusItem = new ToolStripMenuItem("加载中…") { Enabled = false, AutoSize = true };
    menu.Items.Add(statusItem);
    menu.Items.Add(new ToolStripSeparator());

    var pauseItem = new ToolStripMenuItem("暂停守护") { AutoSize = true };
    pauseItem.Click += pauseClick;
    menu.Items.Add(pauseItem);

    var highPerfItem = new ToolStripMenuItem(TrayContextMenuTexts.SwitchHighPerformance) { AutoSize = true };
    highPerfItem.Click += switchHighPerformanceClick;
    menu.Items.Add(highPerfItem);

    var logItem = new ToolStripMenuItem("打开日志") { AutoSize = true };
    logItem.Click += openLogClick;
    menu.Items.Add(logItem);

    var settingsItem = new ToolStripMenuItem("设置…") { AutoSize = true };
    settingsItem.Click += openSettingsClick;
    menu.Items.Add(settingsItem);

    menu.Items.Add(new ToolStripSeparator());
    var exitItem = new ToolStripMenuItem("退出") { AutoSize = true };
    exitItem.Click += exitClick;
    menu.Items.Add(exitItem);

    pauseItem.Text = TrayPauseState.MenuText(null);
    TrayContextMenuPrewarmer.WarmUp(menu);

    return new TrayContextMenuParts(menu, statusItem, pauseItem);
  }
}
