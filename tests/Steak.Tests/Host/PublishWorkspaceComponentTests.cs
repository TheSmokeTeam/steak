using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Steak.Core.Contracts;
using Steak.Host.Components.Features;

namespace Steak.Tests.Host;

public sealed class PublishWorkspaceComponentTests : IDisposable
{
    private readonly BunitContext _context = new();

    [Fact]
    public void PublishWorkspace_BatchMetricsRefreshWhenPublishServiceChanges()
    {
        var batchPublishService = new FakeBatchPublishService();

        _context.Services.AddLogging();
        _context.Services.AddSingleton<IBatchPublishService>(batchPublishService);
        _context.Services.AddSingleton<ITopicBrowserService>(new FakeTopicBrowserService());
        _context.Services.AddSingleton<IMessagePublisher>(new FakeMessagePublisher());
        _context.Services.AddSingleton<IMessagePreviewService>(new FakeMessagePreviewService());
        RegisterFolderPicker();

        var cut = _context.Render<PublishWorkspace>(parameters => parameters
            .Add(component => component.ConnectionSessionId, "session-1"));

        cut.WaitForAssertion(() => Assert.Contains("Batch &amp; single publish", cut.Markup, StringComparison.Ordinal));
        Assert.Contains("Start Publishing", cut.Markup, StringComparison.Ordinal);

        batchPublishService.SetSnapshot(new BatchPublishJobStatus
        {
            IsRunning = true,
            PublishedCount = 2,
            TotalEnvelopes = 3,
            CurrentMessagesPerSecond = 1.5,
            StartedAtUtc = DateTimeOffset.UtcNow
        });

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Stop Publishing", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("2/3 published", cut.Markup, StringComparison.Ordinal);
            Assert.Contains(">2<", cut.Markup, StringComparison.Ordinal);
            Assert.Contains(">3<", cut.Markup, StringComparison.Ordinal);
        });
    }

    private void RegisterFolderPicker()
    {
        var hostAssembly = typeof(PublishWorkspace).Assembly;
        var serviceType = hostAssembly.GetType("Steak.Host.Configuration.ILocalFolderPicker")
            ?? throw new InvalidOperationException("ILocalFolderPicker type could not be found.");
        var implementationType = hostAssembly.GetType("Steak.Host.Configuration.LocalFolderPicker")
            ?? throw new InvalidOperationException("LocalFolderPicker type could not be found.");

        var loggerType = typeof(Logger<>).MakeGenericType(implementationType);
        var logger = Activator.CreateInstance(loggerType, NullLoggerFactory.Instance)
            ?? throw new InvalidOperationException("Typed logger instance could not be created.");
        var picker = Activator.CreateInstance(implementationType, logger)
            ?? throw new InvalidOperationException("LocalFolderPicker instance could not be created.");

        _context.Services.AddSingleton(serviceType, picker);
    }

    private sealed class FakeBatchPublishService : IBatchPublishService
    {
        public event EventHandler? StateChanged;

        public BatchPublishJobStatus Snapshot { get; private set; } = new();

        public Task<BatchPublishJobStatus> StartAsync(BatchPublishRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(Snapshot);

        public Task StopAsync() => Task.CompletedTask;

        public void SetSnapshot(BatchPublishJobStatus snapshot)
        {
            Snapshot = snapshot;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FakeTopicBrowserService : ITopicBrowserService
    {
        private static readonly IReadOnlyList<KafkaTopicSummary> Topics =
        [
            new KafkaTopicSummary
            {
                Name = "orders",
                PartitionCount = 1,
                Partitions = [new TopicPartitionSummary { PartitionId = 0, Leader = "localhost:9092", InSyncReplicas = ["localhost:9092"] }]
            }
        ];

        public Task<IReadOnlyList<KafkaTopicSummary>> ListTopicsAsync(string connectionSessionId, CancellationToken cancellationToken = default)
            => Task.FromResult(Topics);

        public Task<KafkaTopicSummary?> GetTopicAsync(string connectionSessionId, string topic, CancellationToken cancellationToken = default)
            => Task.FromResult(Topics.FirstOrDefault(candidate => string.Equals(candidate.Name, topic, StringComparison.Ordinal)));
    }

    private sealed class FakeMessagePublisher : IMessagePublisher
    {
        public Task<PublishResultInfo> PublishAsync(PublishEnvelopeRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new PublishResultInfo
            {
                Status = "Persisted",
                Topic = request.Topic ?? request.Envelope.Topic ?? "orders",
                Partition = 0,
                Offset = 0,
                TimestampUtc = DateTimeOffset.UtcNow
            });
    }

    private sealed class FakeMessagePreviewService : IMessagePreviewService
    {
        public MessagePreview CreatePreview(string? keyBase64, string valueBase64)
            => new()
            {
                ValueIsUtf8 = true,
                ValueUtf8Preview = "preview",
                ValuePrettyJson = "{}",
                ValueHexPreview = "70 72 65 76 69 65 77"
            };

        public SteakMessageHeader CreateHeaderPreview(string key, byte[]? value)
            => new()
            {
                Key = key,
                ValueBase64 = value is null ? null : Convert.ToBase64String(value)
            };
    }

    public void Dispose() => _context.Dispose();
}
