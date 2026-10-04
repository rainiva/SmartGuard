using System.Text;

namespace SmartGuard.Configuration.Tests;

public sealed class DiagnosticLogTailReaderTests : IDisposable
{
  private readonly string _root = Path.Combine(Path.GetTempPath(), "SmartGuardDiagnosticTail_" + Guid.NewGuid().ToString("N"));
  public DiagnosticLogTailReaderTests() => Directory.CreateDirectory(_root);

  [Fact]
  public void Hundred_megabyte_input_reads_at_most_one_shared_260KiB_window()
  {
    var path = Path.Combine(_root, "startup.log");
    using (var stream = new FileStream(path, FileMode.Create))
    { stream.SetLength(100 * 1024 * 1024); stream.Seek(-32, SeekOrigin.End); stream.Write(Encoding.UTF8.GetBytes("\n中文😀-complete\npartial")); }
    var result = DiagnosticLogTailReader.ReadTail(path, 256 * 1024, CancellationToken.None);
    result.ReadBytes.Should().BeLessThanOrEqualTo(260 * 1024);
    result.IncludedBytes.Should().BeLessThanOrEqualTo(256 * 1024);
    result.Text.Should().Contain("中文😀-complete").And.NotContain("partial").And.NotContain("�");
    result.Truncated.Should().BeTrue(); result.SourceBytes.Should().Be(100 * 1024 * 1024);
  }

  [Fact]
  public void Short_active_file_fills_from_newest_three_archives_under_one_budget()
  {
    var path = Path.Combine(_root, "startup.log"); File.WriteAllText(path, "active\n", new UTF8Encoding(false));
    for (var i = 1; i <= 4; i++) File.WriteAllText(path + $".2026100{i}.001.bak", $"archive-{i}\n", new UTF8Encoding(false));
    var result = DiagnosticLogTailReader.ReadTail(path, 256 * 1024, CancellationToken.None);
    result.Text.Should().Be("archive-2\narchive-3\narchive-4\nactive\n");
    result.ReadBytes.Should().Be(37); result.IncludedBytes.Should().Be(37);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void Overlong_or_unterminated_single_line_is_omitted(bool terminated)
  {
    var path = Path.Combine(_root, "startup.log");
    File.WriteAllText(path, new string('界', 100000) + (terminated ? "\n" : ""), new UTF8Encoding(false));
    var result = DiagnosticLogTailReader.ReadTail(path, 256 * 1024, CancellationToken.None);
    result.IncludedBytes.Should().Be(0); result.Status.Should().Be(DiagnosticReadStatus.TooLarge);
    result.ReadBytes.Should().BeLessThanOrEqualTo(260 * 1024);
  }

  public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
