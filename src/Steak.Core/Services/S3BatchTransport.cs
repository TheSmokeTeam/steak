using System.Runtime.CompilerServices;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Steak.Core.Contracts;

namespace Steak.Core.Services;

/// <summary>
/// Reads Steak envelope JSON files from an S3-compatible object store.
/// </summary>
internal sealed class S3EnvelopeReader : IBatchEnvelopeReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public BatchTransportKind TransportKind => BatchTransportKind.S3;

    public async IAsyncEnumerable<SteakMessageEnvelope> ReadEnvelopesAsync(
        BatchSourceOptions source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var s3 = source.S3 ?? throw new InvalidOperationException("S3 settings are required for S3 batch source.");
        ValidateS3Options(s3);

        using var client = CreateClient(s3);
        var prefix = string.IsNullOrWhiteSpace(s3.Prefix) ? "" : s3.Prefix.TrimEnd('/') + "/";

        var request = new ListObjectsV2Request
        {
            BucketName = s3.Bucket,
            Prefix = prefix
        };

        ListObjectsV2Response response;
        do
        {
            response = await client.ListObjectsV2Async(request, cancellationToken).ConfigureAwait(false);

            foreach (var obj in response.S3Objects
                         .Where(o => o.Key.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(o => o.Key, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var getResponse = await client.GetObjectAsync(s3.Bucket, obj.Key, cancellationToken).ConfigureAwait(false);
                var envelope = await JsonSerializer.DeserializeAsync<SteakMessageEnvelope>(getResponse.ResponseStream, JsonOptions, cancellationToken).ConfigureAwait(false);
                if (envelope is not null)
                {
                    yield return envelope;
                }
            }

            request.ContinuationToken = response.NextContinuationToken;
        }
        while (response.IsTruncated == true);
    }

    private static AmazonS3Client CreateClient(S3LocationOptions s3)
    {
        var config = new AmazonS3Config { RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(s3.Region) };
        if (!string.IsNullOrWhiteSpace(s3.Endpoint))
        {
            config.ServiceURL = s3.Endpoint;
            config.ForcePathStyle = true;
        }

        return new AmazonS3Client(s3.AccessKey, s3.SecretKey, config);
    }

    private static void ValidateS3Options(S3LocationOptions s3)
    {
        if (string.IsNullOrWhiteSpace(s3.Bucket)) throw new InvalidOperationException("S3 bucket is required.");
        if (string.IsNullOrWhiteSpace(s3.AccessKey)) throw new InvalidOperationException("S3 access key is required.");
        if (string.IsNullOrWhiteSpace(s3.SecretKey)) throw new InvalidOperationException("S3 secret key is required.");
    }
}

/// <summary>
/// Writes Steak envelope JSON files to an S3-compatible object store.
/// </summary>
internal sealed class S3EnvelopeWriter : IBatchEnvelopeWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public BatchTransportKind TransportKind => BatchTransportKind.S3;

    public async Task<string> WriteEnvelopeAsync(
        SteakMessageEnvelope envelope,
        string fileName,
        BatchDestinationOptions destination,
        CancellationToken cancellationToken = default)
    {
        var s3 = destination.S3 ?? throw new InvalidOperationException("S3 settings are required for S3 batch destination.");
        ValidateS3Options(s3);

        var prefix = string.IsNullOrWhiteSpace(s3.Prefix) ? "" : s3.Prefix.TrimEnd('/') + "/";
        var key = $"{prefix}{fileName}";

        using var client = CreateClient(s3);
        using var memoryStream = new MemoryStream();
        await JsonSerializer.SerializeAsync(memoryStream, envelope, JsonOptions, cancellationToken).ConfigureAwait(false);
        memoryStream.Position = 0;

        var request = new PutObjectRequest
        {
            BucketName = s3.Bucket,
            Key = key,
            InputStream = memoryStream,
            ContentType = "application/json"
        };

        await client.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);
        return $"s3://{s3.Bucket}/{key}";
    }

    private static AmazonS3Client CreateClient(S3LocationOptions s3)
    {
        var config = new AmazonS3Config { RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(s3.Region) };
        if (!string.IsNullOrWhiteSpace(s3.Endpoint))
        {
            config.ServiceURL = s3.Endpoint;
            config.ForcePathStyle = true;
        }

        return new AmazonS3Client(s3.AccessKey, s3.SecretKey, config);
    }

    private static void ValidateS3Options(S3LocationOptions s3)
    {
        if (string.IsNullOrWhiteSpace(s3.Bucket)) throw new InvalidOperationException("S3 bucket is required.");
        if (string.IsNullOrWhiteSpace(s3.AccessKey)) throw new InvalidOperationException("S3 access key is required.");
        if (string.IsNullOrWhiteSpace(s3.SecretKey)) throw new InvalidOperationException("S3 secret key is required.");
    }
}
