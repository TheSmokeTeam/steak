using Steak.Core.Contracts;
using Steak.Core.Services;

namespace Steak.Tests.Core;

public sealed class FileNameFactoryTests
{
    [Fact]
    public void CreateMessageFileName_SanitizesTopicAndStaysUnique()
    {
        var factory = new FileNameFactory();
        var envelope = new SteakMessageEnvelope
        {
            Topic = "orders/live v1",
            Partition = 7,
            Offset = 128,
            CapturedAtUtc = new DateTimeOffset(2026, 03, 21, 11, 0, 0, TimeSpan.Zero),
            ValueBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("hello"))
        };

        var first = factory.CreateMessageFileName(envelope);
        var second = factory.CreateMessageFileName(envelope);

        Assert.StartsWith("Steak_orders-live-v1_p7_o128_", first);
        Assert.EndsWith(".json", first);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void CreateMessageFileName_FallsBackWhenTopicBecomesEmpty()
    {
        var factory = new FileNameFactory();
        var envelope = new SteakMessageEnvelope
        {
            Topic = "////",
            Partition = 0,
            Offset = 1,
            CapturedAtUtc = new DateTimeOffset(2026, 03, 21, 11, 0, 0, TimeSpan.Zero),
            ValueBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("hello"))
        };

        var fileName = factory.CreateMessageFileName(envelope);

        Assert.StartsWith("Steak_unknown-topic_p0_o1_", fileName);
    }
}
