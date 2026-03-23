using System.Text;

namespace Steak.Core.Contracts;

/// <summary>
/// Formats nested exception chains into operator-facing detail strings.
/// </summary>
public static class SteakErrorDetails
{
    /// <summary>
    /// Formats an exception chain using exception type names and messages.
    /// </summary>
    public static string Format(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var builder = new StringBuilder();
        var current = exception;
        var depth = 0;

        while (current is not null)
        {
            if (depth > 0)
            {
                builder.Append(" --> ");
            }

            var message = string.IsNullOrWhiteSpace(current.Message)
                ? "(no message)"
                : current.Message.Trim();

            builder.Append(current.GetType().Name);
            builder.Append(": ");
            builder.Append(message);

            current = current.InnerException;
            depth++;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Prefixes a formatted exception chain with operation context.
    /// </summary>
    public static string Format(string prefix, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        return $"{prefix.Trim()} {Format(exception)}";
    }
}
