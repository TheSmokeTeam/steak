namespace Steak.Host.Configuration;

/// <summary>
/// Configures how the Steak host binds locally and behaves when launched as a desktop workflow.
/// </summary>
public sealed class SteakRuntimeOptions
{
    /// <summary>
    /// Default localhost URL used by the Windows launcher and <c>dotnet run</c>.
    /// </summary>
    public string LocalUrl { get; set; } = "http://127.0.0.1:4040";

    /// <summary>
    /// Default container bind URL.
    /// </summary>
    public string ContainerUrl { get; set; } = "http://0.0.0.0:8080";

    /// <summary>
    /// Opens the system browser automatically when Steak starts outside a container.
    /// </summary>
    public bool LaunchBrowser { get; set; } = false;
}
