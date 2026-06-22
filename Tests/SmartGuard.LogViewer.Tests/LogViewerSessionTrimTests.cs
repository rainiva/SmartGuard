using System.Windows.Forms;

namespace SmartGuard.LogViewer.Tests;

[Collection("LogViewerWinForms")]
public class LogViewerSessionTrimTests
{
    [Fact]
    public void RefreshView_trims_rich_text_after_many_appends()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var path = WriteTemp("soak.log", "[INFO] seed line\n");
                using var form = new Form();
                var logView = new RichTextBox { Dock = DockStyle.Fill };
                form.Controls.Add(logView);
                var status = new ToolStripStatusLabel();
                var session = new LogViewerSession
                {
                    LogPath = path,
                    LogView = logView,
                    StatusLabel = status,
                };

                session.RefreshView();

                for (var batch = 0; batch < 6; batch++)
                {
                    var chunk = string.Join(
                        '\n',
                        Enumerable.Range(0, 400).Select(i =>
                            $"[INFO] 2026-06-22 12:00:{i % 60:D2} batch {batch} line {i} {new string('y', 350)}"));
                    File.AppendAllText(path, chunk + "\n");
                    session.RefreshView();
                }

                logView.TextLength.Should().BeLessThanOrEqualTo(LogViewerTextTrimmer.DefaultMaxCachedBytes);
                logView.Text.Should().Contain("batch 5 line 399");
                logView.Text.Should().NotContain("batch 0 line 0");
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error.Should().BeNull();
    }

    private static string WriteTemp(string name, string content)
    {
        var path = Path.Combine(Path.GetTempPath(), "SmartGuard.Tests", Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }
}
