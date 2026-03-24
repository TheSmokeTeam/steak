using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Steak.Core.Contracts;
using Steak.Tests.Api;

namespace Steak.Tests.Host;

public sealed class UiContractTests : IClassFixture<SteakApiTests.TestAppFactory>
{
    private readonly SteakApiTests.TestAppFactory _factory;

    public UiContractTests(SteakApiTests.TestAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HomePage_DisconnectedState_DoesNotRenderLegacyIdLabels()
    {
        using var client = _factory.CreateClient();
        await client.DeleteAsync("/api/connection");

        var html = await client.GetStringAsync("/");

        Assert.False(ContainsLabel(html, "Client ID"));
        Assert.False(ContainsLabel(html, "Group ID"));
    }

    [Fact]
    public async Task HomePage_ConnectionForm_ListsScramSha256BeforeScramSha512()
    {
        using var client = _factory.CreateClient();
        await client.DeleteAsync("/api/connection");

        var html = await client.GetStringAsync("/");
        var scram256Index = html.IndexOf("SCRAM-SHA-256", StringComparison.OrdinalIgnoreCase);
        var scram512Index = html.IndexOf("SCRAM-SHA-512", StringComparison.OrdinalIgnoreCase);

        Assert.True(scram256Index >= 0, "SCRAM-SHA-256 should be present on the home page.");
        Assert.True(scram512Index > scram256Index, "SCRAM-SHA-256 should appear before SCRAM-SHA-512.");
    }

    [Fact]
    public async Task HomePage_ConnectedState_DoesNotRenderLegacyIdLabels()
    {
        using var client = _factory.CreateClient();
        await client.DeleteAsync("/api/connection");

        var connectResponse = await client.PostAsJsonAsync("/api/connection", new ConnectRequest
        {
            Settings = new KafkaConnectionSettings
            {
                BootstrapServers = "localhost:9092",
                SecurityProtocol = "Plaintext",
                Username = "ui-user"
            }
        });
        connectResponse.EnsureSuccessStatusCode();

        var html = await client.GetStringAsync("/");

        Assert.Contains("Live message viewer", html, StringComparison.OrdinalIgnoreCase);
        Assert.False(ContainsLabel(html, "Client ID"));
        Assert.False(ContainsLabel(html, "Group ID"));
    }

    private static bool ContainsLabel(string html, string label)
    {
        return Regex.IsMatch(
            html,
            $@">\s*{Regex.Escape(label)}\s*<",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
