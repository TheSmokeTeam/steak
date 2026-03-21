using Steak.Core.Services;

namespace Steak.Tests.Core;

public sealed class MessagePreviewServiceTests
{
    [Fact]
    public void CreatePreview_DetectsUtf8AndPrettyJson()
    {
        var service = new MessagePreviewService();
        var payload = """
{"name":"steak","enabled":true}
""";

        var preview = service.CreatePreview(
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("key-1")),
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payload)));

        Assert.True(preview.KeyIsUtf8);
        Assert.True(preview.ValueIsUtf8);
        Assert.True(preview.ValueIsJson);
        Assert.Contains("\"name\": \"steak\"", preview.ValuePrettyJson);
        Assert.Contains("6B 65 79", preview.KeyHexPreview);
    }

    [Fact]
    public void CreatePreview_ReturnsDecodeErrorForInvalidBase64()
    {
        var service = new MessagePreviewService();

        var preview = service.CreatePreview("not-base64", "still-not-base64");

        Assert.NotNull(preview.KeyDecodeError);
        Assert.NotNull(preview.ValueDecodeError);
        Assert.False(preview.ValueIsUtf8);
    }

    [Fact]
    public void CreateHeaderPreview_PreservesBase64AndUtf8Preview()
    {
        var service = new MessagePreviewService();

        var header = service.CreateHeaderPreview("content-type", System.Text.Encoding.UTF8.GetBytes("application/json"));

        Assert.Equal("content-type", header.Key);
        Assert.Equal(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("application/json")), header.ValueBase64);
        Assert.Equal("application/json", header.Utf8Preview);
        Assert.True(header.IsUtf8);
        Assert.False(header.IsTruncated);
    }
}
