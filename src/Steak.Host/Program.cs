using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Serilog;
using Serilog.Events;
using Steak.Core.Contracts;
using Steak.Core.Services;
using Steak.Host.Api;
using Steak.Host.Components;
using Steak.Host.Configuration;

namespace Steak.Host;

/// <summary>
/// Application entry point for the self-hosted Steak UI and API.
/// </summary>
public class Program
{
    /// <summary>
    /// Boots the ASP.NET Core host, the Blazor UI, and the local API surface.
    /// </summary>
    public static void Main(string[] args)
    {
        Log.Logger = SteakLogging.CreateBootstrapLogger(LogEventLevel.Information);

        try
        {
            var bootstrapLevel = SteakCommandLine.ParseCommandLineLogLevel(args) ?? LogEventLevel.Information;
            if (bootstrapLevel != LogEventLevel.Information)
            {
                Log.CloseAndFlush();
                Log.Logger = SteakLogging.CreateBootstrapLogger(bootstrapLevel);
            }

            var resolvedWebRootPath = TryResolveWebRootPath();
            var builderOptions = new WebApplicationOptions
            {
                Args = args,
                WebRootPath = resolvedWebRootPath
            };

            var builder = WebApplication.CreateBuilder(builderOptions);
            builder.Host.UseSerilog((context, _, configuration) =>
            {
                var minimumLevel = SteakCommandLine.ParseCommandLineLogLevel(args)
                    ?? SteakCommandLine.TryParseConfiguredLogLevel(context.Configuration["Logging:LogLevel:Default"])
                    ?? LogEventLevel.Information;
                var frameworkLevel = SteakCommandLine.TryParseConfiguredLogLevel(context.Configuration["Logging:LogLevel:Microsoft.AspNetCore"])
                    ?? LogEventLevel.Warning;

                SteakLogging.Configure(configuration, minimumLevel, frameworkLevel);
            });

            var isContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
            var runtimeOptions = builder.Configuration.GetSection("Steak:Runtime").Get<SteakRuntimeOptions>() ?? new SteakRuntimeOptions();
            var openBrowserOverride = SteakCommandLine.ParseOpenBrowser(args);

            var explicitUrls = SteakCommandLine.ParseUrls(args);
            var port = SteakCommandLine.ParsePort(args);
            var configuredUrls = ConfigureUrls(builder, runtimeOptions, isContainer, explicitUrls, port);
            var minimumConsoleLevel = SteakCommandLine.ParseCommandLineLogLevel(args)
                ?? SteakCommandLine.TryParseConfiguredLogLevel(builder.Configuration["Logging:LogLevel:Default"])
                ?? LogEventLevel.Information;
            var effectiveLaunchBrowser = openBrowserOverride ?? runtimeOptions.LaunchBrowser;

            var defaultDataRoot = ResolveDataRoot(builder.Environment, builder.Configuration, isContainer);
            var dataProtectionKeyRoot = Path.Combine(defaultDataRoot, "keys");

            Directory.CreateDirectory(dataProtectionKeyRoot);

            builder.Services.Configure<SteakRuntimeOptions>(builder.Configuration.GetSection("Steak:Runtime"));
            builder.Services.PostConfigure<SteakRuntimeOptions>(options =>
            {
                options.LocalUrl = string.IsNullOrWhiteSpace(options.LocalUrl) ? runtimeOptions.LocalUrl : options.LocalUrl;
                options.ContainerUrl = string.IsNullOrWhiteSpace(options.ContainerUrl) ? runtimeOptions.ContainerUrl : options.ContainerUrl;
                options.LaunchBrowser = openBrowserOverride ?? options.LaunchBrowser;
            });
            builder.Services.PostConfigure<SteakStorageOptions>(options =>
            {
                if (string.IsNullOrWhiteSpace(options.DataRoot))
                {
                    options.DataRoot = defaultDataRoot;
                }
            });

            builder.Services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyRoot))
                .SetApplicationName("Steak");
            builder.Services.AddProblemDetails();
            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();
            builder.Services.AddOpenApi();
            builder.Services.AddSwaggerGen();
            builder.Services.AddSteakCore(builder.Configuration);
            builder.Services.AddSingleton<ILocalFolderPicker, LocalFolderPicker>();
            builder.Services.AddHostedService<BrowserLaunchHostedService>();

            var app = builder.Build();

            app.Logger.LogInformation(
                "Steak host configured for {Urls} with minimum console level {LogLevel}. Browser launch enabled: {LaunchBrowser}. Container mode: {IsContainer}.",
                configuredUrls,
                minimumConsoleLevel,
                effectiveLaunchBrowser && !isContainer,
                isContainer);

            // Keep the runtime local-first but still expose enough diagnostics for a trusted operator workflow.
            app.UseExceptionHandler();
            app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

            app.MapOpenApi("/openapi/{documentName}.json");
            app.UseSwaggerUI(options =>
            {
                options.RoutePrefix = "swagger";
                options.SwaggerEndpoint("/openapi/v1.json", "Steak API v1");
                options.DocumentTitle = "Steak API";
            });

            app.UseAntiforgery();

            app.UseStaticFiles();
            app.MapSteakApi();
            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            app.Run();
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Steak terminated unexpectedly during startup.");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static string ConfigureUrls(WebApplicationBuilder builder, SteakRuntimeOptions runtimeOptions, bool isContainer, string? explicitUrls, int? port)
    {
        string urls;

        if (!string.IsNullOrWhiteSpace(explicitUrls))
        {
            urls = explicitUrls;
        }
        else if (port.HasValue)
        {
            var host = isContainer ? "0.0.0.0" : "127.0.0.1";
            urls = $"http://{host}:{port.Value}";
        }
        else
        {
            urls = isContainer ? runtimeOptions.ContainerUrl : runtimeOptions.LocalUrl;
        }

        builder.WebHost.UseUrls(urls);
        return urls;
    }

    private static string ResolveDataRoot(IHostEnvironment environment, IConfiguration configuration, bool isContainer)
    {
        var configured = configuration["Steak:Storage:DataRoot"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        if (isContainer)
        {
            return "/data";
        }

        var candidates = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                candidates.Add(Path.Combine(localAppData, "Steak"));
            }
        }

        candidates.Add(Path.Combine(AppContext.BaseDirectory, ".steak"));
        candidates.Add(Path.Combine(environment.ContentRootPath, ".steak"));
        candidates.Add(Path.Combine(Path.GetTempPath(), "Steak"));

        foreach (var candidate in candidates)
        {
            if (TryEnsureWritableDirectory(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Steak could not find a writable data directory.");
    }

    private static string? TryResolveWebRootPath()
    {
        var depsRoot = TryResolveDepsRoot();
        if (!string.IsNullOrWhiteSpace(depsRoot))
        {
            if (File.Exists(Path.Combine(depsRoot, "app.css")))
            {
                return depsRoot;
            }

            var depsWwwroot = Path.Combine(depsRoot, "wwwroot");
            if (Directory.Exists(depsWwwroot))
            {
                return depsWwwroot;
            }
        }

        var appBaseWwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (Directory.Exists(appBaseWwwroot))
        {
            return appBaseWwwroot;
        }

        return null;
    }

    private static string? TryResolveDepsRoot()
    {
        var depsFilesValue = AppDomain.CurrentDomain.GetData("APP_CONTEXT_DEPS_FILES") as string;
        if (string.IsNullOrWhiteSpace(depsFilesValue))
        {
            return null;
        }

        var firstDepsFile = depsFilesValue
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(firstDepsFile) ? null : Path.GetDirectoryName(firstDepsFile);
    }

    private static bool TryEnsureWritableDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);

            var probePath = Path.Combine(path, $".steak-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probePath, string.Empty);
            File.Delete(probePath);

            return true;
        }
        catch
        {
            return false;
        }
    }
}
