using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Steak.Core.Contracts;

namespace Steak.Core.Services;

/// <summary>
/// Registers the shared Steak runtime services used by the host, UI, and API layers.
/// </summary>
public static class SteakCoreServiceCollectionExtensions
{
    /// <summary>
    /// Adds the core Steak services and options required to talk to Kafka.
    /// </summary>
    public static IServiceCollection AddSteakCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SteakStorageOptions>(configuration.GetSection("Steak:Storage"));

        services.AddSingleton<IConnectionSessionService, ConnectionSessionService>();
        services.AddSingleton<IKafkaConfigurationService, KafkaConfigurationService>();
        services.AddSingleton<IMessagePreviewService, MessagePreviewService>();
        services.AddSingleton<IMessageEnvelopeFactory, MessageEnvelopeFactory>();
        services.AddSingleton<IFileNameFactory, FileNameFactory>();
        services.AddSingleton<ConsumedMessageEnvelopeFactory>();
        services.AddSingleton<ITopicBrowserService, KafkaTopicBrowserService>();
        services.AddSingleton<IMessagePublisher, KafkaMessagePublisher>();
        services.AddSingleton<IViewSessionService, KafkaViewSessionService>();

        // Batch envelope transports
        services.AddSingleton<IBatchEnvelopeReader, FileSystemEnvelopeReader>();
        services.AddSingleton<IBatchEnvelopeReader, S3EnvelopeReader>();
        services.AddSingleton<IBatchEnvelopeWriter, FileSystemEnvelopeWriter>();
        services.AddSingleton<IBatchEnvelopeWriter, S3EnvelopeWriter>();

        // Consume export background service
        services.AddSingleton<ConsumeExportBackgroundService>();
        services.AddSingleton<IConsumeExportService>(sp => sp.GetRequiredService<ConsumeExportBackgroundService>());
        services.AddHostedService(sp => sp.GetRequiredService<ConsumeExportBackgroundService>());

        // Batch publish background service
        services.AddSingleton<BatchPublishBackgroundService>();
        services.AddSingleton<IBatchPublishService>(sp => sp.GetRequiredService<BatchPublishBackgroundService>());
        services.AddHostedService(sp => sp.GetRequiredService<BatchPublishBackgroundService>());

        return services;
    }
}
