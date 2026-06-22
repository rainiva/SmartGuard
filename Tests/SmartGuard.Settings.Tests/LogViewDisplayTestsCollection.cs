namespace SmartGuard.Settings.Tests;

public sealed class LogViewDisplayTestsFixture : IDisposable
{
    public LogViewDisplayTestsFixture()
        => LogViewTagPalette.ConfigureForDarkMode(false);

    public void Dispose()
        => LogViewTagPalette.ConfigureForDarkMode(false);
}

[CollectionDefinition("LogViewDisplay", DisableParallelization = true)]
public class LogViewDisplayTestsCollection : ICollectionFixture<LogViewDisplayTestsFixture>;
