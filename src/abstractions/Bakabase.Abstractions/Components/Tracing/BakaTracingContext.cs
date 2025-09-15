using System.Threading.Channels;
using Bootstrap.Models;
using Microsoft.Extensions.Logging;

namespace Bakabase.Abstractions.Components.Tracing;

public class BakaTracingContext
{
    private readonly Channel<BakaTrace> _channel = Channel.CreateUnbounded<BakaTrace>();

    public void AddTrace(LogLevel level, string topic) => AddTrace(level, topic, (string[]?)null);

    public void AddTrace(LogLevel level, string topic, params string[]? context) =>
        AddTrace(level, topic, context?.Select(c => (c, (object?)null)).ToArray());

    public void AddTrace(LogLevel level, string topic,
        params (string, object?)[]? context)
    {
        AddTrace(new BakaTrace
        {
            Level = level,
            Topic = topic,
            Contexts = context?.Select(c => new KeyValue<string, object?>(c.Item1, c.Item2)).ToList()
        });
    }

    private void AddTrace(BakaTrace trace)
    {
        _channel.Writer.TryWrite(trace);
    }

    public IAsyncEnumerable<BakaTrace> ReadTracesAsync(CancellationToken ct = default)
    {
        return _channel.Reader.ReadAllAsync(ct);
    }

    public void Complete()
    {
        _channel.Writer.TryComplete();
    }
}