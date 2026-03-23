using Microsoft.Extensions.Logging;
using Steak.Core.Contracts;

namespace Steak.Core.Services;

/// <summary>
/// Holds multiple active Kafka connection sessions in memory. No persistence ג€” sessions
/// live only as long as the app process.
/// </summary>
internal sealed class ConnectionSessionService(ILogger<ConnectionSessionService>? logger = null) : IConnectionSessionService
{
    private readonly object _sync = new();
    private readonly Dictionary<string, (KafkaConnectionSettings Settings, DateTimeOffset ConnectedAtUtc)> _sessions = new(StringComparer.Ordinal);

    public ConnectResponse Connect(ConnectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Settings);

        if (logger?.IsEnabled(LogLevel.Debug) == true)
        {
            logger.LogDebug(
                "Received Kafka connection request with raw settings: {ConnectionSettings}",
                KafkaDiagnostics.FormatSettings(request.Settings));
        }

        request.Settings.BootstrapServers = NormalizeBootstrapServers(request.Settings.BootstrapServers);

        if (string.IsNullOrWhiteSpace(request.Settings.BootstrapServers))
        {
            throw new InvalidOperationException("Bootstrap servers are required to connect.");
        }

        if (string.IsNullOrWhiteSpace(request.Settings.SecurityProtocol))
        {
            request.Settings.SecurityProtocol = "SaslPlaintext";
        }

        var requiresSasl = UsesSasl(request.Settings.SecurityProtocol);

        if (requiresSasl && string.IsNullOrWhiteSpace(request.Settings.Username))
        {
            throw new InvalidOperationException("Username is required to connect.");
        }

        if (requiresSasl && string.IsNullOrWhiteSpace(request.Settings.Password))
        {
            throw new InvalidOperationException("Password is required to connect.");
        }

        request.Settings.Username = string.IsNullOrWhiteSpace(request.Settings.Username)
            ? null
            : request.Settings.Username.Trim();
        request.Settings.Password = string.IsNullOrWhiteSpace(request.Settings.Password)
            ? null
            : request.Settings.Password.Trim();

        // Default client id to username when not explicitly set and a username exists.
        if (string.IsNullOrWhiteSpace(request.Settings.ClientId)
            && !string.IsNullOrWhiteSpace(request.Settings.Username))
        {
            request.Settings.ClientId = request.Settings.Username.Trim();
        }

        if (requiresSasl && string.IsNullOrWhiteSpace(request.Settings.SaslMechanism))
        {
            request.Settings.SaslMechanism = "ScramSha512";
        }

        if (!requiresSasl)
        {
            request.Settings.Username = null;
            request.Settings.Password = null;
            request.Settings.SaslMechanism = string.IsNullOrWhiteSpace(request.Settings.SaslMechanism)
                ? string.Empty
                : request.Settings.SaslMechanism.Trim();
        }

        if (requiresSasl)
        {
            logger?.LogInformation(
                "Creating Kafka connection session for {BootstrapServers} as user {Username}",
                request.Settings.BootstrapServers,
                request.Settings.Username);
        }
        else
        {
            logger?.LogInformation(
                "Creating Kafka connection session for {BootstrapServers} without SASL credentials",
                request.Settings.BootstrapServers);
        }

        if (logger?.IsEnabled(LogLevel.Debug) == true)
        {
            logger.LogDebug(
                "Normalized Kafka connection settings ready for session creation: {ConnectionSettings}",
                KafkaDiagnostics.FormatSettings(request.Settings));
        }

        var sessionId = Guid.NewGuid().ToString("N");
        lock (_sync)
        {
            _sessions[sessionId] = (request.Settings, DateTimeOffset.UtcNow);
        }

        logger?.LogInformation(
            "Kafka connection session {SessionId} created for {BootstrapServers}",
            sessionId,
            request.Settings.BootstrapServers);

        return new ConnectResponse
        {
            ConnectionSessionId = sessionId,
            BootstrapServers = request.Settings.BootstrapServers
        };
    }

    public void Disconnect()
    {
        var disconnected = 0;
        lock (_sync)
        {
            disconnected = _sessions.Count;
            _sessions.Clear();
        }

        logger?.LogInformation("Disconnected {SessionCount} Kafka session(s)", disconnected);
    }

    public void Disconnect(string connectionSessionId)
    {
        var removed = false;
        lock (_sync)
        {
            removed = _sessions.Remove(connectionSessionId);
        }

        if (removed)
        {
            logger?.LogInformation("Disconnected Kafka session {SessionId}", connectionSessionId);
        }
        else
        {
            logger?.LogDebug("Kafka disconnect requested for unknown session {SessionId}", connectionSessionId);
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
                if (logger?.IsEnabled(LogLevel.Debug) == true)
                {
                    logger.LogDebug(
                        "Resolved active Kafka session {SessionId} with settings {ConnectionSettings}",
                        connectionSessionId,
                        KafkaDiagnostics.FormatSettings(entry.Settings));
                }

                return entry.Settings;
            }

            logger?.LogWarning("No active Kafka session found for id {SessionId}", connectionSessionId);
            throw new InvalidOperationException("No active connection session matches the supplied id. Connect first.");
        }
    }

    private static string NormalizeBootstrapServers(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(
            ",",
            value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool UsesSasl(string? securityProtocol)
    {
        if (string.IsNullOrWhiteSpace(securityProtocol))
        {
            return true;
        }

        var normalized = new string(securityProtocol.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return normalized is "SASLPLAINTEXT" or "SASLSSL";
    }
}
