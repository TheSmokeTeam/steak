using System.Text.Json;
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

        var settings = sessionService.GetActiveSettings(request.ConnectionSessionId);
        var config = configurationService.BuildConfig(settings, KafkaClientKind.Producer);

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

        jobCancellation?.Cancel();
        if (jobTask is not null)
        {
            try
            {
                await jobTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
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

        var interval = request.MessagesPerSecond is > 0
            ? TimeSpan.FromSeconds(1.0 / request.MessagesPerSecond.Value)
            : TimeSpan.Zero;
        var maxMessages = request.MaxMessages is > 0 ? request.MaxMessages.Value : int.MaxValue;
        long publishedCount = 0;
        long discoveredCount = 0;

        try
        {
            using var producer = new ProducerBuilder<byte[]?, byte[]>(config).Build();

            logger.LogInformation(
                "Started batch publish job from {TransportKind} using session {SessionId}",
                request.Source.TransportKind,
                request.ConnectionSessionId);

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

                await producer.ProduceAsync(topic, message, cancellationToken).ConfigureAwait(false);
                publishedCount++;
                UpdateSuccess(startedAt, discoveredCount, publishedCount);

                if (interval > TimeSpan.Zero)
                {
                    await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                }
            }

            producer.Flush(TimeSpan.FromSeconds(10));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Batch publish job failed");
            UpdateError(exception.Message);
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

    private void CompleteJob()
    {
        CancellationTokenSource? cancellationToDispose = null;

        lock (_sync)
        {
            _snapshot.IsRunning = false;

            if (_jobTask?.IsCompleted ?? false)
            {
                _jobTask = null;
                cancellationToDispose = _jobCancellation;
                _jobCancellation = null;
            }
        }

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
