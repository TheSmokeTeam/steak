using Steak.Core.Contracts;

namespace Steak.Core.Services;

internal static class KafkaConnectionIdentity
{
    public static string ResolveClientId(KafkaConnectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return ResolveIdentity(settings);
    }

    public static string ResolveConsumerGroupId(KafkaConnectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return ResolveIdentity(settings);
    }

    private static string ResolveIdentity(KafkaConnectionSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Username))
        {
            return settings.Username.Trim();
        }

        if (!string.IsNullOrWhiteSpace(Environment.UserName))
        {
            return Environment.UserName.Trim();
        }

        return "steak";
    }
}
