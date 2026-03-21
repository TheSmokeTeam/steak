using System.Globalization;
using System.Text;
using Steak.Core.Contracts;

namespace Steak.Core.Services;

internal sealed class FileNameFactory : IFileNameFactory
{
    public string CreateMessageFileName(SteakMessageEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var topic = SanitizeTopic(envelope.Topic);
        var partition = envelope.Partition?.ToString(CultureInfo.InvariantCulture) ?? "na";
        var offset = envelope.Offset?.ToString(CultureInfo.InvariantCulture) ?? "na";
        var timestamp = (envelope.TimestampUtc ?? envelope.CapturedAtUtc ?? DateTimeOffset.UtcNow)
            .UtcDateTime
            .ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture);
        var shortId = Guid.NewGuid().ToString("n")[..8];

        return $"Steak_{topic}_p{partition}_o{offset}_{timestamp}_{shortId}.json";
    }

    internal static string SanitizeTopic(string? topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return "unknown-topic";
        }

        var builder = new StringBuilder(topic.Length);
        foreach (var character in topic)
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '-' or '_' or '.'
                ? character
                : '-');
        }

        var sanitized = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown-topic" : sanitized;
    }
}
