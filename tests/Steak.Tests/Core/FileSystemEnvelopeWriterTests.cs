using System.Text;
using System.Text.Json;
using Steak.Core.Contracts;
using Steak.Core.Services;

namespace Steak.Tests.Core;

public sealed class FileSystemEnvelopeWriterTests
{
    [Fact]
    public async Task WriteEnvelopeAsync_PreservesLiteralPreviewCharactersInWrittenJson()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "Steak.Tests", nameof(FileSystemEnvelopeWriterTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);

        try
        {
            var payload = """{"comparison":">","html":"<tag>","ampersand":"&"}""";
            var valueBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
            var envelope = new SteakMessageEnvelope
            {
                App = "Steak",
                Topic = "orders",
                ValueBase64 = valueBase64,
                Preview = new MessagePreviewService().CreatePreview(null, valueBase64)
            };

            var writer = new FileSystemEnvelopeWriter();
            var destination = new BatchDestinationOptions
            {
                TransportKind = BatchTransportKind.FileSystem,
                FileSystem = new FileSystemLocationOptions
                {
                    Path = tempPath
                }
            };

            var filePath = await writer.WriteEnvelopeAsync(envelope, "sample.json", destination);
            var json = await File.ReadAllTextAsync(filePath);

            Assert.Contains(valueBase64, json);
            Assert.DoesNotContain("\\u003E", json);
            Assert.DoesNotContain("\\u003C", json);
            Assert.DoesNotContain("\\u0026", json);

            using var document = JsonDocument.Parse(json);
            var preview = document.RootElement.GetProperty("preview");
            Assert.Equal(payload, preview.GetProperty("valueUtf8Preview").GetString());

            var prettyJson = preview.GetProperty("valuePrettyJson").GetString();
            Assert.NotNull(prettyJson);
            Assert.Contains("\"comparison\": \">\"", prettyJson);
            Assert.Contains("\"html\": \"<tag>\"", prettyJson);
            Assert.Contains("\"ampersand\": \"&\"", prettyJson);
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, recursive: true);
            }
        }
    }
}
