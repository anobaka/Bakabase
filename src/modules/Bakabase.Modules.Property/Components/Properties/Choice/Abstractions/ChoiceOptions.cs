namespace Bakabase.Modules.Property.Components.Properties.Choice.Abstractions;

public record ChoiceOptions
{
    public string Value { get; init; } = Guid.NewGuid().ToString();
    public string Label { get; init; } = null!;
    public string? Color { get; init; }
}