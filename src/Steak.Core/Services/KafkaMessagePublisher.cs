using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Steak.Core.Contracts;

namespace Steak.Core.Services;

internal sealed class KafkaMessagePublisher(
    IConnectionSessionService sessionService,
    IKafkaConfigurationService configurationService,
    IMessageEnvelopeFactory envelopeFactory,
    ILogger<KafkaMessagePublisher> logger) : IMessagePublisher
{
    public async Task<PublishResultInfo> PublishAsync(PublishEnvelopeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sessionId = request.ConnectionSessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("A connectionSessionId is required to publish a message.");
        }

        var settings = sessionService.GetActiveSettings(sessionId);
        var normalized = envelopeFactory.NormalizeForPublish(request.Envelope, sessionId, request.Topic);

        if (string.IsNullOrWhiteSpace(normalized.Topic))
        {
            throw new InvalidOperationException("A target topic is required to publish a message.");
        }

        if (string.IsNullOrWhiteSpace(normalized.ValueBase64))
        {
            throw new InvalidOperationException("valueBase64 is required to publish a message.");
        }

        var config = configurationService.BuildConfig(settings, KafkaClientKind.Producer);

        using var producer = new ProducerBuilder<byte[], byte[]>(config)
            .SetKeySerializer(Serializers.ByteArray)
            .SetValueSerializer(Serializers.ByteArray)
            .Build();

        logger.LogInformation("Publishing Kafka message to topic {Topic} via session {SessionId}", normalized.Topic, sessionId);

        var message = new Message<byte[], byte[]>
        {
            Key = string.IsNullOrWhiteSpace(normalized.KeyBase64) ? null! : Convert.FromBase64String(normalized.KeyBase64),
            Value = Convert.FromBase64String(normalized.ValueBase64),
            Headers = KafkaMessageHelpers.BuildHeaders(normalized.Headers),
            Timestamp = normalized.TimestampUtc.HasValue
                ? new Timestamp(normalized.TimestampUtc.Value.UtcDateTime)
                : Timestamp.Default
        };

        var result = await producer.ProduceAsync(normalized.Topic, message, cancellationToken).ConfigureAwait(false);
        return new PublishResultInfo
        {
            Topic = result.Topic,
            Partition = result.Partition.Value,
            Offset = result.Offset.Value,
            TimestampUtc = result.Timestamp.UtcDateTime == DateTime.MinValue
                ? null
                : new DateTimeOffset(result.Timestamp.UtcDateTime, TimeSpan.Zero),
            Status = result.Status.ToString()
        };
    }
}
