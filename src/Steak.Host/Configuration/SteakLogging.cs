using Serilog;
using Serilog.Events;

namespace Steak.Host.Configuration;

internal static class SteakLogging
{
    private const string OutputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";

    public static Serilog.Core.Logger CreateBootstrapLogger(LogEventLevel minimumLevel)
    {
        return new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: OutputTemplate)
            .CreateLogger();
    }

    public static void Configure(
        LoggerConfiguration configuration,
        LogEventLevel applicationLevel,
        LogEventLevel frameworkLevel)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        configuration
            .MinimumLevel.Is(applicationLevel)
            .MinimumLevel.Override("Microsoft", frameworkLevel)
            .MinimumLevel.Override("Microsoft.AspNetCore", frameworkLevel)
            .MinimumLevel.Override("System", frameworkLevel)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: OutputTemplate);
    }
}
