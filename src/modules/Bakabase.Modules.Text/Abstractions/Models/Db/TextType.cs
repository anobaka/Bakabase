using System.ComponentModel.DataAnnotations;
using Bakabase.Modules.Text.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Text.Abstractions.Models.Db;

/// <summary>
/// A named set of text entries. Builtin and user-defined types share this table and a single id
/// space, so anything referencing a type — a workflow node's config, a picker, an API — only ever
/// deals with one integer.
/// </summary>
public record TextType
{
    [Key] public int Id { get; set; }

    /// <summary>
    /// Canonical name. For builtins this is a stable English name and the UI shows a localized
    /// label keyed by <see cref="WellKnown"/> instead, so UI copy never lands in the database.
    /// </summary>
    [Required, MaxLength(64)]
    public string Name { get; set; } = null!;

    /// <summary>
    /// Non-null marks a builtin: consumption sites resolve it by this handle, and the type cannot
    /// be renamed or deleted. Entries remain editable either way.
    /// </summary>
    public WellKnownTextType? WellKnown { get; set; }

    public TextTypeShape Shape { get; set; }

    [MaxLength(256)] public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }
}
