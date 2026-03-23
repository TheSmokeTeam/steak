using System.Diagnostics;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;

namespace Steak.Host.Configuration;

internal sealed class BrowserLaunchHostedService(
    IHostApplicationLifetime applicationLifetime,
    IServer server,
    IOptions<SteakRuntimeOptions> runtimeOptions,
    ILogger<BrowserLaunchHostedService> logger) : IHostedService
{
    private IDisposable? _registration;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _registration = applicationLifetime.ApplicationStarted.Register(OpenBrowserIfNeeded);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _registration?.Dispose();
        _registration = null;
        return Task.CompletedTask;
    }

    private void OpenBrowserIfNeeded()
    {
        if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
        {
            logger.LogDebug("Skipping browser launch because Steak is running in a container");
            return;
        }

        if (!runtimeOptions.Value.LaunchBrowser)
        {
            logger.LogDebug("Skipping browser launch because Steak:Runtime:LaunchBrowser is disabled");
            return;
        }

        var address = server.Features.Get<IServerAddressesFeature>()?.Addresses
            .FirstOrDefault(candidate => candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            ?? runtimeOptions.Value.LocalUrl;

        try
        {
            logger.LogDebug("Launching Steak browser UI at {Address}", address);
            Process.Start(new ProcessStartInfo
            {
                FileName = address,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to launch Steak browser UI at {Address}", address);
        }
    }
}
