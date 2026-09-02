using System.ComponentModel.DataAnnotations;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Service.Models.Input;

public record TextTypeAddInputModel
{
    [Required, MaxLength(64)] public string Name { get; set; } = null!;

    public TextTypeShape Shape { get; set; } = TextTypeShape.Values;

    [MaxLength(256)] public string? Description { get; set; }
}

public record TextTypePatchInputModel
{
    [Required, MaxLength(64)] public string Name { get; set; } = null!;
}

public record TextEntryAddInputModel
{
    [Required, MaxLength(64)] public string Value1 { get; set; } = null!;

    [MaxLength(64)] public string? Value2 { get; set; }
}

public record TextEntryPatchInputModel
{
    [MaxLength(64)] public string? Value1 { get; set; }

    [MaxLength(64)] public string? Value2 { get; set; }
}
