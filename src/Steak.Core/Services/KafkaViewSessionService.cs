using System.Threading.Channels;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Steak.Core.Contracts;

namespace Steak.Core.Services;

internal sealed class KafkaViewSessionService(
    IConnectionSessionService sessionService,
    IKafkaConfigurationService configurationService,
    ConsumedMessageEnvelopeFactory envelopeFactory,
    ILogger<KafkaViewSessionService> logger) : IViewSessionService
{
    private readonly object _sync = new();
    private readonly List<Channel<SteakMessageEnvelope>> _subscribers = [];
    private CancellationTokenSource? _runnerCancellation;
    private Task? _runner;
    private ViewSessionStatus _snapshot = new();

    public event EventHandler? StateChanged;

    public ViewSessionStatus Snapshot
    {
        get
        {
            lock (_sync)
            {
                return Clone(_snapshot);
            }
        }
    }

    public async Task<ViewSessionStatus> StartAsync(StartViewSessionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ConnectionSessionId))
        {
            throw new InvalidOperationException("connectionSessionId is required to start a view session.");
        }

        if (string.IsNullOrWhiteSpace(request.Topic))
        {
            throw new InvalidOperationException("topic is required to start a view session.");
        }

        logger.LogDebug(
            "Starting Kafka view session request. SessionId {SessionId}, topic {Topic}, partition {Partition}, offset mode {OffsetMode}, group {GroupId}, max messages {MaxMessages}",
            request.ConnectionSessionId,
            request.Topic,
            request.Partition,
            request.OffsetMode,
            request.GroupId,
            request.MaxMessages);

        await StopAsync().ConfigureAwait(false);

        var settings = sessionService.GetActiveSettings(request.ConnectionSessionId);
        var config = configurationService.BuildConfig(settings, KafkaClientKind.Consumer);

        config["group.id"] = KafkaMessageHelpers.BuildViewGroupId(request);
        config["auto.offset.reset"] = request.OffsetMode.ToAutoOffsetReset().ToString().ToLowerInvariant();
        config["enable.auto.commit"] = "false";
        config["enable.partition.eof"] = "false";

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "Kafka view session config for session {SessionId}: {KafkaConfig}",
                request.ConnectionSessionId,
                KafkaDiagnostics.FormatConfig(config));
        }

        var runnerCancellation = new CancellationTokenSource();
        lock (_sync)
        {
            _runnerCancellation = runnerCancellation;
            _snapshot = new ViewSessionStatus
            {
                IsRunning = true,
                ConnectionSessionId = request.ConnectionSessionId,
                Topic = request.Topic,
                Partition = request.Partition,
                OffsetMode = request.OffsetMode,
                StartedAtUtc = DateTimeOffset.UtcNow,
                RecentMessages = []
            };
        }

        NotifyStateChanged();
        _runner = Task.Run(() => RunAsync(request, config, runnerCancellation.Token), CancellationToken.None);
        return Snapshot;
    }

    public async Task StopAsync()
    {
        Task? runner;
        CancellationTokenSource? cancellation;

        lock (_sync)
        {
            runner = _runner;
            cancellation = _runnerCancellation;
            _runner = null;
            _runnerCancellation = null;
        }

        if (runner is not null)
        {
            logger.LogDebug("Stopping active Kafka view session");
        }

        cancellation?.Cancel();
        if (runner is not null)
        {
            try
            {
                await runner.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                logger.LogDebug("Kafka view session stop observed operation cancellation");
            }
        }

        cancellation?.Dispose();
    }

    public async IAsyncEnumerable<SteakMessageEnvelope> StreamAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<SteakMessageEnvelope>();
        var snapshot = Snapshot;

        logger.LogDebug(
            "Registering Kafka view stream subscriber. Buffered messages available: {BufferedCount}",
            snapshot.RecentMessages.Count);

        // New subscribers immediately receive the buffered snapshot, then live fan-out from the running session.
        foreach (var message in snapshot.RecentMessages)
        {
            channel.Writer.TryWrite(message);
        }

        lock (_sync)
        {
            _subscribers.Add(channel);
        }

        using var registration = cancellationToken.Register(() =>
        {
            channel.Writer.TryComplete();
            lock (_sync)
            {
                _subscribers.Remove(channel);
            }
        });

        await foreach (var message in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return message;
        }
    }

    private void PublishMessage(StartViewSessionRequest request, SteakMessageEnvelope envelope)
    {
        List<Channel<SteakMessageEnvelope>> subscribers;

        lock (_sync)
        {
            _snapshot.ReceivedCount += 1;
            _snapshot.LastError = null;
            _snapshot.RecentMessages.Add(envelope);
            while (_snapshot.RecentMessages.Count > request.MaxMessages)
            {
                _snapshot.RecentMessages.RemoveAt(0);
            }

            subscribers = [.. _subscribers];
        }

        logger.LogDebug(
            "Kafka view session published message to subscribers. Topic {Topic}, partition {Partition}, offset {Offset}, subscribers {SubscriberCount}",
            envelope.Topic,
            envelope.Partition,
            envelope.Offset,
            subscribers.Count);

        foreach (var subscriber in subscribers)
        {
            subscriber.Writer.TryWrite(envelope);
        }

        NotifyStateChanged();
    }

    private async Task RunAsync(StartViewSessionRequest request, IReadOnlyDictionary<string, string> config, CancellationToken cancellationToken)
    {
        try
        {
            using var consumer = new ConsumerBuilder<byte[], byte[]>(config)
                .SetKeyDeserializer(Deserializers.ByteArray)
                .SetValueDeserializer(Deserializers.ByteArray)
                .SetErrorHandler((_, error) =>
                {
                    if (error.IsFatal)
                    {
                        logger.LogCritical(
                            "Fatal Kafka consumer error in view session {SessionId} for topic {Topic}: {Reason}",
                            request.ConnectionSessionId,
                            request.Topic,
                            error.Reason);
                    }
                    else
                    {
                        logger.LogWarning(
                            "Kafka consumer warning in view session {SessionId} for topic {Topic}: {Reason}",
                            request.ConnectionSessionId,
                            request.Topic,
                            error.Reason);
                    }

                    UpdateError($"Kafka consumer error: {error.Reason}");
                })
                .SetPartitionsAssignedHandler((_, partitions) =>
                {
                    logger.LogDebug(
                        "Kafka view session assigned partitions for topic {Topic}: {Partitions}",
                        request.Topic,
                        string.Join(", ", partitions.Select(partition => partition.ToString())));
                })
                .SetPartitionsRevokedHandler((_, partitions) =>
                {
                    logger.LogDebug(
                        "Kafka view session revoked partitions for topic {Topic}: {Partitions}",
                        request.Topic,
                        string.Join(", ", partitions.Select(partition => partition.ToString())));
                })
                .Build();

            if (request.Partition.HasValue)
            {
                var assignment = new TopicPartitionOffset(request.Topic, request.Partition.Value, request.OffsetMode.ToOffset());
                logger.LogDebug("Assigning Kafka view session to {Assignment}", assignment);
                consumer.Assign(assignment);
            }
            else
            {
                logger.LogDebug("Subscribing Kafka view session to topic {Topic}", request.Topic);
                consumer.Subscribe(request.Topic);
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                var result = consumer.Consume(cancellationToken);
                if (result?.Message is null)
                {
                    logger.LogDebug("Kafka view session consume cycle returned no message for topic {Topic}", request.Topic);
                    continue;
                }

                logger.LogDebug(
                    "Kafka view session consumed message. Topic {Topic}, partition {Partition}, offset {Offset}, key bytes {KeyBytes}, value bytes {ValueBytes}, headers {HeaderCount}",
                    result.Topic,
                    result.Partition.Value,
                    result.Offset.Value,
                    result.Message.Key?.Length ?? 0,
                    result.Message.Value?.Length ?? 0,
                    result.Message.Headers?.Count ?? 0);

                PublishMessage(request, envelopeFactory.Create(request.ConnectionSessionId, result));
                await Task.Yield();
            }

            logger.LogDebug("Closing Kafka view session consumer for topic {Topic}", request.Topic);
            consumer.Close();
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("Kafka view session consume loop cancelled for topic {Topic}", request.Topic);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Kafka view session failed for topic {Topic}", request.Topic);
            UpdateError(SteakErrorDetails.Format(exception));
        }
        finally
        {
            CompleteSession();
        }
    }

    private void CompleteSession()
    {
        List<Channel<SteakMessageEnvelope>> subscribers;
        ViewSessionStatus snapshot;

        lock (_sync)
        {
            _snapshot.IsRunning = false;
            snapshot = Clone(_snapshot);
            subscribers = [.. _subscribers];
            _subscribers.Clear();
        }

        logger.LogDebug(
            "Kafka view session completed for topic {Topic}. Received {ReceivedCount} messages. Last error: {LastError}",
            snapshot.Topic,
            snapshot.ReceivedCount,
            snapshot.LastError);

        foreach (var subscriber in subscribers)
        {
            subscriber.Writer.TryComplete();
        }

        NotifyStateChanged();
    }

    private void UpdateError(string error)
    {
        lock (_sync)
        {
            _snapshot.LastError = error;
        }

        logger.LogError("Kafka view session state updated with error: {Error}", error);
        NotifyStateChanged();
    }

    private void NotifyStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static ViewSessionStatus Clone(ViewSessionStatus source)
    {
        return new ViewSessionStatus
        {
            IsRunning = source.IsRunning,
            ConnectionSessionId = source.ConnectionSessionId,
            Topic = source.Topic,
            Partition = source.Partition,
            OffsetMode = source.OffsetMode,
            StartedAtUtc = source.StartedAtUtc,
            ReceivedCount = source.ReceivedCount,
            LastError = source.LastError,
            RecentMessages = [.. source.RecentMessages]
        };
    }
}
