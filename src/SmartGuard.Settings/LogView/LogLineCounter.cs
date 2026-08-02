using System.IO;
using System.Text;

namespace SmartGuard.Settings.LogView;

public static class LogLineCounter
{
    public static int CountLines(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return 0;

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            var count = 0;
            while (reader.ReadLine() != null)
                count++;

            return count;
        }
        catch
        {
            return 0;
        }
    }
}
