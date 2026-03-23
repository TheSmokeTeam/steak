using Steak.Core.Contracts;

namespace Steak.Host.Configuration;

internal static class UiErrorMessage
{
    public static string Build(string operation, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(exception);

        return $"{operation.Trim()} failed. {SteakErrorDetails.Format(exception)}";
    }
}
