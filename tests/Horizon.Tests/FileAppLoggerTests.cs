using Horizon.Infrastructure.Persistence;
using Xunit;

namespace Horizon.Tests;

public sealed class FileAppLoggerTests
{
    [Fact]
    public void ErrorLogIncludesOperationPageStackAndInnerException()
    {
        var directory = Path.Combine(Path.GetTempPath(), "horizon-logger-tests", Guid.NewGuid().ToString("N"));
        var logger = new FileAppLogger(directory);
        var exception = new InvalidOperationException("outer failure", new ArgumentException("inner failure"));

        logger.Error("auth.provider.google", exception, "SignInViewModel");

        var path = Assert.Single(Directory.GetFiles(directory, "horizon-*.log"));
        var content = File.ReadAllText(path);
        Assert.Contains("operation=auth.provider.google", content);
        Assert.Contains("page=SignInViewModel", content);
        Assert.Contains(typeof(InvalidOperationException).FullName!, content);
        Assert.Contains(typeof(ArgumentException).FullName!, content);
        Assert.Contains("stack=", content);
        Assert.Contains("inner failure", content);
    }
}
