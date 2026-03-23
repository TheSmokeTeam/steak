using Microsoft.Extensions.Logging;
using Steak.Core.Contracts;

namespace Steak.Core.Services;

internal sealed class KafkaConfigurationService(ILogger<KafkaConfigurationService>? logger = null) : IKafkaConfigurationService
{
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "sasl.password",
        "ssl.key.password",
        "ssl.key.pem",
        "ssl.certificate.pem",
        "ssl.ca.pem"
    };

    public Dictionary<string, string> BuildConfig(
        KafkaConnectionSettings settings,
        KafkaClientKind clientKind,
        IReadOnlyDictionary<string, string>? overrides = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (logger?.IsEnabled(LogLevel.Debug) == true)
        {
            logger.LogDebug(
                "Building Kafka {ClientKind} config from settings: {ConnectionSettings}",
                clientKind,
                KafkaDiagnostics.FormatSettings(settings));
        }

        if (string.IsNullOrWhiteSpace(settings.BootstrapServers))
        {
            throw new InvalidOperationException("Bootstrap servers are required.");
        }

        var bootstrapServers = NormalizeBootstrapServers(settings.BootstrapServers);
        if (string.IsNullOrWhiteSpace(bootstrapServers))
        {
            throw new InvalidOperationException("Bootstrap servers are required.");
        }

        var config = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bootstrap.servers"] = bootstrapServers
        };

        AddIfPresent(config, "client.id", settings.ClientId);
        AddIfPresent(config, "security.protocol", NormalizeSecurityProtocol(settings.SecurityProtocol));
        AddIfPresent(config, "sasl.mechanism", NormalizeSaslMechanism(settings.SaslMechanism));
        AddIfPresent(config, "sasl.username", settings.Username);
        AddIfPresent(config, "sasl.password", settings.Password);
        AddIfPresent(config, "ssl.ca.pem", settings.SslCaPem);
        AddIfPresent(config, "ssl.certificate.pem", settings.SslCertificatePem);
        AddIfPresent(config, "ssl.key.pem", settings.SslKeyPem);
        AddIfPresent(config, "ssl.key.password", settings.SslKeyPassword);

        // Advanced overrides from the connection form merge over the base fields.
        foreach (var pair in settings.AdvancedOverrides.Where(pair => !string.IsNullOrWhiteSpace(pair.Key)))
        {
            config[pair.Key] = pair.Value;
        }

        // Caller-supplied overrides merge last.
        if (overrides is not null)
        {
            foreach (var pair in overrides.Where(pair => !string.IsNullOrWhiteSpace(pair.Key)))
            {
                config[pair.Key] = pair.Value;
            }
        }

        if (logger?.IsEnabled(LogLevel.Debug) == true)
        {
            logger.LogDebug(
                "Built Kafka {ClientKind} config: {KafkaConfig}",
                clientKind,
                KafkaDiagnostics.FormatConfig(config));
        }

        return config;
    }

    public IReadOnlyDictionary<string, string> GetMaskedConfig(
        KafkaConnectionSettings settings,
        KafkaClientKind clientKind,
        IReadOnlyDictionary<string, string>? overrides = null)
    {
        var config = BuildConfig(settings, clientKind, overrides);
        return config.ToDictionary(pair => pair.Key, pair => Mask(pair.Key, pair.Value), StringComparer.OrdinalIgnoreCase);
    }

    private static void AddIfPresent(IDictionary<string, string> target, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[key] = value.Trim();
        }
    }

    private static string? NormalizeSecurityProtocol(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        var token = NormalizeToken(trimmed);

        return token switch
        {
            "PLAINTEXT" => "plaintext",
            "SSL" => "ssl",
            "SASLPLAINTEXT" => "sasl_plaintext",
            "SASLSSL" => "sasl_ssl",
            _ => trimmed
        };
    }

    private static string? NormalizeSaslMechanism(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        var token = NormalizeToken(trimmed);

        return token switch
        {
            "GSSAPI" => "GSSAPI",
            "PLAIN" => "PLAIN",
            "SCRAMSHA256" => "SCRAM-SHA-256",
            "SCRAMSHA512" => "SCRAM-SHA-512",
            "OAUTHBEARER" => "OAUTHBEARER",
            _ => trimmed
        };
    }

    private static string NormalizeToken(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    }

    private static string Mask(string key, string value)
    {
        if (!SensitiveKeys.Contains(key))
        {
            return value;
        }

        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= 8 ? new string('*', value.Length) : $"{value[..2]}***{value[^2..]}";
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
}
