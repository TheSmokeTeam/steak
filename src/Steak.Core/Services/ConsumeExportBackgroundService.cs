using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Steak.Core.Contracts;

namespace Steak.Core.Services;

internal sealed class ConsumeExportBackgroundService(
    IConnectionSessionService sessionService,
    IKafkaConfigurationService configurationService,
    ConsumedMessageEnvelopeFactory envelopeFactory,
    IFileNameFactory fileNameFactory,
    IEnumerable<IBatchEnvelopeWriter> writers,
    ILogger<ConsumeExportBackgroundService> logger) : BackgroundService, IConsumeExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly object _sync = new();
    private CancellationToken _hostStoppingToken;
    private Task? _jobTask;
    private CancellationTokenSource? _jobCancellation;
    private ConsumeJobStatus _snapshot = new();

    public event EventHandler? StateChanged;

    public ConsumeJobStatus Snapshot
    {
        get
        {
            lock (_sync)
            {
                return Clone(_snapshot);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _hostStoppingToken = stoppingToken;
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("Consume export background service host lifetime cancelled");
        }
    }

    public async Task<ConsumeJobStatus> StartAsync(CreateConsumeJobRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ConnectionSessionId))
        {
            throw new InvalidOperationException("connectionSessionId is required to start an export job.");
        }

        if (string.IsNullOrWhiteSpace(request.Topic))
        {
            throw new InvalidOperationException("topic is required to start an export job.");
        }

        if (string.IsNullOrWhiteSpace(request.GroupId))
        {
            throw new InvalidOperationException("groupId is required to start an export job.");
        }

        lock (_sync)
        {
            if (_snapshot.IsRunning)
            {
                throw new InvalidOperationException("Only one consume export job can run at a time.");
            }
        }

        logger.LogDebug(
            "Starting Kafka consume export job. SessionId {SessionId}, topic {Topic}, group {GroupId}, partition {Partition}, offset mode {OffsetMode}, destination {Destination}, max messages {MaxMessages}, messages/sec {MessagesPerSecond}",
            request.ConnectionSessionId,
            request.Topic,
            request.GroupId,
            request.Partition,
            request.OffsetMode,
            KafkaDiagnostics.FormatDestination(request.Destination),
            request.MaxMessages,
            request.MessagesPerSecond);

        var settings = sessionService.GetActiveSettings(request.ConnectionSessionId);
        var config = configurationService.BuildConfig(settings, KafkaClientKind.Consumer);

        config["group.id"] = request.GroupId.Trim();
        config["auto.offset.reset"] = request.OffsetMode.ToAutoOffsetReset().ToString().ToLowerInvariant();
        config["enable.auto.commit"] = "true";
        config["enable.auto.offset.store"] = "false";
        config["auto.commit.interval.ms"] = "1000";

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "Kafka consume export config for session {SessionId}: {KafkaConfig}",
                request.ConnectionSessionId,
                KafkaDiagnostics.FormatConfig(config));
        }

        var jobCancellation = CancellationTokenSource.CreateLinkedTokenSource(_hostStoppingToken);
        lock (_sync)
        {
            _jobCancellation = jobCancellation;
            _snapshot = new ConsumeJobStatus
            {
                IsRunning = true,
                ConnectionSessionId = request.ConnectionSessionId,
                Topic = request.Topic,
                GroupId = request.GroupId,
                Partition = request.Partition,
                OffsetMode = request.OffsetMode,
                StartedAtUtc = DateTimeOffset.UtcNow
            };
        }

        NotifyStateChanged();
        _jobTask = Task.Run(() => RunAsync(request, config, jobCancellation.Token), CancellationToken.None);
        return Snapshot;
    }

    public async Task StopAsync()
    {
        Task? jobTask;
        CancellationTokenSource? jobCancellation;

        lock (_sync)
        {
            jobTask = _jobTask;
            jobCancellation = _jobCancellation;
            _jobTask = null;
            _jobCancellation = null;
        }

        if (jobTask is not null)
        {
            logger.LogDebug("Stopping active Kafka consume export job");
        }

        jobCancellation?.Cancel();
        if (jobTask is not null)
        {
            try
            {
                await jobTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                logger.LogDebug("Kafka consume export job stop observed operation cancellation");
            }
        }

        jobCancellation?.Dispose();
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await StopAsync().ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RunAsync(
        CreateConsumeJobRequest request,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var writer = writers.FirstOrDefault(w => w.TransportKind == request.Destination.TransportKind)
            ?? throw new InvalidOperationException($"No envelope writer registered for transport {request.Destination.TransportKind}.");

        logger.LogDebug(
            "Kafka consume export job selected writer {WriterType} for destination {Destination}",
            writer.GetType().Name,
            KafkaDiagnostics.FormatDestination(request.Destination));

        // Build a simple rate limiter when a target throughput is specified.
        var interval = request.MessagesPerSecond is > 0
            ? TimeSpan.FromSeconds(1.0 / request.MessagesPerSecond.Value)
            : TimeSpan.Zero;
        var maxMessages = request.MaxMessages is > 0 ? request.MaxMessages.Value : int.MaxValue;
        long exportedCount = 0;

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
                            "Fatal Kafka consumer error in export job for topic {Topic}: {Reason}",
                            request.Topic,
                            error.Reason);
                    }
                    else
                    {
                        logger.LogWarning(
                            "Kafka consumer warning in export job for topic {Topic}: {Reason}",
                            request.Topic,
                            error.Reason);
                    }

                    UpdateError($"Kafka consumer error: {error.Reason}");
                })
                .SetPartitionsAssignedHandler((_, partitions) =>
                {
                    logger.LogDebug(
                        "Kafka export job assigned partitions for topic {Topic}: {Partitions}",
                        request.Topic,
                        string.Join(", ", partitions.Select(partition => partition.ToString())));
                })
                .SetPartitionsRevokedHandler((_, partitions) =>
                {
                    logger.LogDebug(
                        "Kafka export job revoked partitions for topic {Topic}: {Partitions}",
                        request.Topic,
                        string.Join(", ", partitions.Select(partition => partition.ToString())));
                })
                .Build();

            if (request.Partition.HasValue)
            {
                var assignment = new TopicPartitionOffset(request.Topic, request.Partition.Value, request.OffsetMode.ToOffset());
                logger.LogDebug("Assigning Kafka export job to {Assignment}", assignment);
                consumer.Assign(assignment);
            }
            else
            {
                logger.LogDebug("Subscribing Kafka export job to topic {Topic}", request.Topic);
                consumer.Subscribe(request.Topic);
            }

            while (!cancellationToken.IsCancellationRequested && exportedCount < maxMessages)
            {
                var result = consumer.Consume(cancellationToken);
                if (result?.Message is null)
                {
                    logger.LogDebug("Kafka export consume cycle returned no message for topic {Topic}", request.Topic);
                    continue;
                }

                logger.LogDebug(
                    "Kafka export job consumed message. Topic {Topic}, partition {Partition}, offset {Offset}, key bytes {KeyBytes}, value bytes {ValueBytes}, headers {HeaderCount}",
                    result.Topic,
                    result.Partition.Value,
                    result.Offset.Value,
                    result.Message.Key?.Length ?? 0,
                    result.Message.Value?.Length ?? 0,
                    result.Message.Headers?.Count ?? 0);

                var envelope = envelopeFactory.Create(request.ConnectionSessionId, result);
                var fileName = fileNameFactory.CreateMessageFileName(envelope);

                logger.LogDebug(
                    "Kafka export job prepared envelope {EnvelopeSummary} with file name {FileName}",
                    KafkaDiagnostics.FormatEnvelopeSummary(envelope),
                    fileName);

                var destination = await writer.WriteEnvelopeAsync(envelope, fileName, request.Destination, cancellationToken).ConfigureAwait(false);

                logger.LogDebug(
                    "Kafka export job wrote envelope to {Destination} and is storing offset {Offset}",
                    destination,
                    result.Offset.Value);

                consumer.StoreOffset(result);
                exportedCount++;
                UpdateSuccess(destination, startedAt, exportedCount);

                if (interval > TimeSpan.Zero)
                {
                    await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                }
            }

            try
            {
                logger.LogDebug("Kafka export job committing final offsets");
                consumer.Commit();
            }
            catch (KafkaException exception)
            {
                logger.LogWarning(exception, "Final Kafka offset commit failed when stopping export job.");
            }

            logger.LogDebug("Closing Kafka export consumer for topic {Topic}", request.Topic);
            consumer.Close();
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("Kafka export job cancelled for topic {Topic}", request.Topic);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Kafka export job failed for topic {Topic}", request.Topic);
            UpdateError(SteakErrorDetails.Format(exception));
        }
        finally
        {
            CompleteJob();
        }
    }

    private void UpdateSuccess(string destination, DateTimeOffset startedAt, long count)
    {
        lock (_sync)
        {
            _snapshot.ExportedCount = count;
            _snapshot.LastDestination = destination;
            _snapshot.LastError = null;
            var elapsedSeconds = Math.Max((DateTimeOffset.UtcNow - startedAt).TotalSeconds, 1);
            _snapshot.CurrentMessagesPerSecond = Math.Round(count / elapsedSeconds, 2);
        }

        logger.LogDebug(
            "Kafka export job progress updated. Exported {Count} message(s). Last destination {Destination}",
            count,
            destination);

        NotifyStateChanged();
    }

    private void UpdateError(string error)
    {
        lock (_sync)
        {
            _snapshot.LastError = error;
        }

        logger.LogError("Kafka export job state updated with error: {Error}", error);
        NotifyStateChanged();
    }

    private void CompleteJob()
    {
        CancellationTokenSource? cancellationToDispose = null;
        ConsumeJobStatus snapshot;

        lock (_sync)
        {
            _snapshot.IsRunning = false;
            snapshot = Clone(_snapshot);

            if (_jobTask?.IsCompleted ?? false)
            {
                _jobTask = null;
                cancellationToDispose = _jobCancellation;
                _jobCancellation = null;
            }
        }

        logger.LogDebug(
            "Kafka export job completed for topic {Topic}. Exported {ExportedCount} message(s). Last error: {LastError}",
            snapshot.Topic,
            snapshot.ExportedCount,
            snapshot.LastError);

        cancellationToDispose?.Dispose();
        NotifyStateChanged();
    }

    private void NotifyStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static ConsumeJobStatus Clone(ConsumeJobStatus source)
    {
        return new ConsumeJobStatus
        {
            IsRunning = source.IsRunning,
            ConnectionSessionId = source.ConnectionSessionId,
            Topic = source.Topic,
            GroupId = source.GroupId,
            Partition = source.Partition,
            OffsetMode = source.OffsetMode,
            StartedAtUtc = source.StartedAtUtc,
            ExportedCount = source.ExportedCount,
            LastDestination = source.LastDestination,
            LastError = source.LastError,
            CurrentMessagesPerSecond = source.CurrentMessagesPerSecond
        };
    }
}
