using Serilog.Events;
using Steak.Host.Configuration;

namespace Steak.Tests.Host;

public sealed class SteakCommandLineTests
{
    [Theory]
    [InlineData(new[] { "--log-level", "debug" }, LogEventLevel.Debug)]
    [InlineData(new[] { "--log-level=trace" }, LogEventLevel.Verbose)]
    [InlineData(new[] { "--log-level", "fatal" }, LogEventLevel.Fatal)]
    [InlineData(new[] { "--log-level", "INFO" }, LogEventLevel.Information)]
    public void ParseCommandLineLogLevel_ParsesSupportedValues(string[] args, LogEventLevel expected)
    {
        var level = SteakCommandLine.ParseCommandLineLogLevel(args);

        Assert.Equal(expected, level);
    }

    [Fact]
    public void ParseCommandLineLogLevel_ThrowsForUnsupportedValue()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            SteakCommandLine.ParseCommandLineLogLevel(["--log-level", "loud"]));

        Assert.Contains("Unsupported value 'loud' for --log-level", exception.Message);
    }

    [Fact]
    public void ParsePort_ReadsFlagValue()
    {
        var port = SteakCommandLine.ParsePort(["--port", "5050"]);

        Assert.Equal(5050, port);
    }

    [Theory]
    [InlineData(new[] { "--open-browser", "false" }, false)]
    [InlineData(new[] { "--open-browser=true" }, true)]
    [InlineData(new[] { "--open-browser", "0" }, false)]
    public void ParseOpenBrowser_ParsesSupportedValues(string[] args, bool expected)
    {
        var openBrowser = SteakCommandLine.ParseOpenBrowser(args);

        Assert.Equal(expected, openBrowser);
    }
}
