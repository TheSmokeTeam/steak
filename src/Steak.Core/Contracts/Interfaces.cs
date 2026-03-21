namespace Steak.Core.Contracts;

/// <summary>
/// Manages the single active Kafka connection session.
/// All Kafka operations use the session's stored settings rather than saved profiles.
/// </summary>
public interface IConnectionSessionService
{
    /// <summary>
    /// Establishes a new connection session, replacing any existing one.
    /// </summary>
    ConnectResponse Connect(ConnectRequest request);

    /// <summary>
    /// Disconnects and discards the active session.
    /// </summary>
    void Disconnect();

    /// <summary>
    /// Returns the current session status.
    /// </summary>
    ConnectionSessionStatus GetStatus();

    /// <summary>
    /// Retrieves the connection settings for the active session.
    /// Throws if no session is active.
    /// </summary>
    KafkaConnectionSettings GetActiveSettings(string connectionSessionId);
}

/// <summary>
/// Builds the effective Kafka configuration from the active connection session.
/// </summary>
public interface IKafkaConfigurationService
{
    /// <summary>
    /// Produces the effective Kafka configuration for the requested client kind using session settings.
    /// </summary>
    Dictionary<string, string> BuildConfig(
        KafkaConnectionSettings settings,
        KafkaClientKind clientKind,
        IReadOnlyDictionary<string, string>? overrides = null);

    /// <summary>
    /// Produces the effective configuration with sensitive values masked for display.
    /// </summary>
    IReadOnlyDictionary<string, string> GetMaskedConfig(
        KafkaConnectionSettings settings,
        KafkaClientKind clientKind,
        IReadOnlyDictionary<string, string>? overrides = null);
}

/// <summary>
/// Provides topic and partition metadata for an active session.
/// </summary>
public interface ITopicBrowserService
{
    /// <summary>
    /// Lists all topics visible through the active connection.
    /// </summary>
    Task<IReadOnlyList<KafkaTopicSummary>> ListTopicsAsync(string connectionSessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns metadata for a single topic.
    /// </summary>
    Task<KafkaTopicSummary?> GetTopicAsync(string connectionSessionId, string topic, CancellationToken cancellationToken = default);
}

/// <summary>
/// Generates byte-preserving previews for keys, values, and headers.
/// </summary>
public interface IMessagePreviewService
{
    /// <summary>
    /// Builds preview metadata for the supplied base64 key and value.
    /// </summary>
    MessagePreview CreatePreview(string? keyBase64, string valueBase64);

    /// <summary>
    /// Builds preview metadata for a Kafka header value.
    /// </summary>
    SteakMessageHeader CreateHeaderPreview(string key, byte[]? value);
}

/// <summary>
/// Normalizes and enriches Steak envelopes before they are published.
/// </summary>
public interface IMessageEnvelopeFactory
{
    /// <summary>
    /// Applies default topic or session values and regenerates preview metadata.
    /// </summary>
    SteakMessageEnvelope NormalizeForPublish(SteakMessageEnvelope envelope, string? defaultSessionId, string? defaultTopic);
}

/// <summary>
/// Creates unique export file names for captured envelopes.
/// </summary>
public interface IFileNameFactory
{
    /// <summary>
    /// Builds a unique file name that follows the Steak export naming convention.
    /// </summary>
    string CreateMessageFileName(SteakMessageEnvelope envelope);
}

/// <summary>
/// Manages the single active live view session.
/// </summary>
public interface IViewSessionService
{
    /// <summary>
    /// Raised whenever the live session snapshot changes.
    /// </summary>
    event EventHandler? StateChanged;

    /// <summary>
    /// Returns the current live session snapshot.
    /// </summary>
    ViewSessionStatus Snapshot { get; }

    /// <summary>
    /// Starts a new live view session, replacing any existing one.
    /// </summary>
    Task<ViewSessionStatus> StartAsync(StartViewSessionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the current live view session if one is active.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Streams recent and newly captured envelopes for server-sent event consumers.
    /// </summary>
    IAsyncEnumerable<SteakMessageEnvelope> StreamAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Manages the single active consume-to-destination export job.
/// </summary>
public interface IConsumeExportService
{
    /// <summary>
    /// Raised whenever the export job snapshot changes.
    /// </summary>
    event EventHandler? StateChanged;

    /// <summary>
    /// Returns the current export job snapshot.
    /// </summary>
    ConsumeJobStatus Snapshot { get; }

    /// <summary>
    /// Starts the export worker with the supplied request.
    /// </summary>
    Task<ConsumeJobStatus> StartAsync(CreateConsumeJobRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the active export worker if one is running.
    /// </summary>
    Task StopAsync();
}

/// <summary>
/// Publishes normalized Steak envelopes to Kafka.
/// </summary>
public interface IMessagePublisher
{
    /// <summary>
    /// Publishes the request envelope and returns broker delivery metadata.
    /// </summary>
    Task<PublishResultInfo> PublishAsync(PublishEnvelopeRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Manages the single active batch publish job.
/// </summary>
public interface IBatchPublishService
{
    /// <summary>
    /// Raised whenever the batch job snapshot changes.
    /// </summary>
    event EventHandler? StateChanged;

    /// <summary>
    /// Returns the current batch publish job status.
    /// </summary>
    BatchPublishJobStatus Snapshot { get; }

    /// <summary>
    /// Starts a batch publish operation.
    /// </summary>
    Task<BatchPublishJobStatus> StartAsync(BatchPublishRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the active batch publish job.
    /// </summary>
    Task StopAsync();
}

/// <summary>
/// Reads Steak envelope JSON files from a storage location.
/// </summary>
public interface IBatchEnvelopeReader
{
    /// <summary>
    /// The transport kind this reader supports.
    /// </summary>
    BatchTransportKind TransportKind { get; }

    /// <summary>
    /// Enumerates available envelope files from the configured source.
    /// </summary>
    IAsyncEnumerable<SteakMessageEnvelope> ReadEnvelopesAsync(BatchSourceOptions source, CancellationToken cancellationToken = default);
}

/// <summary>
/// Writes Steak envelope JSON files to a storage location.
/// </summary>
public interface IBatchEnvelopeWriter
{
    /// <summary>
    /// The transport kind this writer supports.
    /// </summary>
    BatchTransportKind TransportKind { get; }

    /// <summary>
    /// Writes one envelope to the configured destination.
    /// Returns a destination reference (file path or S3 key).
    /// </summary>
    Task<string> WriteEnvelopeAsync(SteakMessageEnvelope envelope, string fileName, BatchDestinationOptions destination, CancellationToken cancellationToken = default);
}
