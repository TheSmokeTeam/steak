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

        await StopAsync().ConfigureAwait(false);

        var settings = sessionService.GetActiveSettings(request.ConnectionSessionId);
        var config = configurationService.BuildConfig(settings, KafkaClientKind.Consumer);

        config["group.id"] = KafkaMessageHelpers.BuildViewGroupId(request);
        config["auto.offset.reset"] = request.OffsetMode.ToAutoOffsetReset().ToString().ToLowerInvariant();
        config["enable.auto.commit"] = "false";
        config["enable.partition.eof"] = "false";

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

        cancellation?.Cancel();
        if (runner is not null)
        {
            try
            {
                await runner.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        cancellation?.Dispose();
    }

    public async IAsyncEnumerable<SteakMessageEnvelope> StreamAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<SteakMessageEnvelope>();
        var snapshot = Snapshot;

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
                    if (!error.IsFatal)
                    {
                        return;
                    }

                    UpdateError(error.Reason);
                })
                .Build();

            if (request.Partition.HasValue)
            {
                consumer.Assign(new TopicPartitionOffset(request.Topic, request.Partition.Value, request.OffsetMode.ToOffset()));
            }
            else
            {
                consumer.Subscribe(request.Topic);
            }

            logger.LogInformation(
                "Started Kafka view session for topic {Topic} using session {SessionId}",
                request.Topic,
                request.ConnectionSessionId);

            while (!cancellationToken.IsCancellationRequested)
            {
                var result = consumer.Consume(cancellationToken);
                if (result?.Message is null)
                {
                    continue;
                }

                PublishMessage(request, envelopeFactory.Create(request.ConnectionSessionId, result));
            }

            consumer.Close();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Kafka view session failed for topic {Topic}", request.Topic);
            UpdateError(exception.Message);
        }
        finally
        {
            CompleteSession();
        }
    }

    private void CompleteSession()
    {
        List<Channel<SteakMessageEnvelope>> subscribers;
        lock (_sync)
        {
            _snapshot.IsRunning = false;
            subscribers = [.. _subscribers];
            _subscribers.Clear();
        }

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
