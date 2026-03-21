using Confluent.Kafka;
using Steak.Core.Contracts;

namespace Steak.Core.Services;

internal sealed class ConsumedMessageEnvelopeFactory(IMessagePreviewService previewService)
{
    public SteakMessageEnvelope Create(string connectionSessionId, ConsumeResult<byte[], byte[]> result)
    {
        var keyBase64 = result.Message.Key is null || result.Message.Key.Length == 0
            ? null
            : Convert.ToBase64String(result.Message.Key);
        var valueBase64 = result.Message.Value is null || result.Message.Value.Length == 0
            ? string.Empty
            : Convert.ToBase64String(result.Message.Value);

        var envelope = new SteakMessageEnvelope
        {
            App = "Steak",
            CapturedAtUtc = DateTimeOffset.UtcNow,
            ConnectionSessionId = connectionSessionId,
            Topic = result.Topic,
            Partition = result.Partition.Value,
            Offset = result.Offset.Value,
            TimestampUtc = result.Message.Timestamp.UtcDateTime == DateTime.MinValue
                ? null
                : new DateTimeOffset(result.Message.Timestamp.UtcDateTime, TimeSpan.Zero),
            TimestampType = result.Message.Timestamp.Type.ToString(),
            KeyBase64 = keyBase64,
            ValueBase64 = valueBase64,
            Headers = result.Message.Headers.Select(header => previewService.CreateHeaderPreview(header.Key, header.GetValueBytes())).ToList()
        };

        envelope.Preview = previewService.CreatePreview(keyBase64, valueBase64);
        return envelope;
    }
}
