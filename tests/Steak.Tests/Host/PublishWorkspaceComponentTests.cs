using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Steak.Core.Contracts;
using Steak.Host.Configuration;
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
        _context.Services.AddSingleton<ILocalFolderPicker>(new FakeFolderPicker("D:\\Exports\\publish"));
        _context.Services.AddSingleton<IUiToastService>(new UiToastService());

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

    [Fact]
    public void PublishWorkspace_BrowseButton_InvokesFolderPickerAndUpdatesPath()
    {
        var folderPicker = new FakeFolderPicker("D:\\Exports\\publish");

        _context.Services.AddLogging();
        _context.Services.AddSingleton<IBatchPublishService>(new FakeBatchPublishService());
        _context.Services.AddSingleton<ITopicBrowserService>(new FakeTopicBrowserService());
        _context.Services.AddSingleton<IMessagePublisher>(new FakeMessagePublisher());
        _context.Services.AddSingleton<IMessagePreviewService>(new FakeMessagePreviewService());
        _context.Services.AddSingleton<ILocalFolderPicker>(folderPicker);
        _context.Services.AddSingleton<IUiToastService>(new UiToastService());

        var cut = _context.Render<PublishWorkspace>(parameters => parameters
            .Add(component => component.ConnectionSessionId, "session-1"));

        var browseButton = cut.Find(".browse-button");
        Assert.Equal("button", browseButton.GetAttribute("type"));

        browseButton.Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(1, folderPicker.CallCount);
            Assert.Equal("D:\\Exports\\publish", cut.Find(".browse-row input").GetAttribute("value"));
        });
    }

    [Fact]
    public void PublishWorkspace_StopButton_ShowsClosableToastMessage()
    {
        var batchPublishService = new FakeBatchPublishService();
        batchPublishService.SetSnapshot(new BatchPublishJobStatus
        {
            IsRunning = true,
            PublishedCount = 4
        });
        var toastService = new UiToastService();

        _context.Services.AddLogging();
        _context.Services.AddSingleton<IBatchPublishService>(batchPublishService);
        _context.Services.AddSingleton<ITopicBrowserService>(new FakeTopicBrowserService());
        _context.Services.AddSingleton<IMessagePublisher>(new FakeMessagePublisher());
        _context.Services.AddSingleton<IMessagePreviewService>(new FakeMessagePreviewService());
        _context.Services.AddSingleton<ILocalFolderPicker>(new FakeFolderPicker("D:\\Exports\\publish"));
        _context.Services.AddSingleton<IUiToastService>(toastService);

        var cut = _context.Render<PublishWorkspace>(parameters => parameters
            .Add(component => component.ConnectionSessionId, "session-1"));

        cut.Find(".stop-button").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(1, batchPublishService.StopCallCount);
            var toast = Assert.Single(toastService.Notifications);
            Assert.Equal("Publish stopped", toast.Title);
            Assert.Contains("4 messages", toast.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void PublishWorkspace_ServiceFinishes_ShowsCompletionToastMessage()
    {
        var batchPublishService = new FakeBatchPublishService();
        batchPublishService.SetSnapshot(new BatchPublishJobStatus
        {
            IsRunning = true,
            PublishedCount = 8,
            TotalEnvelopes = 8
        });
        var toastService = new UiToastService();

        _context.Services.AddLogging();
        _context.Services.AddSingleton<IBatchPublishService>(batchPublishService);
        _context.Services.AddSingleton<ITopicBrowserService>(new FakeTopicBrowserService());
        _context.Services.AddSingleton<IMessagePublisher>(new FakeMessagePublisher());
        _context.Services.AddSingleton<IMessagePreviewService>(new FakeMessagePreviewService());
        _context.Services.AddSingleton<ILocalFolderPicker>(new FakeFolderPicker("D:\\Exports\\publish"));
        _context.Services.AddSingleton<IUiToastService>(toastService);

        _ = _context.Render<PublishWorkspace>(parameters => parameters
            .Add(component => component.ConnectionSessionId, "session-1"));

        batchPublishService.SetSnapshot(new BatchPublishJobStatus
        {
            IsRunning = false,
            PublishedCount = 8,
            TotalEnvelopes = 8
        });

        Assert.Equal("Publish finished", Assert.Single(toastService.Notifications).Title);
    }

    private sealed class FakeBatchPublishService : IBatchPublishService
    {
        public event EventHandler? StateChanged;

        public BatchPublishJobStatus Snapshot { get; private set; } = new();
        public int StopCallCount { get; private set; }

        public Task<BatchPublishJobStatus> StartAsync(BatchPublishRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(Snapshot);

        public Task StopAsync()
        {
            StopCallCount++;
            Snapshot = new BatchPublishJobStatus();
            StateChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

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

    private sealed class FakeFolderPicker(string selectedPath) : ILocalFolderPicker
    {
        public int CallCount { get; private set; }

        public Task<string?> PickFolderAsync(string? initialPath = null, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<string?>(selectedPath);
        }
    }

    public void Dispose() => _context.Dispose();
}
