using Steak.Core.Contracts;

namespace Steak.Core.Services;

/// <summary>
/// Holds the single active Kafka connection session in memory. No persistence — session
/// lives only as long as the app process.
/// </summary>
internal sealed class ConnectionSessionService : IConnectionSessionService
{
    private readonly object _sync = new();
    private KafkaConnectionSettings? _settings;
    private string? _sessionId;
    private DateTimeOffset? _connectedAtUtc;

    public ConnectResponse Connect(ConnectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Settings);

        if (string.IsNullOrWhiteSpace(request.Settings.BootstrapServers))
        {
            throw new InvalidOperationException("Bootstrap servers are required to connect.");
        }

        lock (_sync)
        {
            _sessionId = Guid.NewGuid().ToString("N");
            _settings = request.Settings;
            _connectedAtUtc = DateTimeOffset.UtcNow;
        }

        return new ConnectResponse
        {
            ConnectionSessionId = _sessionId,
            BootstrapServers = request.Settings.BootstrapServers
        };
    }

    public void Disconnect()
    {
        lock (_sync)
        {
            _sessionId = null;
            _settings = null;
            _connectedAtUtc = null;
        }
    }

    public ConnectionSessionStatus GetStatus()
    {
        lock (_sync)
        {
            return new ConnectionSessionStatus
            {
                IsConnected = _sessionId is not null,
                ConnectionSessionId = _sessionId,
                BootstrapServers = _settings?.BootstrapServers,
                ConnectedAtUtc = _connectedAtUtc
            };
        }
    }

    public KafkaConnectionSettings GetActiveSettings(string connectionSessionId)
    {
        lock (_sync)
        {
            if (_sessionId is null || !string.Equals(_sessionId, connectionSessionId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("No active connection session matches the supplied id. Connect first.");
            }

            return _settings!;
        }
    }
}
