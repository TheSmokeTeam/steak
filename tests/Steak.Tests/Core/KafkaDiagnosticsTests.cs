using Steak.Core.Contracts;
using Steak.Core.Services;

namespace Steak.Tests.Core;

public sealed class KafkaDiagnosticsTests
{
    [Fact]
    public void FormatSettings_IncludesFullCredentialsAndSortedOverrides()
    {
        var settings = new KafkaConnectionSettings
        {
            BootstrapServers = "localhost:9092",
            Username = "admin",
            Password = "secret",
            SecurityProtocol = "SaslSsl",
            SaslMechanism = "Plain",
            AdvancedOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["zeta"] = "last",
                ["alpha"] = "first"
            }
        };

        var payload = KafkaDiagnostics.FormatSettings(settings);

        Assert.Contains("\"password\":\"secret\"", payload);
        Assert.Contains("\"advancedOverrides\":{\"alpha\":\"first\",\"zeta\":\"last\"}", payload);
    }

    [Fact]
    public void FormatEnvelopeSummary_UsesNullForInvalidBase64Lengths()
    {
        var payload = KafkaDiagnostics.FormatEnvelopeSummary(new SteakMessageEnvelope
        {
            Topic = "orders",
            KeyBase64 = "invalid-base64",
            ValueBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 })
        });

        Assert.Contains("\"keyBytes\":null", payload);
        Assert.Contains("\"valueBytes\":4", payload);
    }
}
