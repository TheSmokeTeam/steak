using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Steak.Core.Contracts;

namespace Steak.Core.Services;

internal sealed class KafkaTopicBrowserService(
    IConnectionSessionService sessionService,
    IKafkaConfigurationService configurationService,
    ILogger<KafkaTopicBrowserService> logger) : ITopicBrowserService
{
    public Task<IReadOnlyList<KafkaTopicSummary>> ListTopicsAsync(string connectionSessionId, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Listing Kafka topics for session {SessionId}", connectionSessionId);

        var settings = sessionService.GetActiveSettings(connectionSessionId);
        var config = configurationService.BuildConfig(settings, KafkaClientKind.Admin);

        // Use short timeouts to avoid hanging on unreachable brokers.
        if (!config.ContainsKey("socket.timeout.ms"))
            config["socket.timeout.ms"] = "5000";
        if (!config.ContainsKey("request.timeout.ms"))
            config["request.timeout.ms"] = "5000";

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "Kafka admin topic-list config for session {SessionId}: {KafkaConfig}",
                connectionSessionId,
                KafkaDiagnostics.FormatConfig(config));
        }

        try
        {
            using var adminClient = new AdminClientBuilder(config)
                .SetErrorHandler((_, error) =>
                {
                    if (error.IsFatal)
                    {
                        logger.LogCritical(
                            "Fatal Kafka admin error while listing topics for session {SessionId}: {Reason}",
                            connectionSessionId,
                            error.Reason);
                    }
                    else
                    {
                        logger.LogWarning(
                            "Kafka admin warning while listing topics for session {SessionId}: {Reason}",
                            connectionSessionId,
                            error.Reason);
                    }
                })
                .Build();

            var metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(5));
            var brokers = metadata.Brokers.ToDictionary(broker => broker.BrokerId, broker => $"{broker.Host}:{broker.Port}");

            logger.LogDebug(
                "Kafka metadata lookup for session {SessionId} returned {BrokerCount} broker(s) and {TopicCount} topic(s)",
                connectionSessionId,
                metadata.Brokers.Count,
                metadata.Topics.Count);

            IReadOnlyList<KafkaTopicSummary> result = metadata.Topics
                .OrderBy(topic => topic.Topic)
                .Select(topic => ToSummary(topic, brokers))
                .ToArray();

            return Task.FromResult(result);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Kafka topic listing failed for session {SessionId}", connectionSessionId);
            throw;
        }
    }

    public Task<KafkaTopicSummary?> GetTopicAsync(string connectionSessionId, string topic, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Fetching Kafka topic metadata for {Topic} in session {SessionId}", topic, connectionSessionId);

        var settings = sessionService.GetActiveSettings(connectionSessionId);
        var config = configurationService.BuildConfig(settings, KafkaClientKind.Admin);

        if (!config.ContainsKey("socket.timeout.ms"))
            config["socket.timeout.ms"] = "5000";
        if (!config.ContainsKey("request.timeout.ms"))
            config["request.timeout.ms"] = "5000";

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "Kafka admin topic-detail config for session {SessionId}: {KafkaConfig}",
                connectionSessionId,
                KafkaDiagnostics.FormatConfig(config));
        }

        try
        {
            using var adminClient = new AdminClientBuilder(config)
                .SetErrorHandler((_, error) =>
                {
                    if (error.IsFatal)
                    {
                        logger.LogCritical(
                            "Fatal Kafka admin error while fetching topic {Topic} for session {SessionId}: {Reason}",
                            topic,
                            connectionSessionId,
                            error.Reason);
                    }
                    else
                    {
                        logger.LogWarning(
                            "Kafka admin warning while fetching topic {Topic} for session {SessionId}: {Reason}",
                            topic,
                            connectionSessionId,
                            error.Reason);
                    }
                })
                .Build();

            var metadata = adminClient.GetMetadata(topic, TimeSpan.FromSeconds(5));
            var brokers = metadata.Brokers.ToDictionary(broker => broker.BrokerId, broker => $"{broker.Host}:{broker.Port}");

            logger.LogDebug(
                "Kafka topic metadata lookup for {Topic} returned {BrokerCount} broker(s) and {TopicCount} topic record(s)",
                topic,
                metadata.Brokers.Count,
                metadata.Topics.Count);

            var details = metadata.Topics.FirstOrDefault(candidate => string.Equals(candidate.Topic, topic, StringComparison.Ordinal));
            return Task.FromResult(details is null ? null : ToSummary(details, brokers));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Kafka topic metadata fetch failed for {Topic} in session {SessionId}", topic, connectionSessionId);
            throw;
        }
    }

    private static KafkaTopicSummary ToSummary(TopicMetadata metadata, IReadOnlyDictionary<int, string> brokers)
    {
        return new KafkaTopicSummary
        {
            Name = metadata.Topic,
            IsInternal = false,
            PartitionCount = metadata.Partitions.Count,
            Partitions = metadata.Partitions
                .OrderBy(partition => partition.PartitionId)
                .Select(partition => new TopicPartitionSummary
                {
                    PartitionId = partition.PartitionId,
                    Leader = brokers.TryGetValue(partition.Leader, out var leader) ? leader : partition.Leader.ToString(),
                    Replicas = partition.Replicas.Select(replica => brokers.TryGetValue(replica, out var broker) ? broker : replica.ToString()).ToList(),
                    InSyncReplicas = partition.InSyncReplicas.Select(replica => brokers.TryGetValue(replica, out var broker) ? broker : replica.ToString()).ToList()
                })
                .ToList()
        };
    }
}
