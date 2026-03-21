namespace Steak.Core.Contracts;

/// <summary>
/// Identifies which Kafka client flavor is being configured.
/// </summary>
public enum KafkaClientKind
{
    /// <summary>
    /// Administrative metadata operations such as topic discovery.
    /// </summary>
    Admin,

    /// <summary>
    /// Message consumption and live view or export workflows.
    /// </summary>
    Consumer,

    /// <summary>
    /// Message publishing workflows.
    /// </summary>
    Producer
}

/// <summary>
/// Represents the starting offset behavior for view and export sessions.
/// </summary>
public enum MessageOffsetMode
{
    /// <summary>
    /// Reuse the broker-stored offset for the configured consumer group.
    /// </summary>
    Stored,

    /// <summary>
    /// Start from the earliest retained offset.
    /// </summary>
    Earliest,

    /// <summary>
    /// Start from the latest offset and only observe new messages.
    /// </summary>
    Latest
}

/// <summary>
/// Selects the batch I/O transport used for publish and consume workflows.
/// </summary>
public enum BatchTransportKind
{
    /// <summary>
    /// Read or write envelopes from/to a local or mounted file system path.
    /// </summary>
    FileSystem,

    /// <summary>
    /// Read or write envelopes from/to an S3-compatible object store.
    /// </summary>
    S3
}

// ────────────────────────────────────────────────────────────────
// Connection session contracts
// ────────────────────────────────────────────────────────────────

/// <summary>
/// Holds the Kafka connection fields submitted by the in-app connection form.
/// Leave optional security fields blank for plaintext clusters.
/// </summary>
public sealed class KafkaConnectionSettings
{
    /// <summary>
    /// Comma-separated broker endpoints passed to the Kafka client.
    /// </summary>
    public string BootstrapServers { get; set; } = string.Empty;

    /// <summary>
    /// SASL username.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// SASL password.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Optional Kafka security protocol. Friendly names such as <c>Plaintext</c>,
    /// <c>SaslPlaintext</c>, <c>SaslSsl</c>, and <c>Ssl</c> are accepted.
    /// </summary>
    public string SecurityProtocol { get; set; } = "SaslPlaintext";

    /// <summary>
    /// Optional SASL mechanism. Friendly names such as <c>Plain</c>,
    /// <c>ScramSha256</c>, and <c>ScramSha512</c> are accepted.
    /// </summary>
    public string SaslMechanism { get; set; } = "ScramSha512";

    /// <summary>
    /// Optional client id applied to all connection types.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Optional CA certificate PEM for SSL connections.
    /// </summary>
    public string? SslCaPem { get; set; }

    /// <summary>
    /// Optional client certificate PEM for SSL connections.
    /// </summary>
    public string? SslCertificatePem { get; set; }

    /// <summary>
    /// Optional client private key PEM for SSL connections.
    /// </summary>
    public string? SslKeyPem { get; set; }

    /// <summary>
    /// Optional password protecting the SSL private key.
    /// </summary>
    public string? SslKeyPassword { get; set; }

    /// <summary>
    /// Raw key=value overrides merged last over all other settings.
    /// </summary>
    public Dictionary<string, string> AdvancedOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Request sent to establish a server-side connection session.
/// </summary>
public sealed class ConnectRequest
{
    /// <summary>
    /// Connection settings provided by the in-app form.
    /// </summary>
    public KafkaConnectionSettings Settings { get; set; } = new();
}

/// <summary>
/// Returned after a successful connect, carrying the session id for subsequent calls.
/// </summary>
public sealed class ConnectResponse
{
    /// <summary>
    /// Server-generated session identifier.
    /// </summary>
    public string ConnectionSessionId { get; set; } = string.Empty;

    /// <summary>
    /// Bootstrap servers that the session is connected to.
    /// </summary>
    public string BootstrapServers { get; set; } = string.Empty;
}

/// <summary>
/// Describes the state of the active connection session.
/// </summary>
public sealed class ConnectionSessionStatus
{
    /// <summary>
    /// Whether a connection session is active.
    /// </summary>
    public bool IsConnected { get; set; }

    /// <summary>
    /// Active session id, when connected.
    /// </summary>
    public string? ConnectionSessionId { get; set; }

    /// <summary>
    /// Bootstrap servers, when connected.
    /// </summary>
    public string? BootstrapServers { get; set; }

    /// <summary>
    /// Username used for the session.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// UTC time the session was established.
    /// </summary>
    public DateTimeOffset? ConnectedAtUtc { get; set; }
}

// ────────────────────────────────────────────────────────────────
// Batch transport contracts
// ────────────────────────────────────────────────────────────────

/// <summary>
/// File system location for batch envelope I/O.
/// </summary>
public sealed class FileSystemLocationOptions
{
    /// <summary>
    /// Absolute or relative path to the folder.
    /// </summary>
    public string Path { get; set; } = string.Empty;
}

/// <summary>
/// S3 location for batch envelope I/O.
/// </summary>
public sealed class S3LocationOptions
{
    /// <summary>
    /// S3-compatible endpoint URL. Leave blank for AWS default.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// AWS region.
    /// </summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>
    /// S3 bucket name.
    /// </summary>
    public string Bucket { get; set; } = string.Empty;

    /// <summary>
    /// Optional key prefix (folder) within the bucket.
    /// </summary>
    public string? Prefix { get; set; }

    /// <summary>
    /// S3 access key id.
    /// </summary>
    public string AccessKey { get; set; } = string.Empty;

    /// <summary>
    /// S3 secret access key.
    /// </summary>
    public string SecretKey { get; set; } = string.Empty;
}

/// <summary>
/// Configures the source for a batch publish operation.
/// </summary>
public sealed class BatchSourceOptions
{
    /// <summary>
    /// Transport type for reading envelopes.
    /// </summary>
    public BatchTransportKind TransportKind { get; set; } = BatchTransportKind.FileSystem;

    /// <summary>
    /// File system settings when <see cref="TransportKind"/> is <see cref="BatchTransportKind.FileSystem"/>.
    /// </summary>
    public FileSystemLocationOptions? FileSystem { get; set; }

    /// <summary>
    /// S3 settings when <see cref="TransportKind"/> is <see cref="BatchTransportKind.S3"/>.
    /// </summary>
    public S3LocationOptions? S3 { get; set; }
}

/// <summary>
/// Configures the destination for a batch consume/export operation.
/// </summary>
public sealed class BatchDestinationOptions
{
    /// <summary>
    /// Transport type for writing envelopes.
    /// </summary>
    public BatchTransportKind TransportKind { get; set; } = BatchTransportKind.FileSystem;

    /// <summary>
    /// File system settings when <see cref="TransportKind"/> is <see cref="BatchTransportKind.FileSystem"/>.
    /// </summary>
    public FileSystemLocationOptions? FileSystem { get; set; }

    /// <summary>
    /// S3 settings when <see cref="TransportKind"/> is <see cref="BatchTransportKind.S3"/>.
    /// </summary>
    public S3LocationOptions? S3 { get; set; }
}

// ────────────────────────────────────────────────────────────────
// Batch publish contracts
// ────────────────────────────────────────────────────────────────

/// <summary>
/// Starts a batch publish job that reads envelopes from the configured source and publishes them to Kafka.
/// </summary>
public sealed class BatchPublishRequest
{
    /// <summary>
    /// Active connection session id.
    /// </summary>
    public string ConnectionSessionId { get; set; } = string.Empty;

    /// <summary>
    /// Where to read the Steak envelope JSON files from.
    /// </summary>
    public BatchSourceOptions Source { get; set; } = new();

    /// <summary>
    /// Optional topic override applied to every envelope.
    /// </summary>
    public string? TopicOverride { get; set; }

    /// <summary>
    /// Maximum envelopes to publish. Zero or null means no limit.
    /// </summary>
    public int? MaxMessages { get; set; }

    /// <summary>
    /// Target throughput in messages per second. Zero or null means unlimited.
    /// </summary>
    public double? MessagesPerSecond { get; set; }

    /// <summary>
    /// When true, restart publishing from the beginning once all envelopes have been sent.
    /// </summary>
    public bool Loop { get; set; }
}

/// <summary>
/// Current state of the active batch publish job.
/// </summary>
public sealed class BatchPublishJobStatus
{
    /// <summary>
    /// Whether a batch publish job is currently running.
    /// </summary>
    public bool IsRunning { get; set; }

    /// <summary>
    /// Number of envelopes successfully published.
    /// </summary>
    public long PublishedCount { get; set; }

    /// <summary>
    /// Total envelopes discovered in the source.
    /// </summary>
    public long TotalEnvelopes { get; set; }

    /// <summary>
    /// Current throughput.
    /// </summary>
    public double CurrentMessagesPerSecond { get; set; }

    /// <summary>
    /// Last error from the job, when present.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// UTC start time.
    /// </summary>
    public DateTimeOffset? StartedAtUtc { get; set; }
}

/// <summary>
/// Represents one Kafka header with both byte-preserving and decoded previews.
/// </summary>
public sealed class SteakMessageHeader
{
    /// <summary>
    /// Kafka header key.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Raw header value preserved as base64.
    /// </summary>
    public string? ValueBase64 { get; set; }

    /// <summary>
    /// UTF-8 text preview when the value can be decoded safely.
    /// </summary>
    public string? Utf8Preview { get; set; }

    /// <summary>
    /// Hexadecimal preview for binary inspection.
    /// </summary>
    public string? HexPreview { get; set; }

    /// <summary>
    /// Indicates whether <see cref="Utf8Preview"/> is valid UTF-8.
    /// </summary>
    public bool IsUtf8 { get; set; }

    /// <summary>
    /// Indicates whether the generated preview was clipped.
    /// </summary>
    public bool IsTruncated { get; set; }

    /// <summary>
    /// Decode error captured while building the preview, when present.
    /// </summary>
    public string? DecodeError { get; set; }
}

/// <summary>
/// Captures the derived inspection views for a Kafka key and value payload.
/// </summary>
public sealed class MessagePreview
{
    /// <summary>
    /// Decoded key byte length.
    /// </summary>
    public int KeyLength { get; set; }

    /// <summary>
    /// Decoded value byte length.
    /// </summary>
    public int ValueLength { get; set; }

    /// <summary>
    /// Indicates whether the key bytes represent valid UTF-8.
    /// </summary>
    public bool KeyIsUtf8 { get; set; }

    /// <summary>
    /// UTF-8 preview of the key when available.
    /// </summary>
    public string? KeyUtf8Preview { get; set; }

    /// <summary>
    /// Hex preview of the key.
    /// </summary>
    public string? KeyHexPreview { get; set; }

    /// <summary>
    /// Indicates whether the key hex preview was clipped.
    /// </summary>
    public bool KeyHexTruncated { get; set; }

    /// <summary>
    /// Error captured while decoding the key.
    /// </summary>
    public string? KeyDecodeError { get; set; }

    /// <summary>
    /// Indicates whether the value bytes represent valid UTF-8.
    /// </summary>
    public bool ValueIsUtf8 { get; set; }

    /// <summary>
    /// Indicates whether the value can also be parsed as JSON.
    /// </summary>
    public bool ValueIsJson { get; set; }

    /// <summary>
    /// UTF-8 preview of the value when available.
    /// </summary>
    public string? ValueUtf8Preview { get; set; }

    /// <summary>
    /// Pretty-printed JSON preview of the value when valid JSON is detected.
    /// </summary>
    public string? ValuePrettyJson { get; set; }

    /// <summary>
    /// Hex preview of the raw value bytes.
    /// </summary>
    public string? ValueHexPreview { get; set; }

    /// <summary>
    /// Indicates whether the value hex preview was clipped.
    /// </summary>
    public bool ValueHexTruncated { get; set; }

    /// <summary>
    /// Error captured while decoding the value.
    /// </summary>
    public string? ValueDecodeError { get; set; }
}

/// <summary>
/// The canonical JSON contract used by Steak for view, export, and publish workflows.
/// </summary>
public sealed class SteakMessageEnvelope
{
    /// <summary>
    /// Producing application name. Defaults to <c>Steak</c>.
    /// </summary>
    public string App { get; set; } = "Steak";

    /// <summary>
    /// UTC timestamp when Steak captured or normalized the envelope.
    /// </summary>
    public DateTimeOffset? CapturedAtUtc { get; set; }

    /// <summary>
    /// Connection session id associated with the envelope.
    /// </summary>
    public string? ConnectionSessionId { get; set; }

    /// <summary>
    /// Kafka topic name.
    /// </summary>
    public string? Topic { get; set; }

    /// <summary>
    /// Kafka partition number when known.
    /// </summary>
    public int? Partition { get; set; }

    /// <summary>
    /// Kafka offset when known.
    /// </summary>
    public long? Offset { get; set; }

    /// <summary>
    /// Broker timestamp associated with the message when available.
    /// </summary>
    public DateTimeOffset? TimestampUtc { get; set; }

    /// <summary>
    /// Kafka timestamp type label when available.
    /// </summary>
    public string? TimestampType { get; set; }

    /// <summary>
    /// Raw message key preserved as base64.
    /// </summary>
    public string? KeyBase64 { get; set; }

    /// <summary>
    /// Raw message value preserved as base64.
    /// </summary>
    public string ValueBase64 { get; set; } = string.Empty;

    /// <summary>
    /// Message headers with derived previews.
    /// </summary>
    public List<SteakMessageHeader> Headers { get; set; } = [];

    /// <summary>
    /// Derived previews generated from the base64 payloads.
    /// </summary>
    public MessagePreview? Preview { get; set; }
}

/// <summary>
/// Describes one topic partition from Kafka metadata.
/// </summary>
public sealed class TopicPartitionSummary
{
    /// <summary>
    /// Partition id.
    /// </summary>
    public int PartitionId { get; set; }

    /// <summary>
    /// Leader broker string when known.
    /// </summary>
    public string? Leader { get; set; }

    /// <summary>
    /// Replica broker list.
    /// </summary>
    public List<string> Replicas { get; set; } = [];

    /// <summary>
    /// In-sync replica broker list.
    /// </summary>
    public List<string> InSyncReplicas { get; set; } = [];
}

/// <summary>
/// Describes a Kafka topic and its partitions.
/// </summary>
public sealed class KafkaTopicSummary
{
    /// <summary>
    /// Topic name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether the topic is internal to the broker.
    /// </summary>
    public bool IsInternal { get; set; }

    /// <summary>
    /// Total partition count.
    /// </summary>
    public int PartitionCount { get; set; }

    /// <summary>
    /// Detailed partition metadata.
    /// </summary>
    public List<TopicPartitionSummary> Partitions { get; set; } = [];
}

/// <summary>
/// Starts a consume-to-folder or S3 export job.
/// </summary>
public sealed class CreateConsumeJobRequest
{
    /// <summary>
    /// Active connection session id.
    /// </summary>
    public string ConnectionSessionId { get; set; } = string.Empty;

    /// <summary>
    /// Topic to export.
    /// </summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>
    /// Consumer group id used for the export worker.
    /// </summary>
    public string GroupId { get; set; } = string.Empty;

    /// <summary>
    /// Optional partition restriction. When omitted, all partitions are consumed.
    /// </summary>
    public int? Partition { get; set; }

    /// <summary>
    /// Offset mode used when the consumer group has no stored offset yet.
    /// </summary>
    public MessageOffsetMode OffsetMode { get; set; } = MessageOffsetMode.Latest;

    /// <summary>
    /// Destination for exported envelopes.
    /// </summary>
    public BatchDestinationOptions Destination { get; set; } = new();

    /// <summary>
    /// Maximum number of messages to export. Zero or null means no limit.
    /// </summary>
    public int? MaxMessages { get; set; }

    /// <summary>
    /// Target throughput in messages per second. Zero or null means unlimited.
    /// </summary>
    public double? MessagesPerSecond { get; set; }
}

/// <summary>
/// Represents the current state of the active export worker.
/// </summary>
public sealed class ConsumeJobStatus
{
    /// <summary>
    /// Indicates whether an export job is currently active.
    /// </summary>
    public bool IsRunning { get; set; }

    /// <summary>
    /// Connection session id used by the active job.
    /// </summary>
    public string? ConnectionSessionId { get; set; }

    /// <summary>
    /// Topic currently being exported.
    /// </summary>
    public string? Topic { get; set; }

    /// <summary>
    /// Consumer group id used by the active job.
    /// </summary>
    public string? GroupId { get; set; }

    /// <summary>
    /// Partition restriction when one is active.
    /// </summary>
    public int? Partition { get; set; }

    /// <summary>
    /// Offset behavior for the active job.
    /// </summary>
    public MessageOffsetMode OffsetMode { get; set; }

    /// <summary>
    /// UTC start time for the active job.
    /// </summary>
    public DateTimeOffset? StartedAtUtc { get; set; }

    /// <summary>
    /// Number of envelopes successfully written.
    /// </summary>
    public long ExportedCount { get; set; }

    /// <summary>
    /// Last destination reference (file path or S3 key).
    /// </summary>
    public string? LastDestination { get; set; }

    /// <summary>
    /// Last error emitted by the worker, when present.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Approximate throughput based on successfully written envelopes.
    /// </summary>
    public double CurrentMessagesPerSecond { get; set; }
}

/// <summary>
/// Starts a live view session for a topic or partition.
/// </summary>
public sealed class StartViewSessionRequest
{
    /// <summary>
    /// Active connection session id.
    /// </summary>
    public string ConnectionSessionId { get; set; } = string.Empty;

    /// <summary>
    /// Topic to view.
    /// </summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>
    /// Optional partition restriction. When omitted, the session subscribes to the whole topic.
    /// </summary>
    public int? Partition { get; set; }

    /// <summary>
    /// Offset behavior used when the consumer group has no stored offset yet.
    /// </summary>
    public MessageOffsetMode OffsetMode { get; set; } = MessageOffsetMode.Latest;

    /// <summary>
    /// Optional caller-supplied consumer group id. Steak generates an ephemeral group when omitted.
    /// </summary>
    public string? GroupId { get; set; }

    /// <summary>
    /// Maximum number of recent messages retained in memory for the session.
    /// </summary>
    public int MaxMessages { get; set; } = 250;
}

/// <summary>
/// Represents the current state of the active live view session.
/// </summary>
public sealed class ViewSessionStatus
{
    /// <summary>
    /// Indicates whether a live view session is active.
    /// </summary>
    public bool IsRunning { get; set; }

    /// <summary>
    /// Connection session id backing the session.
    /// </summary>
    public string? ConnectionSessionId { get; set; }

    /// <summary>
    /// Topic currently being viewed.
    /// </summary>
    public string? Topic { get; set; }

    /// <summary>
    /// Partition restriction when one is active.
    /// </summary>
    public int? Partition { get; set; }

    /// <summary>
    /// Offset behavior for the session.
    /// </summary>
    public MessageOffsetMode OffsetMode { get; set; }

    /// <summary>
    /// UTC start time for the session.
    /// </summary>
    public DateTimeOffset? StartedAtUtc { get; set; }

    /// <summary>
    /// Number of messages received during the current session.
    /// </summary>
    public long ReceivedCount { get; set; }

    /// <summary>
    /// Last error emitted by the session, when present.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Recent message buffer exposed to the UI and SSE stream.
    /// </summary>
    public List<SteakMessageEnvelope> RecentMessages { get; set; } = [];
}

/// <summary>
/// Request contract for publishing a single Steak envelope to Kafka.
/// </summary>
public sealed class PublishEnvelopeRequest
{
    /// <summary>
    /// Active connection session id.
    /// </summary>
    public string? ConnectionSessionId { get; set; }

    /// <summary>
    /// Optional target topic. When omitted, the envelope's topic is used.
    /// </summary>
    public string? Topic { get; set; }

    /// <summary>
    /// Envelope payload to publish.
    /// </summary>
    public SteakMessageEnvelope Envelope { get; set; } = new();
}

/// <summary>
/// Publish delivery metadata returned by the broker.
/// </summary>
public sealed class PublishResultInfo
{
    /// <summary>
    /// Topic that received the message.
    /// </summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>
    /// Destination partition.
    /// </summary>
    public int Partition { get; set; }

    /// <summary>
    /// Broker-assigned offset.
    /// </summary>
    public long Offset { get; set; }

    /// <summary>
    /// Broker timestamp when available.
    /// </summary>
    public DateTimeOffset? TimestampUtc { get; set; }

    /// <summary>
    /// Delivery status string returned by the Kafka client.
    /// </summary>
    public string Status { get; set; } = "unknown";
}

/// <summary>
/// Request contract for generating decoded previews from base64 payloads.
/// </summary>
public sealed class MessagePreviewRequest
{
    /// <summary>
    /// Optional base64-encoded key.
    /// </summary>
    public string? KeyBase64 { get; set; }

    /// <summary>
    /// Required base64-encoded message value.
    /// </summary>
    public string ValueBase64 { get; set; } = string.Empty;
}

/// <summary>
/// Configures where Steak persists exported envelopes.
/// </summary>
public sealed class SteakStorageOptions
{
    /// <summary>
    /// Root directory for all mutable Steak data.
    /// </summary>
    public string DataRoot { get; set; } = string.Empty;

    /// <summary>
    /// Folder name used for exported envelopes beneath <see cref="DataRoot"/>.
    /// </summary>
    public string ExportsFolderName { get; set; } = "exports";
}
