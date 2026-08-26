using System.ComponentModel.DataAnnotations;

namespace Bakabase.Modules.Text.Abstractions.Models.Db;

/// <summary>
/// One entry of a <see cref="TextType"/>. What the two values mean is the type's
/// <see cref="Domain.Constants.TextTypeShape"/>; both slots exist regardless of shape so no data
/// is lost when a type's shape says only the first one is used.
/// </summary>
public record TextEntry
{
    [Key] public int Id { get; set; }

    public int TypeId { get; set; }

    [Required, MaxLength(64)] public string Value1 { get; set; } = null!;

    [MaxLength(64)] public string? Value2 { get; set; }
}
