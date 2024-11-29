using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Service.Models.View;

public record ResourceDiffViewModel
{
    public PropertyViewModel Property { get; set; } = null!;
    public string? Value1 { get; set; }
    public string? Value2 { get; set; }
}