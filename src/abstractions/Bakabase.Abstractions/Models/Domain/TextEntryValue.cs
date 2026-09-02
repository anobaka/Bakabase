namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// One entry of a text type, as exposed outside the vocabulary store.
/// </summary>
public record TextEntryValue
{
    public int Id { get; set; }

    public int TypeId { get; set; }

    public string Value1 { get; set; } = null!;

    /// <summary>
    /// Meaning depends on the type's shape; always nullable, including under shapes that only use
    /// the first value (historical rows may still carry one).
    /// </summary>
    public string? Value2 { get; set; }
}
