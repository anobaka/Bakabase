namespace Bakabase.Abstractions.Components.Tracing;

public static class ScopedBakaTracingContextAccessor
{
    public static readonly AsyncLocal<BakaTracingContext?> Current = new();
}