using Microsoft.AspNetCore.Http.HttpResults;
using Steak.Core.Contracts;

namespace Steak.Host.Api;

/// <summary>
/// Maps the Steak Minimal API surface used by the UI and external automation clients.
/// </summary>
public static class SteakApiEndpoints
{
    /// <summary>
    /// Registers the Steak API route groups.
    /// </summary>
    public static IEndpointRouteBuilder MapSteakApi(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api").WithTags("Steak");

        api.MapGet("/health", (IHostEnvironment environment) =>
        {
            var payload = new
            {
                app = "Steak",
                environment = environment.EnvironmentName,
                utcNow = DateTimeOffset.UtcNow,
                version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown"
            };

            return TypedResults.Ok(payload);
        })
        .WithName("GetHealth")
        .WithSummary("Get runtime health information.");

        // Connection session management
        var connection = api.MapGroup("/connection");

        connection.MapPost("/", (ConnectRequest request, IConnectionSessionService sessionService) =>
        {
            try
            {
                return Results.Ok(sessionService.Connect(request));
            }
            catch (Exception exception)
            {
                return ToProblemResult(exception);
            }
        })
        .WithName("Connect")
        .WithSummary("Establish a new Kafka connection session.");

        connection.MapDelete("/", (IConnectionSessionService sessionService) =>
        {
            sessionService.Disconnect();
            return Results.NoContent();
        })
        .WithName("Disconnect")
        .WithSummary("Disconnect and discard all active connection sessions.");

        connection.MapDelete("/{sessionId}", (string sessionId, IConnectionSessionService sessionService) =>
        {
            sessionService.Disconnect(sessionId);
            return Results.NoContent();
        })
        .WithName("DisconnectSession")
        .WithSummary("Disconnect a specific connection session by id.");

        connection.MapGet("/", (IConnectionSessionService sessionService) =>
        {
            return Results.Ok(sessionService.GetStatus());
        })
        .WithName("GetConnectionStatus")
        .WithSummary("Get the current connection session status.");

        connection.MapGet("/all", (IConnectionSessionService sessionService) =>
        {
            return Results.Ok(sessionService.GetAllSessions());
        })
        .WithName("GetAllConnections")
        .WithSummary("Get all active connection sessions.");

        // Topic browsing
        var topics = api.MapGroup("/topics");

        topics.MapGet("/", async Task<Results<Ok<IReadOnlyList<KafkaTopicSummary>>, ProblemHttpResult>> (
            [AsParameters] SessionQuery query,
            ITopicBrowserService topicBrowser,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return TypedResults.Ok(await topicBrowser.ListTopicsAsync(query.ConnectionSessionId, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception exception)
            {
                return ToProblem(exception);
            }
        })
        .WithName("ListTopics")
        .WithSummary("List Kafka topics for the active connection session.");

        topics.MapGet("/{topic}", async Task<Results<Ok<KafkaTopicSummary>, NotFound, ProblemHttpResult>> (
            string topic,
            [AsParameters] SessionQuery query,
            ITopicBrowserService topicBrowser,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var summary = await topicBrowser.GetTopicAsync(query.ConnectionSessionId, topic, cancellationToken).ConfigureAwait(false);
                return summary is null ? TypedResults.NotFound() : TypedResults.Ok(summary);
            }
            catch (Exception exception)
            {
                return ToProblem(exception);
            }
        })
        .WithName("GetTopic")
        .WithSummary("Get metadata for one Kafka topic.");

        // Live view sessions
        var viewSessions = api.MapGroup("/view-sessions");

        viewSessions.MapGet("/", (IViewSessionService viewSessionService) => TypedResults.Ok(viewSessionService.Snapshot))
            .WithName("GetViewSession")
            .WithSummary("Get the current live view session status.");

        viewSessions.MapPost("/", async Task<Results<Ok<ViewSessionStatus>, ProblemHttpResult>> (
            StartViewSessionRequest request,
            IViewSessionService viewSessionService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return TypedResults.Ok(await viewSessionService.StartAsync(request, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception exception)
            {
                return ToProblem(exception);
            }
        })
        .WithName("StartViewSession")
        .WithSummary("Start a live message viewer session.");

        viewSessions.MapDelete("/", async (IViewSessionService viewSessionService) =>
        {
            await viewSessionService.StopAsync().ConfigureAwait(false);
            return TypedResults.NoContent();
        })
        .WithName("StopViewSession")
        .WithSummary("Stop the live message viewer session.");

        viewSessions.MapGet("/events", async (HttpContext context, IViewSessionService viewSessionService) =>
        {
            context.Response.Headers.ContentType = "text/event-stream";

            await foreach (var message in viewSessionService.StreamAsync(context.RequestAborted))
            {
                var payload = System.Text.Json.JsonSerializer.Serialize(message);
                await context.Response.WriteAsync($"data: {payload}\n\n", context.RequestAborted).ConfigureAwait(false);
                await context.Response.Body.FlushAsync(context.RequestAborted).ConfigureAwait(false);
            }
        })
        .WithName("StreamViewSessionEvents")
        .WithSummary("Stream live Kafka messages as server-sent events.");

        // Consume export jobs
        var consumeJobs = api.MapGroup("/consume-jobs");

        consumeJobs.MapGet("/", (IConsumeExportService consumeExportService) => TypedResults.Ok(consumeExportService.Snapshot))
            .WithName("GetConsumeJob")
            .WithSummary("Get the current export job status.");

        consumeJobs.MapPost("/", async Task<Results<Ok<ConsumeJobStatus>, ProblemHttpResult>> (
            CreateConsumeJobRequest request,
            IConsumeExportService consumeExportService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return TypedResults.Ok(await consumeExportService.StartAsync(request, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception exception)
            {
                return ToProblem(exception);
            }
        })
        .WithName("StartConsumeJob")
        .WithSummary("Start a consume-to-destination export job.");

        consumeJobs.MapDelete("/", async (IConsumeExportService consumeExportService) =>
        {
            await consumeExportService.StopAsync().ConfigureAwait(false);
            return TypedResults.NoContent();
        })
        .WithName("StopConsumeJob")
        .WithSummary("Stop the consume-to-destination export job.");

        // Batch publish jobs
        var batchPublish = api.MapGroup("/batch-publish");

        batchPublish.MapGet("/", (IBatchPublishService batchPublishService) => TypedResults.Ok(batchPublishService.Snapshot))
            .WithName("GetBatchPublishJob")
            .WithSummary("Get the current batch publish job status.");

        batchPublish.MapPost("/", async Task<Results<Ok<BatchPublishJobStatus>, ProblemHttpResult>> (
            BatchPublishRequest request,
            IBatchPublishService batchPublishService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return TypedResults.Ok(await batchPublishService.StartAsync(request, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception exception)
            {
                return ToProblem(exception);
            }
        })
        .WithName("StartBatchPublishJob")
        .WithSummary("Start a batch publish job from a file or S3 source.");

        batchPublish.MapDelete("/", async (IBatchPublishService batchPublishService) =>
        {
            await batchPublishService.StopAsync().ConfigureAwait(false);
            return TypedResults.NoContent();
        })
        .WithName("StopBatchPublishJob")
        .WithSummary("Stop the batch publish job.");

        // Message preview
        var messages = api.MapGroup("/messages");

        messages.MapPost("/preview", (MessagePreviewRequest request, IMessagePreviewService previewService) =>
        {
            return TypedResults.Ok(previewService.CreatePreview(request.KeyBase64, request.ValueBase64));
        })
        .WithName("PreviewMessage")
        .WithSummary("Preview decoded message content from the envelope base64 fields.");

        // Single envelope publish
        api.MapPost("/publish", async Task<Results<Ok<PublishResultInfo>, ProblemHttpResult>> (
            PublishEnvelopeRequest request,
            IMessagePublisher messagePublisher,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return TypedResults.Ok(await messagePublisher.PublishAsync(request, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception exception)
            {
                return ToProblem(exception);
            }
        })
        .WithName("PublishMessage")
        .WithSummary("Publish a single Steak envelope to Kafka.");

        return endpoints;
    }

    private static ProblemHttpResult ToProblem(Exception exception)
    {
        var statusCode = exception switch
        {
            FormatException => StatusCodes.Status400BadRequest,
            InvalidOperationException => StatusCodes.Status400BadRequest,
            ArgumentException => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };

        return TypedResults.Problem(
            title: $"Steak API error ({exception.GetType().Name})",
            detail: SteakErrorDetails.Format(exception),
            statusCode: statusCode);
    }

    private static IResult ToProblemResult(Exception exception)
    {
        var statusCode = exception switch
        {
            FormatException => StatusCodes.Status400BadRequest,
            InvalidOperationException => StatusCodes.Status400BadRequest,
            ArgumentException => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };

        return Results.Problem(
            title: $"Steak API error ({exception.GetType().Name})",
            detail: SteakErrorDetails.Format(exception),
            statusCode: statusCode);
    }

    /// <summary>
    /// Shared query string contract for session-scoped requests.
    /// </summary>
    public sealed class SessionQuery
    {
        /// <summary>
        /// Active connection session id.
        /// </summary>
        public string ConnectionSessionId { get; set; } = string.Empty;
    }
}
