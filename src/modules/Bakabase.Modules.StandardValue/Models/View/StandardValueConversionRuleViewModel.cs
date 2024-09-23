using Bakabase.Modules.StandardValue.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.StandardValue.Models.View;

public record StandardValueConversionRuleViewModel
{
    public StandardValueConversionRule Rule { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
}