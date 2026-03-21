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

        var settings = sessionService.GetActiveSettings(request.ConnectionSessionId);
        var config = configurationService.BuildConfig(settings, KafkaClientKind.Consumer);

        config["group.id"] = request.GroupId.Trim();
        config["auto.offset.reset"] = request.OffsetMode.ToAutoOffsetReset().ToString().ToLowerInvariant();
        config["enable.auto.commit"] = "true";
        config["enable.auto.offset.store"] = "false";
        config["auto.commit.interval.ms"] = "1000";

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
        CreateConsumeJobRequest request,
        IReadOnlyDictionary<string, string> config,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var writer = writers.FirstOrDefault(w => w.TransportKind == request.Destination.TransportKind)
            ?? throw new InvalidOperationException($"No envelope writer registered for transport {request.Destination.TransportKind}.");

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
                "Started Kafka export job for topic {Topic} using session {SessionId}",
                request.Topic,
                request.ConnectionSessionId);

            while (!cancellationToken.IsCancellationRequested && exportedCount < maxMessages)
            {
                var result = consumer.Consume(cancellationToken);
                if (result?.Message is null)
                {
                    continue;
                }

                var envelope = envelopeFactory.Create(request.ConnectionSessionId, result);
                var fileName = fileNameFactory.CreateMessageFileName(envelope);

                var destination = await writer.WriteEnvelopeAsync(envelope, fileName, request.Destination, cancellationToken).ConfigureAwait(false);

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
                consumer.Commit();
            }
            catch (KafkaException exception)
            {
                logger.LogWarning(exception, "Final Kafka offset commit failed when stopping export job.");
            }

            consumer.Close();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Kafka export job failed for topic {Topic}", request.Topic);
            UpdateError(exception.Message);
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
