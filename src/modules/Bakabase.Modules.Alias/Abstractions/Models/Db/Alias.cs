using System.ComponentModel.DataAnnotations;

namespace Bakabase.Modules.Alias.Abstractions.Models.Db;

public record Alias
{
    [Key] public int Id { get; set; }
    [MaxLength(256)] public string Text { get; set; } = null!;

    /// <summary>
    /// <see cref="Preferred"/> equals <see cref="Text"/> by default.
    /// </summary>
    [MaxLength(256)]
    public string? Preferred { get; set; }
}