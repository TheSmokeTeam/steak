using Steak.Core.Contracts;
using Steak.Core.Services;

namespace Steak.Tests.Core;

public sealed class KafkaConfigurationServiceTests
{
    [Fact]
    public void BuildConfig_MergesSettingsAndOverrides()
    {
        var settings = new KafkaConnectionSettings
        {
            BootstrapServers = "localhost:9092",
            ClientId = "profile-client",
            Username = "demo-user",
            Password = "secretpass",
            SecurityProtocol = "SaslPlaintext",
            SaslMechanism = "ScramSha512"
        };

        var service = new KafkaConfigurationService();

        var config = service.BuildConfig(
            settings,
            KafkaClientKind.Producer,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["client.id"] = "override-client",
                ["linger.ms"] = "7"
            });

        Assert.Equal("localhost:9092", config["bootstrap.servers"]);
        Assert.Equal("override-client", config["client.id"]);
        Assert.Equal("7", config["linger.ms"]);
        Assert.Equal("secretpass", config["sasl.password"]);
        Assert.Equal("sasl_plaintext", config["security.protocol"]);
        Assert.Equal("SCRAM-SHA-512", config["sasl.mechanism"]);
    }

    [Fact]
    public void BuildConfig_OmitsBlankOptionalSecuritySettings()
    {
        var settings = new KafkaConnectionSettings
        {
            BootstrapServers = "localhost:9092"
        };

        var service = new KafkaConfigurationService();

        var config = service.BuildConfig(settings, KafkaClientKind.Consumer);

        Assert.Equal("localhost:9092", config["bootstrap.servers"]);
        Assert.Equal("sasl_plaintext", config["security.protocol"]);
        Assert.Equal("SCRAM-SHA-512", config["sasl.mechanism"]);
    }

    [Fact]
    public void BuildConfig_NormalizesBootstrapServerLists()
    {
        var settings = new KafkaConnectionSettings
        {
            BootstrapServers = " broker-a:9092, broker-b:9092 ,,broker-c:9092 "
        };

        var service = new KafkaConfigurationService();

        var config = service.BuildConfig(settings, KafkaClientKind.Consumer);

        Assert.Equal("broker-a:9092,broker-b:9092,broker-c:9092", config["bootstrap.servers"]);
    }

    [Fact]
    public void GetMaskedConfig_MasksSensitiveValues()
    {
        var settings = new KafkaConnectionSettings
        {
            BootstrapServers = "localhost:9092",
            Password = "secretpass"
        };

        var service = new KafkaConfigurationService();

        var config = service.GetMaskedConfig(settings, KafkaClientKind.Consumer);

        Assert.Equal("se***ss", config["sasl.password"]);
    }
}
