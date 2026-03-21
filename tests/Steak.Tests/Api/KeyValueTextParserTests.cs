using Steak.Host.Configuration;

namespace Steak.Tests.Api;

public sealed class KeyValueTextParserTests
{
    [Fact]
    public void Parse_SupportsCommentsEqualsAndColonSyntax()
    {
        var parsed = KeyValueTextParser.Parse(
            """
            # comment
            fetch.max.bytes=4096
            client.id: steak
            """);

        Assert.Equal("4096", parsed["fetch.max.bytes"]);
        Assert.Equal("steak", parsed["client.id"]);
    }

    [Fact]
    public void Format_SortsKeysAndParseRejectsMalformedLines()
    {
        var formatted = KeyValueTextParser.Format(new Dictionary<string, string>
        {
            ["z.last"] = "2",
            ["a.first"] = "1"
        });

        Assert.StartsWith("a.first=1", formatted);
        Assert.Contains($"{Environment.NewLine}z.last=2", formatted);
        Assert.Throws<FormatException>(() => KeyValueTextParser.Parse("missing separator"));
    }
}
