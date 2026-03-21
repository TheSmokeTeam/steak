using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Steak.Core.Contracts;
using Steak.Host;

namespace Steak.Tests.Api;

public sealed class SteakApiTests : IClassFixture<SteakApiTests.TestAppFactory>
{
    private static readonly JsonSerializerOptions ApiJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly TestAppFactory _factory;

    public SteakApiTests(TestAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Connect_ReturnsSessionId()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/connection", new ConnectRequest
        {
            Settings = new KafkaConnectionSettings { BootstrapServers = "localhost:9092" }
        });

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ConnectResponse>();

        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.ConnectionSessionId));
    }

    [Fact]
    public async Task ConnectionStatus_ReturnsCurrentState()
    {
        using var client = _factory.CreateClient();

        var status = await client.GetFromJsonAsync<ConnectionSessionStatus>("/api/connection");

        Assert.NotNull(status);
    }

    [Fact]
    public async Task Topics_ReturnFakeMetadata()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetFromJsonAsync<List<KafkaTopicSummary>>("/api/topics?connectionSessionId=demo");

        Assert.NotNull(response);
        Assert.Single(response);
        Assert.Equal("orders", response[0].Name);
    }

    [Fact]
    public async Task ConsumeJobLifecycle_ReturnsStatus()
    {
        using var client = _factory.CreateClient();

        var start = await client.PostAsJsonAsync("/api/consume-jobs", new CreateConsumeJobRequest
        {
            ConnectionSessionId = "demo",
            Topic = "orders",
            GroupId = "steak-export-orders"
        });

        start.EnsureSuccessStatusCode();
        var status = await client.GetFromJsonAsync<ConsumeJobStatus>("/api/consume-jobs", ApiJsonOptions);

        Assert.NotNull(status);
        Assert.True(status.IsRunning);

        var stop = await client.DeleteAsync("/api/consume-jobs");
        Assert.Equal(HttpStatusCode.NoContent, stop.StatusCode);
    }

    [Fact]
    public async Task Publish_ReturnsDeliveryResult()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/publish", new PublishEnvelopeRequest
        {
            ConnectionSessionId = "demo",
            Topic = "orders",
            Envelope = new SteakMessageEnvelope
            {
                ValueBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"ok":true}"""))
            }
        });

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PublishResultInfo>();

        Assert.NotNull(result);
        Assert.Equal("orders", result.Topic);
        Assert.Equal(42, result.Offset);
    }

    [Fact]
    public async Task ViewSessionEvents_StreamSsePayload()
    {
        using var client = _factory.CreateClient();

        var start = await client.PostAsJsonAsync("/api/view-sessions", new StartViewSessionRequest
        {
            ConnectionSessionId = "demo",
            Topic = "orders"
        });
        start.EnsureSuccessStatusCode();

        var response = await client.GetAsync("/api/view-sessions/events", HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        var firstLine = await reader.ReadLineAsync();

        Assert.NotNull(firstLine);
        Assert.StartsWith("data:", firstLine);
        Assert.Contains("orders", firstLine);
    }

    [Fact]
    public async Task ViewSession_StartAcceptsStringEnumPayload()
    {
        using var client = _factory.CreateClient();

        using var content = new StringContent(
            """
            {
              "connectionSessionId": "demo",
              "topic": "orders",
              "offsetMode": "Earliest"
            }
            """,
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("/api/view-sessions", content);

        response.EnsureSuccessStatusCode();
        var status = await response.Content.ReadFromJsonAsync<ViewSessionStatus>(ApiJsonOptions);

        Assert.NotNull(status);
        Assert.True(status.IsRunning);
        Assert.Equal(MessageOffsetMode.Earliest, status.OffsetMode);
    }

    [Fact]
    public async Task PublishWithoutSession_ReturnsProblemDetails()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/publish", new PublishEnvelopeRequest
        {
            Envelope = new SteakMessageEnvelope()
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("Steak API error", payload);
    }

    [Fact]
    public async Task BatchPublish_ReturnsStatus()
    {
        using var client = _factory.CreateClient();

        var start = await client.PostAsJsonAsync("/api/batch-publish", new BatchPublishRequest
        {
            ConnectionSessionId = "demo",
            Source = new BatchSourceOptions
            {
                TransportKind = BatchTransportKind.FileSystem,
                FileSystem = new FileSystemLocationOptions { Path = "D:\\test" }
            }
        });

        start.EnsureSuccessStatusCode();
        var status = await client.GetFromJsonAsync<BatchPublishJobStatus>("/api/batch-publish");
        Assert.NotNull(status);

        var stop = await client.DeleteAsync("/api/batch-publish");
        Assert.Equal(HttpStatusCode.NoContent, stop.StatusCode);
    }

    public sealed class TestAppFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("Steak:Runtime:LaunchBrowser", "false");
            builder.UseSetting("Steak:Storage:DataRoot", Path.Combine(Path.GetTempPath(), "Steak.Tests", Guid.NewGuid().ToString("N")));

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IConnectionSessionService>();
                services.RemoveAll<ITopicBrowserService>();
                services.RemoveAll<IViewSessionService>();
                services.RemoveAll<IConsumeExportService>();
                services.RemoveAll<IMessagePublisher>();
                services.RemoveAll<IBatchPublishService>();

                services.AddSingleton<IConnectionSessionService, FakeConnectionSessionService>();
                services.AddSingleton<ITopicBrowserService, FakeTopicBrowserService>();
                services.AddSingleton<IViewSessionService, FakeViewSessionService>();
                services.AddSingleton<IConsumeExportService, FakeConsumeExportService>();
                services.AddSingleton<IMessagePublisher, FakeMessagePublisher>();
                services.AddSingleton<IBatchPublishService, FakeBatchPublishService>();
            });
        }
    }

    private sealed class FakeConnectionSessionService : IConnectionSessionService
    {
        private string? _sessionId;
        private KafkaConnectionSettings _settings = new();

        public ConnectResponse Connect(ConnectRequest request)
        {
            _sessionId = Guid.NewGuid().ToString("N");
            _settings = request.Settings;
            return new ConnectResponse { ConnectionSessionId = _sessionId };
        }

        public void Disconnect() => _sessionId = null;

        public ConnectionSessionStatus GetStatus() => new()
        {
            IsConnected = _sessionId is not null,
            ConnectionSessionId = _sessionId,
            BootstrapServers = _settings.BootstrapServers
        };

        public KafkaConnectionSettings GetActiveSettings(string connectionSessionId) => _settings;
    }

    private sealed class FakeTopicBrowserService : ITopicBrowserService
    {
        private static readonly KafkaTopicSummary Topic = new()
        {
            Name = "orders",
            PartitionCount = 2,
            Partitions =
            [
                new TopicPartitionSummary { PartitionId = 0, Leader = "broker-1:9092", InSyncReplicas = ["broker-1:9092"] },
                new TopicPartitionSummary { PartitionId = 1, Leader = "broker-2:9092", InSyncReplicas = ["broker-2:9092"] }
            ]
        };

        public Task<IReadOnlyList<KafkaTopicSummary>> ListTopicsAsync(string connectionSessionId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<KafkaTopicSummary>>([Topic]);

        public Task<KafkaTopicSummary?> GetTopicAsync(string connectionSessionId, string topic, CancellationToken cancellationToken = default)
            => Task.FromResult<KafkaTopicSummary?>(Topic);
    }

    private sealed class FakeViewSessionService : IViewSessionService
    {
        public event EventHandler? StateChanged;

        public ViewSessionStatus Snapshot { get; private set; } = new();

        public Task<ViewSessionStatus> StartAsync(StartViewSessionRequest request, CancellationToken cancellationToken = default)
        {
            Snapshot = new ViewSessionStatus
            {
                IsRunning = true,
                ConnectionSessionId = request.ConnectionSessionId,
                Topic = request.Topic,
                OffsetMode = request.OffsetMode,
                ReceivedCount = 1,
                RecentMessages =
                [
                    new SteakMessageEnvelope
                    {
                        Topic = request.Topic,
                        Partition = 0,
                        Offset = 42,
                        ValueBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"hello":"world"}"""))
                    }
                ]
            };

            StateChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(Snapshot);
        }

        public Task StopAsync()
        {
            Snapshot = new ViewSessionStatus();
            StateChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
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

    private sealed class FakeConsumeExportService : IConsumeExportService
    {
        public event EventHandler? StateChanged;

        public ConsumeJobStatus Snapshot { get; private set; } = new();

        public Task<ConsumeJobStatus> StartAsync(CreateConsumeJobRequest request, CancellationToken cancellationToken = default)
        {
            Snapshot = new ConsumeJobStatus
            {
                IsRunning = true,
                ConnectionSessionId = request.ConnectionSessionId,
                Topic = request.Topic,
                GroupId = request.GroupId,
                ExportedCount = 3,
                LastDestination = "exports/Steak_orders_p0_o42_20260321T120000000_deadbeef.json"
            };

            StateChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(Snapshot);
        }

        public Task StopAsync()
        {
            Snapshot = new ConsumeJobStatus();
            StateChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMessagePublisher : IMessagePublisher
    {
        public Task<PublishResultInfo> PublishAsync(PublishEnvelopeRequest request, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request.ConnectionSessionId))
            {
                throw new InvalidOperationException("A connectionSessionId is required to publish a message.");
            }

            return Task.FromResult(new PublishResultInfo
            {
                Topic = request.Topic ?? request.Envelope.Topic ?? "orders",
                Partition = 0,
                Offset = 42,
                Status = "Persisted",
                TimestampUtc = DateTimeOffset.UtcNow
            });
        }
    }

    private sealed class FakeBatchPublishService : IBatchPublishService
    {
        public event EventHandler? StateChanged;

        public BatchPublishJobStatus Snapshot { get; private set; } = new();

        public Task<BatchPublishJobStatus> StartAsync(BatchPublishRequest request, CancellationToken cancellationToken = default)
        {
            Snapshot = new BatchPublishJobStatus { IsRunning = true };
            StateChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(Snapshot);
        }

        public Task StopAsync()
        {
            Snapshot = new BatchPublishJobStatus();
            StateChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }
}
