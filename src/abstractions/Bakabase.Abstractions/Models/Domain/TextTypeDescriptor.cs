using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// A text type as everything outside the vocabulary store sees it: builtin and user-defined rows
/// in one shape, so pickers and management UIs consume a single list and reference a single id.
/// </summary>
public record TextTypeDescriptor
{
    public int Id { get; set; }

    /// <summary>
    /// Canonical name. Builtin rows keep a stable English name here and are displayed through a
    /// label localized by <see cref="WellKnown"/>, so UI copy never lands in the database.
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// Non-null marks a builtin: it cannot be renamed or deleted, and consumption sites resolve
    /// it by this handle rather than by id.
    /// </summary>
    public WellKnownTextType? WellKnown { get; set; }

    public TextTypeShape Shape { get; set; }

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }

    public int EntryCount { get; set; }

    public bool IsBuiltin => WellKnown.HasValue;
}
