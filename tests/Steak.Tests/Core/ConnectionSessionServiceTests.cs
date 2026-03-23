using Steak.Core.Contracts;
using Steak.Core.Services;

namespace Steak.Tests.Core;

public sealed class ConnectionSessionServiceTests
{
    private readonly ConnectionSessionService _service = new();

    private ConnectResponse ConnectDefault(string bootstrap = "localhost:9092", string user = "admin", string pass = "secret")
    {
        return _service.Connect(new ConnectRequest
        {
            Settings = new KafkaConnectionSettings
            {
                BootstrapServers = bootstrap,
                Username = user,
                Password = pass
            }
        });
    }

    [Fact]
    public void Connect_ReturnsUniqueSessionId()
    {
        var first = ConnectDefault();
        var second = ConnectDefault();

        Assert.NotEqual(first.ConnectionSessionId, second.ConnectionSessionId);
        Assert.False(string.IsNullOrWhiteSpace(first.ConnectionSessionId));
    }

    [Fact]
    public void Connect_DefaultsClientIdToUsername()
    {
        ConnectDefault(user: "myuser");

        var sessions = _service.GetAllSessions();
        var settings = _service.GetActiveSettings(sessions[0].ConnectionSessionId!);

        Assert.Equal("myuser", settings.ClientId);
    }

    [Fact]
    public void Connect_DefaultsSecurityProtocolAndSaslMechanism()
    {
        var response = ConnectDefault();

        var settings = _service.GetActiveSettings(response.ConnectionSessionId);

        Assert.Equal("SaslPlaintext", settings.SecurityProtocol);
        Assert.Equal("ScramSha512", settings.SaslMechanism);
    }

    [Fact]
    public void Connect_NormalizesBootstrapServerLists()
    {
        var response = ConnectDefault(" broker-a:9092, broker-b:9092 ,,broker-c:9092 ");

        var settings = _service.GetActiveSettings(response.ConnectionSessionId);

        Assert.Equal("broker-a:9092,broker-b:9092,broker-c:9092", settings.BootstrapServers);
        Assert.Equal("broker-a:9092,broker-b:9092,broker-c:9092", response.BootstrapServers);
    }

    [Fact]
    public void Connect_PreservesExplicitClientId()
    {
        _service.Connect(new ConnectRequest
        {
            Settings = new KafkaConnectionSettings
            {
                BootstrapServers = "localhost:9092",
                Username = "admin",
                Password = "secret",
                ClientId = "custom-client"
            }
        });

        var sessions = _service.GetAllSessions();
        var settings = _service.GetActiveSettings(sessions[0].ConnectionSessionId!);

        Assert.Equal("custom-client", settings.ClientId);
    }

    [Fact]
    public void Connect_ThrowsWhenBootstrapServersBlank()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _service.Connect(new ConnectRequest
            {
                Settings = new KafkaConnectionSettings { BootstrapServers = "  ", Username = "u", Password = "p" }
            }));

        Assert.Contains("Bootstrap servers", ex.Message);
    }

    [Fact]
    public void Connect_ThrowsWhenUsernameBlank()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _service.Connect(new ConnectRequest
            {
                Settings = new KafkaConnectionSettings { BootstrapServers = "localhost:9092", Username = "", Password = "p" }
            }));

        Assert.Contains("Username", ex.Message);
    }

    [Fact]
    public void Connect_ThrowsWhenPasswordBlank()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _service.Connect(new ConnectRequest
            {
                Settings = new KafkaConnectionSettings { BootstrapServers = "localhost:9092", Username = "u", Password = "" }
            }));

        Assert.Contains("Password", ex.Message);
    }

    [Fact]
    public void Connect_AllowsPlaintextWithoutCredentials()
    {
        var response = _service.Connect(new ConnectRequest
        {
            Settings = new KafkaConnectionSettings
            {
                BootstrapServers = "localhost:9092",
                SecurityProtocol = "Plaintext",
                SaslMechanism = "Plain"
            }
        });

        var settings = _service.GetActiveSettings(response.ConnectionSessionId);

        Assert.Equal("Plaintext", settings.SecurityProtocol);
        Assert.Null(settings.Username);
        Assert.Null(settings.Password);
        Assert.Equal("Plain", settings.SaslMechanism);
    }

    [Fact]
    public void Connect_AllowsSslWithoutCredentials()
    {
        var response = _service.Connect(new ConnectRequest
        {
            Settings = new KafkaConnectionSettings
            {
                BootstrapServers = "localhost:9092",
                SecurityProtocol = "Ssl",
                SslCaPem = "ca-pem"
            }
        });

        var settings = _service.GetActiveSettings(response.ConnectionSessionId);

        Assert.Equal("Ssl", settings.SecurityProtocol);
        Assert.Null(settings.Username);
        Assert.Null(settings.Password);
        Assert.Equal("ca-pem", settings.SslCaPem);
    }

    [Fact]
    public void Connect_DefaultsClientIdFromUsernameBeforeDroppingPlaintextCredentials()
    {
        var response = _service.Connect(new ConnectRequest
        {
            Settings = new KafkaConnectionSettings
            {
                BootstrapServers = "localhost:9092",
                SecurityProtocol = "Plaintext",
                Username = "local-user",
                Password = "ignored"
            }
        });

        var settings = _service.GetActiveSettings(response.ConnectionSessionId);

        Assert.Equal("local-user", settings.ClientId);
        Assert.Null(settings.Username);
        Assert.Null(settings.Password);
    }

    [Fact]
    public void Connect_ThrowsWhenRequestNull()
    {
        Assert.Throws<ArgumentNullException>(() => _service.Connect(null!));
    }

    [Fact]
    public void Connect_ThrowsWhenSettingsNull()
    {
        Assert.Throws<ArgumentNullException>(() => _service.Connect(new ConnectRequest { Settings = null! }));
    }

    [Fact]
    public void GetAllSessions_ReturnsMultipleSessions()
    {
        var r1 = ConnectDefault("broker-a:9092");
        var r2 = ConnectDefault("broker-b:9092");
        var r3 = ConnectDefault("broker-c:9092");

        var all = _service.GetAllSessions();

        Assert.Equal(3, all.Count);
        Assert.Contains(all, s => s.ConnectionSessionId == r1.ConnectionSessionId);
        Assert.Contains(all, s => s.ConnectionSessionId == r2.ConnectionSessionId);
        Assert.Contains(all, s => s.ConnectionSessionId == r3.ConnectionSessionId);
        Assert.All(all, s => Assert.True(s.IsConnected));
    }

    [Fact]
    public void GetAllSessions_ReturnsCorrectBootstrapServers()
    {
        ConnectDefault("broker-a:9092");
        ConnectDefault("broker-b:9092");

        var all = _service.GetAllSessions();

        Assert.Contains(all, s => s.BootstrapServers == "broker-a:9092");
        Assert.Contains(all, s => s.BootstrapServers == "broker-b:9092");
    }

    [Fact]
    public void GetAllSessions_ReturnsEmptyWhenNoSessions()
    {
        var all = _service.GetAllSessions();

        Assert.Empty(all);
    }

    [Fact]
    public void DisconnectById_RemovesSingleSession()
    {
        var r1 = ConnectDefault("broker-a:9092");
        var r2 = ConnectDefault("broker-b:9092");

        _service.Disconnect(r1.ConnectionSessionId!);

        var remaining = _service.GetAllSessions();
        Assert.Single(remaining);
        Assert.Equal(r2.ConnectionSessionId, remaining[0].ConnectionSessionId);
    }

    [Fact]
    public void DisconnectById_DoesNotThrowForUnknownId()
    {
        ConnectDefault();

        _service.Disconnect("nonexistent-id");

        Assert.Single(_service.GetAllSessions());
    }

    [Fact]
    public void DisconnectAll_ClearsAllSessions()
    {
        ConnectDefault("broker-a:9092");
        ConnectDefault("broker-b:9092");
        ConnectDefault("broker-c:9092");

        _service.Disconnect();

        Assert.Empty(_service.GetAllSessions());
    }

    [Fact]
    public void GetActiveSettings_ReturnsCorrectSettings()
    {
        var response = _service.Connect(new ConnectRequest
        {
            Settings = new KafkaConnectionSettings
            {
                BootstrapServers = "broker-x:9092",
                Username = "testuser",
                Password = "testpass",
                SecurityProtocol = "SaslSsl",
                SaslMechanism = "ScramSha512"
            }
        });

        var settings = _service.GetActiveSettings(response.ConnectionSessionId!);

        Assert.Equal("broker-x:9092", settings.BootstrapServers);
        Assert.Equal("testuser", settings.Username);
        Assert.Equal("testpass", settings.Password);
        Assert.Equal("SaslSsl", settings.SecurityProtocol);
        Assert.Equal("ScramSha512", settings.SaslMechanism);
    }

    [Fact]
    public void GetActiveSettings_ThrowsForInvalidSessionId()
    {
        ConnectDefault();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            _service.GetActiveSettings("nonexistent-session"));

        Assert.Contains("No active connection session", ex.Message);
    }

    [Fact]
    public void GetActiveSettings_ThrowsAfterSessionDisconnected()
    {
        var response = ConnectDefault();

        _service.Disconnect(response.ConnectionSessionId!);

        Assert.Throws<InvalidOperationException>(() =>
            _service.GetActiveSettings(response.ConnectionSessionId!));
    }

    [Fact]
    public void GetStatus_ReturnsFirstSession()
    {
        var r1 = ConnectDefault("broker-first:9092");
        ConnectDefault("broker-second:9092");

        var status = _service.GetStatus();

        Assert.True(status.IsConnected);
        Assert.NotNull(status.ConnectionSessionId);
        Assert.NotNull(status.BootstrapServers);
    }

    [Fact]
    public void GetStatus_ReturnsDisconnectedWhenEmpty()
    {
        var status = _service.GetStatus();

        Assert.False(status.IsConnected);
        Assert.Null(status.ConnectionSessionId);
    }

    [Fact]
    public void Connect_SetsConnectedAtUtc()
    {
        var before = DateTimeOffset.UtcNow;
        ConnectDefault();
        var after = DateTimeOffset.UtcNow;

        var session = _service.GetAllSessions()[0];

        Assert.True(session.ConnectedAtUtc >= before);
        Assert.True(session.ConnectedAtUtc <= after);
    }

    [Fact]
    public void Connect_ReturnsBootstrapServersInResponse()
    {
        var response = ConnectDefault("my-broker:9092");

        Assert.Equal("my-broker:9092", response.BootstrapServers);
    }
}
