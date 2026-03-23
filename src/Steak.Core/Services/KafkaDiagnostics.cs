using System.Text.Json;
using Steak.Core.Contracts;

namespace Steak.Core.Services;

internal static class KafkaDiagnostics
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string FormatSettings(KafkaConnectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["bootstrapServers"] = settings.BootstrapServers,
            ["username"] = settings.Username,
            ["password"] = settings.Password,
            ["securityProtocol"] = settings.SecurityProtocol,
            ["saslMechanism"] = settings.SaslMechanism,
            ["clientId"] = settings.ClientId,
            ["sslCaPem"] = settings.SslCaPem,
            ["sslCertificatePem"] = settings.SslCertificatePem,
            ["sslKeyPem"] = settings.SslKeyPem,
            ["sslKeyPassword"] = settings.SslKeyPassword,
            ["advancedOverrides"] = settings.AdvancedOverrides
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static string FormatConfig(IReadOnlyDictionary<string, string> config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return JsonSerializer.Serialize(
            config.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            JsonOptions);
    }

    public static string FormatEnvelopeSummary(SteakMessageEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["topic"] = envelope.Topic,
            ["partition"] = envelope.Partition,
            ["offset"] = envelope.Offset,
            ["timestampUtc"] = envelope.TimestampUtc,
            ["connectionSessionId"] = envelope.ConnectionSessionId,
            ["headers"] = envelope.Headers.Count,
            ["keyBytes"] = GetDecodedLength(envelope.KeyBase64),
            ["valueBytes"] = GetDecodedLength(envelope.ValueBase64)
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static string FormatSource(BatchSourceOptions source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["transportKind"] = source.TransportKind.ToString(),
            ["fileSystemPath"] = source.FileSystem?.Path,
            ["s3Bucket"] = source.S3?.Bucket,
            ["s3Region"] = source.S3?.Region,
            ["s3Prefix"] = source.S3?.Prefix,
            ["s3Endpoint"] = source.S3?.Endpoint
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static string FormatDestination(BatchDestinationOptions destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["transportKind"] = destination.TransportKind.ToString(),
            ["fileSystemPath"] = destination.FileSystem?.Path,
            ["s3Bucket"] = destination.S3?.Bucket,
            ["s3Region"] = destination.S3?.Region,
            ["s3Prefix"] = destination.S3?.Prefix,
            ["s3Endpoint"] = destination.S3?.Endpoint
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static int? GetDecodedLength(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return 0;
        }

        try
        {
            return Convert.FromBase64String(base64).Length;
        }
        catch
        {
            return null;
        }
    }
}
