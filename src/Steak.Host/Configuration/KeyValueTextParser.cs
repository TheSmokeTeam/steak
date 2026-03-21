namespace Steak.Host.Configuration;

internal static class KeyValueTextParser
{
    public static Dictionary<string, string> Parse(string? input)
    {
        var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(input))
        {
            return results;
        }

        var lines = input.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.TrimEntries);
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex < 0)
            {
                separatorIndex = line.IndexOf(':');
            }

            if (separatorIndex <= 0)
            {
                throw new FormatException($"Invalid override on line {index + 1}. Use key=value.");
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            results[key] = value;
        }

        return results;
    }

    public static string Format(IReadOnlyDictionary<string, string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(Environment.NewLine, values.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}"));
    }
}
