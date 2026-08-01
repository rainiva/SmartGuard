using SmartGuard.Configuration;

namespace SmartGuard.Configuration.Tests;

public class LogFailureInjectorTests
{
  [Fact]
  public void Initially_no_failures_injected()
  {
    var injector = new LogFailureInjector();
    injector.IsDiskFullInjected.Should().BeFalse();
    injector.IsPathDeniedInjected.Should().BeFalse();
    injector.IsFileLockedInjected.Should().BeFalse();
    injector.IsConsumerExceptionInjected.Should().BeFalse();
  }

  [Fact]
  public void InjectDiskFull_sets_flag()
  {
    var injector = new LogFailureInjector();
    injector.InjectDiskFull();
    injector.IsDiskFullInjected.Should().BeTrue();
  }

  [Fact]
  public void InjectPathPermissionDenied_sets_flag()
  {
    var injector = new LogFailureInjector();
    injector.InjectPathPermissionDenied();
    injector.IsPathDeniedInjected.Should().BeTrue();
  }

  [Fact]
  public void InjectFileLocked_sets_flag()
  {
    var injector = new LogFailureInjector();
    injector.InjectFileLocked();
    injector.IsFileLockedInjected.Should().BeTrue();
  }

  [Fact]
  public void InjectConsumerException_sets_flag()
  {
    var injector = new LogFailureInjector();
    injector.InjectConsumerException();
    injector.IsConsumerExceptionInjected.Should().BeTrue();
  }

  [Fact]
  public void Reset_clears_all_flags()
  {
    var injector = new LogFailureInjector();
    injector.InjectDiskFull();
    injector.InjectPathPermissionDenied();
    injector.InjectFileLocked();
    injector.InjectConsumerException();
    injector.Reset();
    injector.IsDiskFullInjected.Should().BeFalse();
    injector.IsPathDeniedInjected.Should().BeFalse();
    injector.IsFileLockedInjected.Should().BeFalse();
    injector.IsConsumerExceptionInjected.Should().BeFalse();
  }
}
