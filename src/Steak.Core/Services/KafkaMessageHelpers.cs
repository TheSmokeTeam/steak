using Confluent.Kafka;
using Steak.Core.Contracts;

namespace Steak.Core.Services;

internal static class KafkaMessageHelpers
{
    public static AutoOffsetReset ToAutoOffsetReset(this MessageOffsetMode mode)
    {
        return mode switch
        {
            MessageOffsetMode.Earliest => AutoOffsetReset.Earliest,
            MessageOffsetMode.Latest => AutoOffsetReset.Latest,
            _ => AutoOffsetReset.Latest
        };
    }

    public static Offset ToOffset(this MessageOffsetMode mode)
    {
        return mode switch
        {
            MessageOffsetMode.Earliest => Offset.Beginning,
            MessageOffsetMode.Latest => Offset.End,
            _ => Offset.Stored
        };
    }

    public static Headers BuildHeaders(IEnumerable<SteakMessageHeader> headers)
    {
        var kafkaHeaders = new Headers();
        foreach (var header in headers)
        {
            if (string.IsNullOrWhiteSpace(header.Key))
            {
                continue;
            }

            byte[]? bytes = null;
            if (!string.IsNullOrWhiteSpace(header.ValueBase64))
            {
                bytes = Convert.FromBase64String(header.ValueBase64);
            }

            kafkaHeaders.Add(header.Key.Trim(), bytes);
        }

        return kafkaHeaders;
    }
}
