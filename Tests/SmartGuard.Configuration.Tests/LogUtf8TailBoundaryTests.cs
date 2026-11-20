using System.Text;

namespace SmartGuard.Configuration.Tests;

public sealed class LogUtf8TailBoundaryTests
{
  [Fact]
  public void Backward_window_discards_cut_first_line_and_never_emits_it_twice()
  {
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".log");
    try
    {
      File.WriteAllText(path, "head-" + new string('界', 100) + "\ncomplete\n", new UTF8Encoding(false));
      var source = new LogSourceDescriptor(path, LogSourceIdentity.FromFilePath(path), LogSourceKind.Current);
      var lines = LogUtf8LineScanner.ReadLinesBackward(source, new FileInfo(path).Length, 32, CancellationToken.None).ToArray();
      lines.Select(line => line.Text).Should().Equal("complete");
    }
    finally { File.Delete(path); }
  }
}
