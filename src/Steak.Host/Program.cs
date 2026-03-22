using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
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
        var resolvedWebRootPath = TryResolveWebRootPath();
        var builderOptions = new WebApplicationOptions
        {
            Args = args,
            WebRootPath = resolvedWebRootPath
        };

        var builder = WebApplication.CreateBuilder(builderOptions);
        var isContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
        var runtimeOptions = builder.Configuration.GetSection("Steak:Runtime").Get<SteakRuntimeOptions>() ?? new SteakRuntimeOptions();

        var explicitUrls = ParseUrls(args);
        var port = ParsePort(args);
        ConfigureUrls(builder, runtimeOptions, isContainer, explicitUrls, port);

        var defaultDataRoot = ResolveDataRoot(builder.Environment, builder.Configuration, isContainer);
        var dataProtectionKeyRoot = Path.Combine(defaultDataRoot, "keys");

        Directory.CreateDirectory(dataProtectionKeyRoot);

        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole();
        builder.Logging.AddDebug();

        builder.Services.Configure<SteakRuntimeOptions>(builder.Configuration.GetSection("Steak:Runtime"));
        builder.Services.PostConfigure<SteakRuntimeOptions>(options =>
        {
            options.LocalUrl = string.IsNullOrWhiteSpace(options.LocalUrl) ? runtimeOptions.LocalUrl : options.LocalUrl;
            options.ContainerUrl = string.IsNullOrWhiteSpace(options.ContainerUrl) ? runtimeOptions.ContainerUrl : options.ContainerUrl;
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
        builder.Services.AddHostedService<BrowserLaunchHostedService>();

        var app = builder.Build();

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

    private static void ConfigureUrls(WebApplicationBuilder builder, SteakRuntimeOptions runtimeOptions, bool isContainer, string? explicitUrls, int? port)
    {
        if (!string.IsNullOrWhiteSpace(explicitUrls))
        {
            builder.WebHost.UseUrls(explicitUrls);
            return;
        }

        if (port.HasValue)
        {
            var host = isContainer ? "0.0.0.0" : "127.0.0.1";
            builder.WebHost.UseUrls($"http://{host}:{port.Value}");
            return;
        }

        builder.WebHost.UseUrls(isContainer ? runtimeOptions.ContainerUrl : runtimeOptions.LocalUrl);
    }

    private static string? ParseUrls(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--urls" && i + 1 < args.Length)
            {
                return args[i + 1];
            }

            if (args[i].StartsWith("--urls=", StringComparison.OrdinalIgnoreCase))
            {
                return args[i]["--urls=".Length..];
            }
        }

        return null;
    }

    private static int? ParsePort(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--port" && i + 1 < args.Length && int.TryParse(args[i + 1], out var p))
                return p;
            if (args[i].StartsWith("--port=", StringComparison.OrdinalIgnoreCase))
            {
                var value = args[i]["--port=".Length..];
                if (int.TryParse(value, out var p2))
                    return p2;
            }
        }
        return null;
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
