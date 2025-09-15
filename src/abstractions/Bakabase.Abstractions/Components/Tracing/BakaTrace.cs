using Bootstrap.Models;
using Microsoft.Extensions.Logging;

namespace Bakabase.Abstractions.Components.Tracing;

public record BakaTrace
{
    public LogLevel Level { get; init; }
    public required string Topic { get; init; }
    public List<KeyValue<string, object?>>? Contexts { get; set; } = null;
}