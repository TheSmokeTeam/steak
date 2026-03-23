using Steak.Core.Contracts;

namespace Steak.Tests.Core;

public sealed class SteakErrorDetailsTests
{
    [Fact]
    public void Format_IncludesExceptionTypeChain()
    {
        var exception = new InvalidOperationException(
            "Top level failure",
            new ArgumentException("Inner failure"));

        var detail = SteakErrorDetails.Format(exception);

        Assert.Equal("InvalidOperationException: Top level failure --> ArgumentException: Inner failure", detail);
    }

    [Fact]
    public void Format_WithPrefix_PrependsOperationContext()
    {
        var exception = new InvalidOperationException("Boom");

        var detail = SteakErrorDetails.Format("Connect failed.", exception);

        Assert.Equal("Connect failed. InvalidOperationException: Boom", detail);
    }
}
