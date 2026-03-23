using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Steak.Core.Contracts;

namespace Steak.Core.Services;

internal sealed class BatchPublishBackgroundService(
    IConnectionSessionService sessionService,
    IKafkaConfigurationService configurationService,
    IMessageEnvelopeFactory envelopeFactory,
    IEnumerable<IBatchEnvelopeReader> readers,
    ILogger<BatchPublishBackgroundService> logger) : BackgroundService, IBatchPublishService
{
    private readonly object _sync = new();
    private CancellationToken _hostStoppingToken;
    private Task? _jobTask;
    private CancellationTokenSource? _jobCancellation;
    private BatchPublishJobStatus _snapshot = new();

    public event EventHandler? StateChanged;

    public BatchPublishJobStatus Snapshot
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
            logger.LogDebug("Batch publish background service host lifetime cancelled");
        }
    }

    public async Task<BatchPublishJobStatus> StartAsync(BatchPublishRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ConnectionSessionId))
            throw new InvalidOperationException("connectionSessionId is required to start a batch publish job.");

        lock (_sync)
        {
            if (_snapshot.IsRunning)
                throw new InvalidOperationException("Only one batch publish job can run at a time.");
        }

        logger.LogDebug(
            "Starting Kafka batch publish job. SessionId {SessionId}, source {Source}, topic override {TopicOverride}, max messages {MaxMessages}, messages/sec {MessagesPerSecond}, loop {Loop}",
            request.ConnectionSessionId,
            KafkaDiagnostics.FormatSource(request.Source),
            request.TopicOverride,
            request.MaxMessages,
            request.MessagesPerSecond,
            request.Loop);

        var settings = sessionService.GetActiveSettings(request.ConnectionSessionId);
        var config = configurationService.BuildConfig(settings, KafkaClientKind.Producer);

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "Kafka batch publish config for session {SessionId}: {KafkaConfig}",
                request.ConnectionSessionId,
                KafkaDiagnostics.FormatConfig(config));
        }

        var jobCancellation = CancellationTokenSource.CreateLinkedTokenSource(_hostStoppingToken);
        lock (_sync)
        {
            _jobCancellation = jobCancellation;
            _snapshot = new BatchPublishJobStatus
            {
                IsRunning = true,
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
            logger.LogDebug("Stopping active Kafka batch publish job");
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
                logger.LogDebug("Kafka batch publish job stop observed operation cancellation");
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
        BatchPublishRequest request,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var reader = readers.FirstOrDefault(r => r.TransportKind == request.Source.TransportKind)
            ?? throw new InvalidOperationException($"No envelope reader registered for transport {request.Source.TransportKind}.");

        logger.LogDebug(
            "Kafka batch publish job selected reader {ReaderType} for source {Source}",
            reader.GetType().Name,
            KafkaDiagnostics.FormatSource(request.Source));

        var interval = request.MessagesPerSecond is > 0
            ? TimeSpan.FromSeconds(1.0 / request.MessagesPerSecond.Value)
            : TimeSpan.Zero;
        var maxMessages = request.MaxMessages is > 0 ? request.MaxMessages.Value : int.MaxValue;
        long publishedCount = 0;
        long discoveredCount = 0;

        try
        {
            using var producer = new ProducerBuilder<byte[]?, byte[]>(config)
                .SetErrorHandler((_, error) =>
                {
                    if (error.IsFatal)
                    {
                        logger.LogCritical("Fatal Kafka producer error in batch publish job: {Reason}", error.Reason);
                    }
                    else
                    {
                        logger.LogWarning("Kafka producer warning in batch publish job: {Reason}", error.Reason);
                    }

                    UpdateError($"Kafka producer error: {error.Reason}");
                })
                .Build();

            do
            {
                logger.LogDebug("Kafka batch publish loop iteration started");

                await foreach (var envelope in reader.ReadEnvelopesAsync(request.Source, cancellationToken).ConfigureAwait(false))
                {
                    if (cancellationToken.IsCancellationRequested || publishedCount >= maxMessages)
                        break;

                    discoveredCount++;
                    var normalized = envelopeFactory.NormalizeForPublish(envelope, request.ConnectionSessionId, request.TopicOverride);
                    var topic = normalized.Topic ?? throw new InvalidOperationException("Envelope is missing a topic.");

                    var message = new Message<byte[]?, byte[]>
                    {
                        Key = normalized.KeyBase64 is not null ? Convert.FromBase64String(normalized.KeyBase64) : null,
                        Value = Convert.FromBase64String(normalized.ValueBase64)
                    };

                    if (normalized.Headers.Count > 0)
                    {
                        message.Headers = new Headers();
                        foreach (var header in normalized.Headers)
                        {
                            message.Headers.Add(header.Key, header.ValueBase64 is not null ? Convert.FromBase64String(header.ValueBase64) : null);
                        }
                    }

                    logger.LogDebug(
                        "Kafka batch publish job discovered envelope {EnvelopeSummary}. Publishing to topic {Topic}. Discovered {DiscoveredCount}, published {PublishedCount}",
                        KafkaDiagnostics.FormatEnvelopeSummary(normalized),
                        topic,
                        discoveredCount,
                        publishedCount);

                    var delivery = await producer.ProduceAsync(topic, message, cancellationToken).ConfigureAwait(false);
                    publishedCount++;

                    logger.LogDebug(
                        "Kafka batch publish delivered to topic {Topic}, partition {Partition}, offset {Offset}, status {Status}",
                        delivery.Topic,
                        delivery.Partition.Value,
                        delivery.Offset.Value,
                        delivery.Status);

                    UpdateSuccess(startedAt, discoveredCount, publishedCount);

                    if (interval > TimeSpan.Zero)
                    {
                        await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                    }
                }

                if (publishedCount >= maxMessages)
                    break;
            }
            while (request.Loop && !cancellationToken.IsCancellationRequested);

            logger.LogDebug("Flushing Kafka batch publish producer");
            producer.Flush(TimeSpan.FromSeconds(10));
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("Kafka batch publish job cancelled");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Batch publish job failed");
            UpdateError(SteakErrorDetails.Format(exception));
        }
        finally
        {
            CompleteJob();
        }
    }

    private void UpdateSuccess(DateTimeOffset startedAt, long discoveredCount, long publishedCount)
    {
        lock (_sync)
        {
            _snapshot.PublishedCount = publishedCount;
            _snapshot.TotalEnvelopes = discoveredCount;
            _snapshot.LastError = null;
            var elapsedSeconds = Math.Max((DateTimeOffset.UtcNow - startedAt).TotalSeconds, 1);
            _snapshot.CurrentMessagesPerSecond = Math.Round(publishedCount / elapsedSeconds, 2);
        }

        logger.LogDebug(
            "Kafka batch publish progress updated. Published {PublishedCount} of {DiscoveredCount} discovered envelope(s)",
            publishedCount,
            discoveredCount);

        NotifyStateChanged();
    }

    private void UpdateError(string error)
    {
        lock (_sync)
        {
            _snapshot.LastError = error;
        }

        logger.LogError("Kafka batch publish state updated with error: {Error}", error);
        NotifyStateChanged();
    }

    private void CompleteJob()
    {
        CancellationTokenSource? cancellationToDispose = null;
        BatchPublishJobStatus snapshot;

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
            "Kafka batch publish job completed. Published {PublishedCount} message(s), discovered {DiscoveredCount}, last error: {LastError}",
            snapshot.PublishedCount,
            snapshot.TotalEnvelopes,
            snapshot.LastError);

        cancellationToDispose?.Dispose();
        NotifyStateChanged();
    }

    private void NotifyStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static BatchPublishJobStatus Clone(BatchPublishJobStatus source)
    {
        return new BatchPublishJobStatus
        {
            IsRunning = source.IsRunning,
            PublishedCount = source.PublishedCount,
            TotalEnvelopes = source.TotalEnvelopes,
            CurrentMessagesPerSecond = source.CurrentMessagesPerSecond,
            LastError = source.LastError,
            StartedAtUtc = source.StartedAtUtc
        };
    }
}
