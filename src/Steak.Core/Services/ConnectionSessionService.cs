using Steak.Core.Contracts;

namespace Steak.Core.Services;

/// <summary>
/// Holds multiple active Kafka connection sessions in memory. No persistence — sessions
/// live only as long as the app process.
/// </summary>
internal sealed class ConnectionSessionService : IConnectionSessionService
{
    private readonly object _sync = new();
    private readonly Dictionary<string, (KafkaConnectionSettings Settings, DateTimeOffset ConnectedAtUtc)> _sessions = new(StringComparer.Ordinal);

    public ConnectResponse Connect(ConnectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Settings);

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

        // Default client id to username when not explicitly set.
        if (string.IsNullOrWhiteSpace(request.Settings.ClientId))
        {
            request.Settings.ClientId = request.Settings.Username.Trim();
        }

        if (string.IsNullOrWhiteSpace(request.Settings.SecurityProtocol))
        {
            request.Settings.SecurityProtocol = "SaslPlaintext";
        }

        if (string.IsNullOrWhiteSpace(request.Settings.SaslMechanism))
        {
            request.Settings.SaslMechanism = "ScramSha512";
        }

        var sessionId = Guid.NewGuid().ToString("N");
        lock (_sync)
        {
            _sessions[sessionId] = (request.Settings, DateTimeOffset.UtcNow);
        }

        return new ConnectResponse
        {
            ConnectionSessionId = sessionId,
            BootstrapServers = request.Settings.BootstrapServers
        };
    }

    public void Disconnect()
    {
        lock (_sync)
        {
            _sessions.Clear();
        }
    }

    public void Disconnect(string connectionSessionId)
    {
        lock (_sync)
        {
            _sessions.Remove(connectionSessionId);
        }
    }

    public ConnectionSessionStatus GetStatus()
    {
        lock (_sync)
        {
            var first = _sessions.FirstOrDefault();
            if (first.Key is null)
            {
                return new ConnectionSessionStatus();
            }

            return new ConnectionSessionStatus
            {
                IsConnected = true,
                ConnectionSessionId = first.Key,
                BootstrapServers = first.Value.Settings.BootstrapServers,
                Username = first.Value.Settings.Username,
                ConnectedAtUtc = first.Value.ConnectedAtUtc
            };
        }
    }

    public IReadOnlyList<ConnectionSessionStatus> GetAllSessions()
    {
        lock (_sync)
        {
            return _sessions
                .Select(kv => new ConnectionSessionStatus
                {
                    IsConnected = true,
                    ConnectionSessionId = kv.Key,
                    BootstrapServers = kv.Value.Settings.BootstrapServers,
                    Username = kv.Value.Settings.Username,
                    ConnectedAtUtc = kv.Value.ConnectedAtUtc
                })
                .ToList();
        }
    }

    public KafkaConnectionSettings GetActiveSettings(string connectionSessionId)
    {
        lock (_sync)
        {
            if (_sessions.TryGetValue(connectionSessionId, out var entry))
            {
                return entry.Settings;
            }

            throw new InvalidOperationException("No active connection session matches the supplied id. Connect first.");
        }
    }
}
