using Steak.Core.Contracts;
using Steak.Core.Services;

namespace Steak.Tests.Core;

public sealed class KafkaConfigurationServiceTests
{
    private readonly KafkaConfigurationService _service = new();

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
            SaslMechanism = "ScramSha256"
        };

        var config = _service.BuildConfig(
            settings,
            KafkaClientKind.Producer,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["client.id"] = "override-client",
                ["linger.ms"] = "7"
            });

        Assert.Equal("localhost:9092", config["bootstrap.servers"]);
        Assert.Equal("demo-user", config["client.id"]);
        Assert.Equal("7", config["linger.ms"]);
        Assert.Equal("secretpass", config["sasl.password"]);
        Assert.Equal("sasl_plaintext", config["security.protocol"]);
        Assert.Equal("SCRAM-SHA-256", config["sasl.mechanism"]);
    }

    [Fact]
    public void BuildConfig_DefaultsToSaslPlaintextAndScramSha256()
    {
        var settings = new KafkaConnectionSettings
        {
            BootstrapServers = "localhost:9092",
            Username = "demo-user",
            Password = "secretpass"
        };

        var config = _service.BuildConfig(settings, KafkaClientKind.Consumer);

        Assert.Equal("localhost:9092", config["bootstrap.servers"]);
        Assert.Equal("demo-user", config["sasl.username"]);
        Assert.Equal("secretpass", config["sasl.password"]);
        Assert.Equal("sasl_plaintext", config["security.protocol"]);
        Assert.Equal("SCRAM-SHA-256", config["sasl.mechanism"]);
    }

    [Fact]
    public void BuildConfig_NormalizesBootstrapServerLists()
    {
        var settings = new KafkaConnectionSettings
        {
            BootstrapServers = " broker-a:9092, broker-b:9092 ,,broker-c:9092 ",
            Username = "demo-user",
            Password = "secretpass"
        };

        var config = _service.BuildConfig(settings, KafkaClientKind.Consumer);

        Assert.Equal("broker-a:9092,broker-b:9092,broker-c:9092", config["bootstrap.servers"]);
    }

    [Fact]
    public void GetMaskedConfig_MasksSensitiveValues()
    {
        var settings = new KafkaConnectionSettings
        {
            BootstrapServers = "localhost:9092",
            Username = "demo-user",
            Password = "secretpass"
        };

        var config = _service.GetMaskedConfig(settings, KafkaClientKind.Consumer);

        Assert.Equal("se***ss", config["sasl.password"]);
    }

    [Theory]
    [InlineData("Plaintext", "plaintext")]
    [InlineData("plaintext", "plaintext")]
    [InlineData("SaslPlaintext", "sasl_plaintext")]
    [InlineData("sasl_ssl", "sasl_ssl")]
    [InlineData("SSL", "ssl")]
    public void BuildConfig_NormalizesSecurityProtocolTokens(string input, string expected)
    {
        var config = _service.BuildConfig(
            new KafkaConnectionSettings
            {
                BootstrapServers = "localhost:9092",
                SecurityProtocol = input,
                Username = "admin",
                Password = "secret"
            },
            KafkaClientKind.Admin);

        Assert.Equal(expected, config["security.protocol"]);
    }

    [Theory]
    [InlineData("Plain", "PLAIN")]
    [InlineData("plain", "PLAIN")]
    [InlineData("ScramSha256", "SCRAM-SHA-256")]
    [InlineData("scram-sha-512", "SCRAM-SHA-512")]
    [InlineData("OAuthBearer", "OAUTHBEARER")]
    [InlineData("Gssapi", "GSSAPI")]
    public void BuildConfig_NormalizesSaslMechanismTokens(string input, string expected)
    {
        var config = _service.BuildConfig(
            new KafkaConnectionSettings
            {
                BootstrapServers = "localhost:9092",
                SecurityProtocol = "SaslPlaintext",
                SaslMechanism = input,
                Username = "admin",
                Password = "secret"
            },
            KafkaClientKind.Admin);

        Assert.Equal(expected, config["sasl.mechanism"]);
    }

    [Fact]
    public void BuildConfig_OmitsSaslSettingsForPlaintext()
    {
        var config = _service.BuildConfig(
            new KafkaConnectionSettings
            {
                BootstrapServers = "localhost:9092",
                SecurityProtocol = "Plaintext",
                SaslMechanism = "ScramSha512",
                Username = "admin",
                Password = "secret"
            },
            KafkaClientKind.Admin);

        Assert.Equal("plaintext", config["security.protocol"]);
        Assert.DoesNotContain(config.Keys, key => string.Equals(key, "sasl.mechanism", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(config.Keys, key => string.Equals(key, "sasl.username", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(config.Keys, key => string.Equals(key, "sasl.password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildConfig_ThrowsWhenUsernameMissingForSaslConnection()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _service.BuildConfig(
                new KafkaConnectionSettings
                {
                    BootstrapServers = "localhost:9092",
                    SecurityProtocol = "SaslPlaintext",
                    Password = "secret"
                },
                KafkaClientKind.Admin));

        Assert.Contains("Username", ex.Message);
    }

    [Fact]
    public void BuildConfig_ThrowsWhenPasswordMissingForSaslConnection()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _service.BuildConfig(
                new KafkaConnectionSettings
                {
                    BootstrapServers = "localhost:9092",
                    SecurityProtocol = "SaslPlaintext",
                    Username = "admin"
                },
                KafkaClientKind.Admin));

        Assert.Contains("Password", ex.Message);
    }

    [Fact]
    public void BuildConfig_OmitsSaslSettingsForSslButKeepsSslFields()
    {
        var config = _service.BuildConfig(
            new KafkaConnectionSettings
            {
                BootstrapServers = "localhost:9092",
                SecurityProtocol = "Ssl",
                SaslMechanism = "Plain",
                Username = "admin",
                Password = "secret",
                SslCaPem = "ca-pem",
                SslCertificatePem = "cert-pem",
                SslKeyPem = "key-pem",
                SslKeyPassword = "key-pass"
            },
            KafkaClientKind.Consumer);

        Assert.Equal("ssl", config["security.protocol"]);
        Assert.Equal("ca-pem", config["ssl.ca.pem"]);
        Assert.Equal("cert-pem", config["ssl.certificate.pem"]);
        Assert.Equal("key-pem", config["ssl.key.pem"]);
        Assert.Equal("key-pass", config["ssl.key.password"]);
        Assert.DoesNotContain(config.Keys, key => string.Equals(key, "sasl.mechanism", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(config.Keys, key => string.Equals(key, "sasl.username", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(config.Keys, key => string.Equals(key, "sasl.password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetMaskedConfig_MasksEverySensitiveValue()
    {
        var config = _service.GetMaskedConfig(
            new KafkaConnectionSettings
            {
                BootstrapServers = "localhost:9092",
                SecurityProtocol = "SaslSsl",
                SaslMechanism = "Plain",
                Username = "admin",
                Password = "secretpass",
                SslCaPem = "ca-secret",
                SslCertificatePem = "cert-secret",
                SslKeyPem = "key-secret",
                SslKeyPassword = "pw-secret"
            },
            KafkaClientKind.Admin);

        Assert.Equal("se***ss", config["sasl.password"]);
        Assert.Equal("ca***et", config["ssl.ca.pem"]);
        Assert.Equal("ce***et", config["ssl.certificate.pem"]);
        Assert.Equal("ke***et", config["ssl.key.pem"]);
        Assert.Equal("pw***et", config["ssl.key.password"]);
    }
}
