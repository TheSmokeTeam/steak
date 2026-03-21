using Steak.Core.Contracts;

namespace Steak.Core.Services;

internal sealed class MessageEnvelopeFactory(IMessagePreviewService previewService) : IMessageEnvelopeFactory
{
    public SteakMessageEnvelope NormalizeForPublish(SteakMessageEnvelope envelope, string? defaultSessionId, string? defaultTopic)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var normalized = new SteakMessageEnvelope
        {
            App = string.IsNullOrWhiteSpace(envelope.App) ? "Steak" : envelope.App.Trim(),
            CapturedAtUtc = envelope.CapturedAtUtc,
            ConnectionSessionId = string.IsNullOrWhiteSpace(envelope.ConnectionSessionId) ? defaultSessionId : envelope.ConnectionSessionId?.Trim(),
            Topic = string.IsNullOrWhiteSpace(defaultTopic) ? envelope.Topic?.Trim() : defaultTopic.Trim(),
            Partition = envelope.Partition,
            Offset = envelope.Offset,
            TimestampUtc = envelope.TimestampUtc,
            TimestampType = string.IsNullOrWhiteSpace(envelope.TimestampType) ? null : envelope.TimestampType.Trim(),
            KeyBase64 = string.IsNullOrWhiteSpace(envelope.KeyBase64) ? null : envelope.KeyBase64.Trim(),
            ValueBase64 = envelope.ValueBase64?.Trim() ?? string.Empty,
            Headers = envelope.Headers?.Where(header => !string.IsNullOrWhiteSpace(header.Key)).Select(header => new SteakMessageHeader
            {
                Key = header.Key.Trim(),
                ValueBase64 = string.IsNullOrWhiteSpace(header.ValueBase64) ? null : header.ValueBase64.Trim(),
                Utf8Preview = header.Utf8Preview,
                HexPreview = header.HexPreview,
                IsUtf8 = header.IsUtf8,
                IsTruncated = header.IsTruncated,
                DecodeError = header.DecodeError
            }).ToList() ?? []
        };

        normalized.Preview = previewService.CreatePreview(normalized.KeyBase64, normalized.ValueBase64);
        return normalized;
    }
}
