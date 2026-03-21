using System.Text;
using System.Text.Json;
using Steak.Core.Contracts;

namespace Steak.Core.Services;

internal sealed class MessagePreviewService : IMessagePreviewService
{
    private const int MaxPreviewChars = 16_384;
    private const int MaxHexChars = 8_192;

    public MessagePreview CreatePreview(string? keyBase64, string valueBase64)
    {
        var keyResult = DecodePayload(keyBase64);
        var valueResult = DecodePayload(valueBase64);

        return new MessagePreview
        {
            KeyLength = keyResult.Length,
            KeyIsUtf8 = keyResult.IsUtf8,
            KeyUtf8Preview = keyResult.TextPreview,
            KeyHexPreview = keyResult.HexPreview,
            KeyHexTruncated = keyResult.HexTruncated,
            KeyDecodeError = keyResult.Error,
            ValueLength = valueResult.Length,
            ValueIsUtf8 = valueResult.IsUtf8,
            ValueIsJson = valueResult.IsJson,
            ValueUtf8Preview = valueResult.TextPreview,
            ValuePrettyJson = valueResult.JsonPreview,
            ValueHexPreview = valueResult.HexPreview,
            ValueHexTruncated = valueResult.HexTruncated,
            ValueDecodeError = valueResult.Error
        };
    }

    public SteakMessageHeader CreateHeaderPreview(string key, byte[]? value)
    {
        var bytes = value ?? [];
        var base64 = bytes.Length == 0 ? null : Convert.ToBase64String(bytes);
        var text = TryDecodeUtf8(bytes, out var decoded) ? Clip(decoded) : null;
        var hex = BuildHexPreview(bytes, out var truncated);

        return new SteakMessageHeader
        {
            Key = key,
            ValueBase64 = base64,
            Utf8Preview = text,
            HexPreview = hex,
            IsUtf8 = text is not null,
            IsTruncated = truncated
        };
    }

    private static DecodeResult DecodePayload(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return DecodeResult.Empty;
        }

        try
        {
            var bytes = Convert.FromBase64String(base64);
            var isUtf8 = TryDecodeUtf8(bytes, out var decodedText);
            var clippedText = isUtf8 ? Clip(decodedText) : null;
            // JSON preview is derived from the exact UTF-8 bytes and never replaces the preserved base64 payload.
            var prettyJson = isUtf8 ? TryFormatJson(decodedText) : null;

            return new DecodeResult(
                bytes.Length,
                isUtf8,
                prettyJson is not null,
                clippedText,
                prettyJson,
                BuildHexPreview(bytes, out var truncated),
                truncated,
                null);
        }
        catch (FormatException exception)
        {
            return new DecodeResult(0, false, false, null, null, null, false, exception.Message);
        }
    }

    private static bool TryDecodeUtf8(byte[] bytes, out string decoded)
    {
        try
        {
            decoded = new UTF8Encoding(false, true).GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            decoded = string.Empty;
            return false;
        }
    }

    private static string? TryFormatJson(string input)
    {
        try
        {
            using var document = JsonDocument.Parse(input);
            return Clip(JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Clip(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= MaxPreviewChars
            ? value
            : $"{value[..MaxPreviewChars]} ...";
    }

    private static string BuildHexPreview(byte[] bytes, out bool truncated)
    {
        if (bytes.Length == 0)
        {
            truncated = false;
            return string.Empty;
        }

        var builder = new StringBuilder(Math.Min(bytes.Length * 3, MaxHexChars + 16));
        truncated = false;
        for (var index = 0; index < bytes.Length; index++)
        {
            if (builder.Length >= MaxHexChars)
            {
                truncated = true;
                break;
            }

            if (index > 0)
            {
                builder.Append(' ');
            }

            builder.Append(bytes[index].ToString("X2"));
        }

        if (truncated)
        {
            builder.Append(" ...");
        }

        return builder.ToString();
    }

    private sealed record DecodeResult(
        int Length,
        bool IsUtf8,
        bool IsJson,
        string? TextPreview,
        string? JsonPreview,
        string? HexPreview,
        bool HexTruncated,
        string? Error)
    {
        public static DecodeResult Empty { get; } = new(0, false, false, null, null, string.Empty, false, null);
    }
}
