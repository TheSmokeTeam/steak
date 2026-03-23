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

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "Publishing Kafka message with session {SessionId}, config {KafkaConfig}, envelope {EnvelopeSummary}",
                sessionId,
                KafkaDiagnostics.FormatConfig(config),
                KafkaDiagnostics.FormatEnvelopeSummary(normalized));
        }

        try
        {
            using var producer = new ProducerBuilder<byte[], byte[]>(config)
                .SetKeySerializer(Serializers.ByteArray)
                .SetValueSerializer(Serializers.ByteArray)
                .SetErrorHandler((_, error) =>
                {
                    if (error.IsFatal)
                    {
                        logger.LogCritical(
                            "Fatal Kafka producer error for topic {Topic} via session {SessionId}: {Reason}",
                            normalized.Topic,
                            sessionId,
                            error.Reason);
                    }
                    else
                    {
                        logger.LogWarning(
                            "Kafka producer warning for topic {Topic} via session {SessionId}: {Reason}",
                            normalized.Topic,
                            sessionId,
                            error.Reason);
                    }
                })
                .Build();

            var message = new Message<byte[], byte[]>
            {
                Key = string.IsNullOrWhiteSpace(normalized.KeyBase64) ? null! : Convert.FromBase64String(normalized.KeyBase64),
                Value = Convert.FromBase64String(normalized.ValueBase64),
                Headers = KafkaMessageHelpers.BuildHeaders(normalized.Headers),
                Timestamp = normalized.TimestampUtc.HasValue
                    ? new Timestamp(normalized.TimestampUtc.Value.UtcDateTime)
                    : Timestamp.Default
            };

            logger.LogDebug(
                "Sending Kafka message to topic {Topic} via session {SessionId}. Key bytes: {KeyBytes}. Value bytes: {ValueBytes}. Headers: {HeaderCount}",
                normalized.Topic,
                sessionId,
                message.Key?.Length ?? 0,
                message.Value.Length,
                message.Headers?.Count ?? 0);

            var result = await producer.ProduceAsync(normalized.Topic, message, cancellationToken).ConfigureAwait(false);

            logger.LogDebug(
                "Kafka publish succeeded for topic {Topic} via session {SessionId}. Partition {Partition}, offset {Offset}, status {Status}",
                result.Topic,
                sessionId,
                result.Partition.Value,
                result.Offset.Value,
                result.Status);

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
        catch (Exception exception)
        {
            logger.LogError(exception, "Kafka publish failed for topic {Topic} via session {SessionId}", normalized.Topic, sessionId);
            throw;
        }
    }
}
