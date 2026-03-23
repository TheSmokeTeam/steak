using Steak.Host.Configuration;

namespace Steak.Tests.Host;

public sealed class UiErrorMessageTests
{
    [Fact]
    public void Build_PrefixesOperationAndExceptionDetail()
    {
        var message = UiErrorMessage.Build(
            "Connect to cluster",
            new InvalidOperationException("Broker transport failure"));

        Assert.Equal(
            "Connect to cluster failed. InvalidOperationException: Broker transport failure",
            message);
    }
}
