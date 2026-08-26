using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Services;

/// <summary>
/// Owns text types and their entries: what words exist. What is done with those words lives in
/// <see cref="Components.Text.ITextOps"/>.
///
/// Builtin and user-defined types are the same kind of row here — <see cref="GetTypes"/> is the
/// one list every picker and management view consumes.
/// </summary>
public interface ITextVocabularyService
{
    Task<List<TextTypeDescriptor>> GetTypes();

    Task<TextTypeDescriptor> AddType(string name, TextTypeShape shape, string? description = null);

    /// <summary>Throws for builtin types.</summary>
    Task RenameType(int id, string name);

    /// <summary>Throws for builtin types.</summary>
    Task DeleteType(int id);

    Task<List<TextEntryValue>> GetEntries(int typeId);

    Task<TextEntryValue> AddEntry(int typeId, string value1, string? value2 = null);

    Task PatchEntry(int id, string? value1, string? value2);

    Task DeleteEntry(int id);

    Task AddEntries(int typeId, IEnumerable<(string Value1, string? Value2)> entries);

    /// <summary>
    /// Resolve a type's entries into the form its shape implies. The single "read the words"
    /// entry point for consumers.
    /// </summary>
    Task<TextSet> ResolveSet(int typeId);

    /// <inheritdoc cref="ResolveSet(int)"/>
    Task<TextSet> ResolveSet(WellKnownTextType wellKnown);

    Task<int> GetTypeId(WellKnownTextType wellKnown);

    /// <summary>
    /// Ensures builtin types exist and tops up their prefab entries. Idempotent: an entry already
    /// present under its type is left alone, so user deletions of prefab entries are not undone
    /// within a run and re-adding stays a no-op.
    /// </summary>
    Task EnsureSeeds();
}
