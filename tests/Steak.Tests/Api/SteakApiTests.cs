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

    private static KafkaConnectionSettings CreateConnectionSettings(string bootstrapServers)
    {
        return new KafkaConnectionSettings
        {
            BootstrapServers = bootstrapServers,
            Username = "username",
            Password = "password",
            SecurityProtocol = "SaslPlaintext",
            SaslMechanism = "ScramSha256"
        };
    }

    [Fact]
    public async Task Connect_ReturnsSessionId()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/connection", new ConnectRequest
        {
            Settings = CreateConnectionSettings("localhost:9092")
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

        var connectResponse = await client.PostAsJsonAsync("/api/connection", new ConnectRequest
        {
            Settings = CreateConnectionSettings("localhost:9092")
        });
        var session = await connectResponse.Content.ReadFromJsonAsync<ConnectResponse>();

        var response = await client.GetFromJsonAsync<List<KafkaTopicSummary>>($"/api/topics?connectionSessionId={session!.ConnectionSessionId}");

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
    public async Task Health_ReturnsOk()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Connect_MultipleConnections_ReturnsDistinctIds()
    {
        using var client = _factory.CreateClient();

        var r1 = await client.PostAsJsonAsync("/api/connection", new ConnectRequest { Settings = CreateConnectionSettings("broker-a:9092") });
        var r2 = await client.PostAsJsonAsync("/api/connection", new ConnectRequest { Settings = CreateConnectionSettings("broker-b:9092") });

        r1.EnsureSuccessStatusCode();
        r2.EnsureSuccessStatusCode();

        var c1 = await r1.Content.ReadFromJsonAsync<ConnectResponse>();
        var c2 = await r2.Content.ReadFromJsonAsync<ConnectResponse>();

        Assert.NotNull(c1);
        Assert.NotNull(c2);
        Assert.NotEqual(c1.ConnectionSessionId, c2.ConnectionSessionId);
    }

    [Fact]
    public async Task GetAllConnections_ReturnsAllSessions()
    {
        using var client = _factory.CreateClient();

        await client.PostAsJsonAsync("/api/connection", new ConnectRequest { Settings = CreateConnectionSettings("broker-a:9092") });
        await client.PostAsJsonAsync("/api/connection", new ConnectRequest { Settings = CreateConnectionSettings("broker-b:9092") });

        var all = await client.GetFromJsonAsync<List<ConnectionSessionStatus>>("/api/connection/all");

        Assert.NotNull(all);
        Assert.True(all.Count >= 2);
    }

    [Fact]
    public async Task DisconnectById_RemovesSpecificSession()
    {
        using var client = _factory.CreateClient();

        var r1 = await client.PostAsJsonAsync("/api/connection", new ConnectRequest { Settings = CreateConnectionSettings("broker-a:9092") });
        r1.EnsureSuccessStatusCode();
        var c1 = await r1.Content.ReadFromJsonAsync<ConnectResponse>();
        Assert.NotNull(c1);

        var disconnect = await client.DeleteAsync($"/api/connection/{c1.ConnectionSessionId}");

        Assert.Equal(HttpStatusCode.NoContent, disconnect.StatusCode);
    }

    [Fact]
    public async Task DisconnectAll_ClearsAllSessions()
    {
        using var client = _factory.CreateClient();

        await client.PostAsJsonAsync("/api/connection", new ConnectRequest { Settings = CreateConnectionSettings("broker-a:9092") });

        var disconnect = await client.DeleteAsync("/api/connection");
        Assert.Equal(HttpStatusCode.NoContent, disconnect.StatusCode);

        var status = await client.GetFromJsonAsync<ConnectionSessionStatus>("/api/connection");
        Assert.NotNull(status);
        Assert.False(status.IsConnected);
    }

    [Fact]
    public async Task Topics_WithInvalidSession_ReturnsBadRequest()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/topics?connectionSessionId=nonexistent");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("No active connection session", payload);
    }

    [Fact]
    public async Task SingleTopic_WithInvalidSession_ReturnsBadRequest()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/topics/orders?connectionSessionId=nonexistent");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ViewSession_StopWithoutStart_ReturnsNoContent()
    {
        using var client = _factory.CreateClient();

        var response = await client.DeleteAsync("/api/view-sessions");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
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
                services.AddSingleton<ITopicBrowserService>(sp =>
                    new FakeTopicBrowserService(sp.GetRequiredService<IConnectionSessionService>()));
                services.AddSingleton<IViewSessionService, FakeViewSessionService>();
                services.AddSingleton<IConsumeExportService, FakeConsumeExportService>();
                services.AddSingleton<IMessagePublisher, FakeMessagePublisher>();
                services.AddSingleton<IBatchPublishService, FakeBatchPublishService>();
            });
        }
    }

    private sealed class FakeConnectionSessionService : IConnectionSessionService
    {
        private readonly Dictionary<string, KafkaConnectionSettings> _sessions = new(StringComparer.Ordinal);

        public ConnectResponse Connect(ConnectRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Settings.BootstrapServers))
            {
                throw new InvalidOperationException("Bootstrap servers are required to connect.");
            }

            if (string.IsNullOrWhiteSpace(request.Settings.Username))
            {
                throw new InvalidOperationException("Username is required to connect.");
            }

            if (string.IsNullOrWhiteSpace(request.Settings.Password))
            {
                throw new InvalidOperationException("Password is required to connect.");
            }

            var id = Guid.NewGuid().ToString("N");
            _sessions[id] = request.Settings;
            return new ConnectResponse
            {
                ConnectionSessionId = id,
                BootstrapServers = request.Settings.BootstrapServers,
                ConnectionName = request.Settings.ConnectionName
            };
        }

        public void Disconnect() => _sessions.Clear();

        public void Disconnect(string connectionSessionId) => _sessions.Remove(connectionSessionId);

        public ConnectionSessionStatus GetStatus()
        {
            var first = _sessions.FirstOrDefault();
            return first.Key is null
                ? new ConnectionSessionStatus()
                : new ConnectionSessionStatus
                {
                    IsConnected = true,
                    ConnectionSessionId = first.Key,
                    BootstrapServers = first.Value.BootstrapServers,
                    ConnectionName = first.Value.ConnectionName,
                    Username = first.Value.Username
                };
        }

        public IReadOnlyList<ConnectionSessionStatus> GetAllSessions() =>
            _sessions.Select(kv => new ConnectionSessionStatus
            {
                IsConnected = true,
                ConnectionSessionId = kv.Key,
                BootstrapServers = kv.Value.BootstrapServers,
                ConnectionName = kv.Value.ConnectionName,
                Username = kv.Value.Username
            }).ToList();

        public KafkaConnectionSettings GetActiveSettings(string connectionSessionId) =>
            _sessions.TryGetValue(connectionSessionId, out var s) ? s : throw new InvalidOperationException("No active connection session matches the supplied id. Connect first.");
    }

    private sealed class FakeTopicBrowserService(IConnectionSessionService sessionService) : ITopicBrowserService
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
        {
            sessionService.GetActiveSettings(connectionSessionId); // validates session exists
            return Task.FromResult<IReadOnlyList<KafkaTopicSummary>>([Topic]);
        }

        public Task<KafkaTopicSummary?> GetTopicAsync(string connectionSessionId, string topic, CancellationToken cancellationToken = default)
        {
            sessionService.GetActiveSettings(connectionSessionId); // validates session exists
            return Task.FromResult<KafkaTopicSummary?>(Topic);
        }
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
