using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Steak.Core.Contracts;
using Steak.Host.Components.Features;
using Steak.Host.Configuration;

namespace Steak.Tests.Host;

public sealed class ViewWorkspaceComponentTests : IDisposable
{
    private readonly BunitContext _context = new();

    [Fact]
    public void ViewWorkspace_StopButton_ShowsClosableToastMessage()
    {
        var viewSessionService = new FakeViewSessionService
        {
            Snapshot = new ViewSessionStatus
            {
                IsRunning = true,
                Topic = "orders",
                ReceivedCount = 11
            }
        };
        var toastService = new UiToastService();

        _context.Services.AddLogging();
        _context.Services.AddSingleton<IViewSessionService>(viewSessionService);
        _context.Services.AddSingleton<ITopicBrowserService>(new FakeTopicBrowserService());
        _context.Services.AddSingleton<IUiToastService>(toastService);

        var cut = _context.Render<ViewWorkspace>(parameters => parameters
            .Add(component => component.ConnectionSessionId, "session-1"));

        cut.Find(".stop-button").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.True(viewSessionService.StopCalled);
            var toast = Assert.Single(toastService.Notifications);
            Assert.Equal("Live view stopped", toast.Title);
            Assert.Contains("orders", toast.Message, StringComparison.Ordinal);
            Assert.Contains("11 messages", toast.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ViewWorkspace_ServiceFinishes_ShowsCompletionToastMessage()
    {
        var viewSessionService = new FakeViewSessionService
        {
            Snapshot = new ViewSessionStatus
            {
                IsRunning = true,
                Topic = "orders",
                ReceivedCount = 5
            }
        };
        var toastService = new UiToastService();

        _context.Services.AddLogging();
        _context.Services.AddSingleton<IViewSessionService>(viewSessionService);
        _context.Services.AddSingleton<ITopicBrowserService>(new FakeTopicBrowserService());
        _context.Services.AddSingleton<IUiToastService>(toastService);

        _ = _context.Render<ViewWorkspace>(parameters => parameters
            .Add(component => component.ConnectionSessionId, "session-1"));

        viewSessionService.SetSnapshot(new ViewSessionStatus
        {
            IsRunning = false,
            Topic = "orders",
            ReceivedCount = 5
        });

        Assert.Equal("Live view finished", Assert.Single(toastService.Notifications).Title);
    }

    private sealed class FakeViewSessionService : IViewSessionService
    {
        public event EventHandler? StateChanged;

        public ViewSessionStatus Snapshot { get; set; } = new();
        public bool StopCalled { get; private set; }

        public Task<ViewSessionStatus> StartAsync(StartViewSessionRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(Snapshot);

        public Task StopAsync()
        {
            StopCalled = true;
            Snapshot = new ViewSessionStatus();
            StateChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public void SetSnapshot(ViewSessionStatus snapshot)
        {
            Snapshot = snapshot;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public async IAsyncEnumerable<SteakMessageEnvelope> StreamAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var message in Snapshot.RecentMessages)
            {
                yield return message;
                await Task.Yield();
            }
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

    public void Dispose() => _context.Dispose();
}
