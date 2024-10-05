namespace Bakabase.Modules.Property.Components.Properties.Choice.Abstractions;

public record ChoiceOptions
{
    public string Value { get; set; } = Guid.NewGuid().ToString();
    public string Label { get; set; } = null!;
    public string Color { get; set; } = null!;
}