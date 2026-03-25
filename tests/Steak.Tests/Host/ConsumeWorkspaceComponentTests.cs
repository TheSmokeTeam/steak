using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Steak.Core.Contracts;
using Steak.Core.Services;
using Steak.Host.Configuration;
using Steak.Host.Components.Features;

namespace Steak.Tests.Host;

public sealed class ConsumeWorkspaceComponentTests : IDisposable
{
    private readonly BunitContext _context = new();

    [Fact]
    public void ConsumeWorkspace_BrowseButton_InvokesFolderPickerAndUpdatesPath()
    {
        var folderPicker = new FakeFolderPicker("D:\\Exports\\consume");

        _context.Services.AddLogging();
        _context.Services.AddSingleton<IConsumeExportService>(new FakeConsumeExportService());
        _context.Services.AddSingleton<ITopicBrowserService>(new FakeTopicBrowserService());
        _context.Services.AddSingleton<IFileNameFactory, FileNameFactory>();
        _context.Services.AddSingleton<ILocalFolderPicker>(folderPicker);
        _context.Services.AddSingleton<IUiToastService>(new UiToastService());

        var cut = _context.Render<ConsumeWorkspace>(parameters => parameters
            .Add(component => component.ConnectionSessionId, "session-1")
            .Add(component => component.Username, "demo-user"));

        var browseButton = cut.Find(".browse-button");
        Assert.Equal("button", browseButton.GetAttribute("type"));

        browseButton.Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(1, folderPicker.CallCount);
            Assert.Equal("D:\\Exports\\consume", cut.Find(".browse-row input").GetAttribute("value"));
        });
    }

    [Fact]
    public void ConsumeWorkspace_StopButton_ShowsClosableToastMessage()
    {
        var consumeService = new FakeConsumeExportService
        {
            Snapshot = new ConsumeJobStatus
            {
                IsRunning = true,
                Topic = "orders",
                ExportedCount = 7
            }
        };
        var toastService = new UiToastService();

        _context.Services.AddLogging();
        _context.Services.AddSingleton<IConsumeExportService>(consumeService);
        _context.Services.AddSingleton<ITopicBrowserService>(new FakeTopicBrowserService());
        _context.Services.AddSingleton<IFileNameFactory, FileNameFactory>();
        _context.Services.AddSingleton<ILocalFolderPicker>(new FakeFolderPicker("D:\\Exports\\consume"));
        _context.Services.AddSingleton<IUiToastService>(toastService);

        var cut = _context.Render<ConsumeWorkspace>(parameters => parameters
            .Add(component => component.ConnectionSessionId, "session-1")
            .Add(component => component.Username, "demo-user"));

        cut.Find(".stop-button").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.True(consumeService.StopCalled);
            var toast = Assert.Single(toastService.Notifications);
            Assert.Equal("Consume stopped", toast.Title);
            Assert.Contains("orders", toast.Message, StringComparison.Ordinal);
            Assert.Contains("7 messages", toast.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ConsumeWorkspace_ServiceFinishes_ShowsCompletionToastMessage()
    {
        var consumeService = new FakeConsumeExportService
        {
            Snapshot = new ConsumeJobStatus
            {
                IsRunning = true,
                Topic = "orders",
                ExportedCount = 9
            }
        };
        var toastService = new UiToastService();

        _context.Services.AddLogging();
        _context.Services.AddSingleton<IConsumeExportService>(consumeService);
        _context.Services.AddSingleton<ITopicBrowserService>(new FakeTopicBrowserService());
        _context.Services.AddSingleton<IFileNameFactory, FileNameFactory>();
        _context.Services.AddSingleton<ILocalFolderPicker>(new FakeFolderPicker("D:\\Exports\\consume"));
        _context.Services.AddSingleton<IUiToastService>(toastService);

        _ = _context.Render<ConsumeWorkspace>(parameters => parameters
            .Add(component => component.ConnectionSessionId, "session-1")
            .Add(component => component.Username, "demo-user"));

        consumeService.SetSnapshot(new ConsumeJobStatus
        {
            IsRunning = false,
            Topic = "orders",
            ExportedCount = 9
        });

        Assert.Equal("Consume finished", Assert.Single(toastService.Notifications).Title);
    }

    private sealed class FakeConsumeExportService : IConsumeExportService
    {
        public event EventHandler? StateChanged;

        public ConsumeJobStatus Snapshot { get; set; } = new();
        public bool StopCalled { get; private set; }

        public Task<ConsumeJobStatus> StartAsync(CreateConsumeJobRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(Snapshot);

        public Task StopAsync()
        {
            StopCalled = true;
            Snapshot = new ConsumeJobStatus();
            StateChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public void SetSnapshot(ConsumeJobStatus snapshot)
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
