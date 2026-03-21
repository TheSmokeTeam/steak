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
        var settings = sessionService.GetActiveSettings(connectionSessionId);
        var config = configurationService.BuildConfig(settings, KafkaClientKind.Admin);
        using var adminClient = new AdminClientBuilder(config).Build();

        logger.LogInformation("Listing Kafka topics for session {SessionId}", connectionSessionId);
        var metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(10));
        var brokers = metadata.Brokers.ToDictionary(broker => broker.BrokerId, broker => $"{broker.Host}:{broker.Port}");

        IReadOnlyList<KafkaTopicSummary> result = metadata.Topics
            .OrderBy(topic => topic.Topic)
            .Select(topic => ToSummary(topic, brokers))
            .ToArray();

        return Task.FromResult(result);
    }

    public Task<KafkaTopicSummary?> GetTopicAsync(string connectionSessionId, string topic, CancellationToken cancellationToken = default)
    {
        var settings = sessionService.GetActiveSettings(connectionSessionId);
        var config = configurationService.BuildConfig(settings, KafkaClientKind.Admin);
        using var adminClient = new AdminClientBuilder(config).Build();

        logger.LogInformation("Fetching Kafka topic metadata for {Topic} in session {SessionId}", topic, connectionSessionId);
        var metadata = adminClient.GetMetadata(topic, TimeSpan.FromSeconds(10));
        var brokers = metadata.Brokers.ToDictionary(broker => broker.BrokerId, broker => $"{broker.Host}:{broker.Port}");

        var details = metadata.Topics.FirstOrDefault(candidate => string.Equals(candidate.Topic, topic, StringComparison.Ordinal));
        return Task.FromResult(details is null ? null : ToSummary(details, brokers));
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
