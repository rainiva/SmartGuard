namespace SmartGuard.Settings.Tests;

[Collection("LogViewDisplay")]
public class LogViewTagPaletteConcurrencyTests
{
    [Fact]
    public void Concurrent_theme_toggle_and_brush_lookup_does_not_throw()
    {
        var errors = new List<Exception>();
        Parallel.For(0, 200, i =>
        {
            try
            {
                LogViewTagPalette.ConfigureForDarkMode(i % 2 == 0);
                _ = LogViewTagPalette.GetTagBrush("INFO").Color;
                _ = LogViewTagPalette.GetBodyBrush().Color;
                _ = LogLineDisplayItem.Parse("[INFO] 2026-06-21 10:00:00 concurrent line").LineText;
            }
            catch (Exception ex)
            {
                lock (errors)
                {
                    errors.Add(ex);
                }
            }
        });

        errors.Should().BeEmpty();
    }
}
