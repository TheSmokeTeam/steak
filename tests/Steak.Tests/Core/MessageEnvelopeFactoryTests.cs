using Steak.Core.Contracts;
using Steak.Core.Services;

namespace Steak.Tests.Core;

public sealed class MessageEnvelopeFactoryTests
{
    [Fact]
    public void NormalizeForPublish_AppliesDefaultsAndRegeneratesPreview()
    {
        var factory = new MessageEnvelopeFactory(new MessagePreviewService());
        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("""{"ok":true}"""));

        var normalized = factory.NormalizeForPublish(
            new SteakMessageEnvelope
            {
                App = "  ",
                ConnectionSessionId = "  ",
                Topic = "  ",
                KeyBase64 = $"  {Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("key-1"))}  ",
                ValueBase64 = $"  {payload}  ",
                Headers =
                [
                    new SteakMessageHeader { Key = "  trace-id  ", ValueBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("abc")) },
                    new SteakMessageHeader { Key = "   " }
                ]
            },
            "session-1",
            "orders");

        Assert.Equal("Steak", normalized.App);
        Assert.Equal("session-1", normalized.ConnectionSessionId);
        Assert.Equal("orders", normalized.Topic);
        Assert.Single(normalized.Headers);
        Assert.Equal("trace-id", normalized.Headers[0].Key);
        Assert.NotNull(normalized.Preview);
        Assert.True(normalized.Preview.ValueIsJson);
        Assert.True(normalized.Preview.KeyIsUtf8);
    }

    [Fact]
    public void NormalizeForPublish_UsesExplicitTopicOverride()
    {
        var factory = new MessageEnvelopeFactory(new MessagePreviewService());
        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("""{"ok":true}"""));

        var normalized = factory.NormalizeForPublish(
            new SteakMessageEnvelope
            {
                Topic = "orders",
                ValueBase64 = payload
            },
            "session-1",
            "payments");

        Assert.Equal("payments", normalized.Topic);
    }
}
