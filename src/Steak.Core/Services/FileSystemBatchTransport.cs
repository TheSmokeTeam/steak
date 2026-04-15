using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Steak.Core.Contracts;

namespace Steak.Core.Services;

/// <summary>
/// Reads Steak envelope JSON files from the local file system.
/// </summary>
internal sealed class FileSystemEnvelopeReader(ILogger<FileSystemEnvelopeReader>? logger = null) : IBatchEnvelopeReader
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

        logger?.LogDebug("Reading Steak envelopes from file system path {Path}", path);

        if (!Directory.Exists(path))
        {
            throw new InvalidOperationException($"Source directory does not exist: {path}");
        }

        var files = Directory.EnumerateFiles(path, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            logger?.LogDebug("Reading Steak envelope file {FilePath}", file);

            await using var stream = File.OpenRead(file);
            var envelope = await JsonSerializer.DeserializeAsync<SteakMessageEnvelope>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (envelope is not null)
            {
                logger?.LogDebug(
                    "Loaded Steak envelope from {FilePath}: {EnvelopeSummary}",
                    file,
                    KafkaDiagnostics.FormatEnvelopeSummary(envelope));

                yield return envelope;
            }
        }
    }
}

/// <summary>
/// Writes Steak envelope JSON files to the local file system.
/// </summary>
internal sealed class FileSystemEnvelopeWriter(ILogger<FileSystemEnvelopeWriter>? logger = null) : IBatchEnvelopeWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
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

        logger?.LogDebug(
            "Writing Steak envelope to file system path {FilePath}: {EnvelopeSummary}",
            filePath,
            KafkaDiagnostics.FormatEnvelopeSummary(envelope));

        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, envelope, JsonOptions, cancellationToken).ConfigureAwait(false);
        return filePath;
    }
}
