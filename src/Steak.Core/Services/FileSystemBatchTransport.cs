using System.Runtime.CompilerServices;
using System.Text.Json;
using Steak.Core.Contracts;

namespace Steak.Core.Services;

/// <summary>
/// Reads Steak envelope JSON files from the local file system.
/// </summary>
internal sealed class FileSystemEnvelopeReader : IBatchEnvelopeReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public BatchTransportKind TransportKind => BatchTransportKind.FileSystem;

    public async IAsyncEnumerable<SteakMessageEnvelope> ReadEnvelopesAsync(
        BatchSourceOptions source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var path = source.FileSystem?.Path
            ?? throw new InvalidOperationException("FileSystem.Path is required for file system batch source.");

        if (!Directory.Exists(path))
        {
            throw new InvalidOperationException($"Source directory does not exist: {path}");
        }

        var files = Directory.EnumerateFiles(path, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var stream = File.OpenRead(file);
            var envelope = await JsonSerializer.DeserializeAsync<SteakMessageEnvelope>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (envelope is not null)
            {
                yield return envelope;
            }
        }
    }
}

/// <summary>
/// Writes Steak envelope JSON files to the local file system.
/// </summary>
internal sealed class FileSystemEnvelopeWriter : IBatchEnvelopeWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public BatchTransportKind TransportKind => BatchTransportKind.FileSystem;

    public async Task<string> WriteEnvelopeAsync(
        SteakMessageEnvelope envelope,
        string fileName,
        BatchDestinationOptions destination,
        CancellationToken cancellationToken = default)
    {
        var path = destination.FileSystem?.Path
            ?? throw new InvalidOperationException("FileSystem.Path is required for file system batch destination.");

        Directory.CreateDirectory(path);
        var filePath = Path.Combine(path, fileName);

        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, envelope, JsonOptions, cancellationToken).ConfigureAwait(false);
        return filePath;
    }
}
