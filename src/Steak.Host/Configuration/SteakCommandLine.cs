using Serilog.Events;

namespace Steak.Host.Configuration;

internal static class SteakCommandLine
{
    public static string? ParseUrls(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return TryGetValue(args, "--urls");
    }

    public static int? ParsePort(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var value = TryGetValue(args, "--port");
        return int.TryParse(value, out var port) ? port : null;
    }

    public static LogEventLevel? ParseCommandLineLogLevel(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var value = TryGetValue(args, "--log-level");
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return ParseLogLevelOrThrow(value, "--log-level");
    }

    public static bool? ParseOpenBrowser(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var value = TryGetValue(args, "--open-browser");
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return ParseBooleanOrThrow(value, "--open-browser");
    }

    public static LogEventLevel? TryParseConfiguredLogLevel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return TryParseLogLevel(value, out var level) ? level : null;
    }

    private static string? TryGetValue(string[] args, string switchName)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], switchName, StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                return args[index + 1];
            }

            if (args[index].StartsWith($"{switchName}=", StringComparison.OrdinalIgnoreCase))
            {
                return args[index][(switchName.Length + 1)..];
            }
        }

        return null;
    }

    private static LogEventLevel ParseLogLevelOrThrow(string value, string switchName)
    {
        if (TryParseLogLevel(value, out var level))
        {
            return level;
        }

        throw new InvalidOperationException(
            $"Unsupported value '{value}' for {switchName}. Supported values: trace, verbose, debug, information, info, warning, warn, error, critical, fatal.");
    }

    private static bool ParseBooleanOrThrow(string value, string switchName)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "true" or "1" or "yes" or "y" => true,
            "false" or "0" or "no" or "n" => false,
            _ => throw new InvalidOperationException(
                $"Unsupported value '{value}' for {switchName}. Supported values: true, false, yes, no, 1, 0.")
        };
    }

    private static bool TryParseLogLevel(string value, out LogEventLevel level)
    {
        var normalized = value.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "trace":
            case "verbose":
                level = LogEventLevel.Verbose;
                return true;
            case "debug":
                level = LogEventLevel.Debug;
                return true;
            case "information":
            case "info":
                level = LogEventLevel.Information;
                return true;
            case "warning":
            case "warn":
                level = LogEventLevel.Warning;
                return true;
            case "error":
                level = LogEventLevel.Error;
                return true;
            case "critical":
            case "fatal":
                level = LogEventLevel.Fatal;
                return true;
            default:
                level = default;
                return false;
        }
    }
}
