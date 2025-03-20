namespace Bakabase.Abstractions.Components.Tasks;

public record BTaskEvent<TEvent>(DateTime DateTime, TEvent Event);