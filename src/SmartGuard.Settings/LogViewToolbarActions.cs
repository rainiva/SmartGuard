using System.Diagnostics;
using System.IO;
using System.Text;

namespace SmartGuard.Settings;

public static class LogViewToolbarActions
{
    public static string BuildVisibleText(LogViewSnapshot snapshot)
    {
        return string.Join(Environment.NewLine, snapshot.DisplayLines);
    }

    public static string ResolveLogFilePath(string logPath, string? fallbackLogPath)
    {
        if (File.Exists(logPath))
            return logPath;

        if (!string.IsNullOrWhiteSpace(fallbackLogPath) && File.Exists(fallbackLogPath))
            return fallbackLogPath;

        return logPath;
    }

    public static void ExportVisibleText(string text, string destinationPath)
    {
        File.WriteAllText(destinationPath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static ProcessStartInfo CreateRevealLogFileProcessStartInfo(string logFilePath)
    {
        return new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{logFilePath}\"",
            UseShellExecute = true,
        };
    }
}
