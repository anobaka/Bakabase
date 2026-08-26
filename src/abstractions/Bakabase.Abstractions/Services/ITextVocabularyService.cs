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

    /// <summary>
    /// Resolves a builtin's set. Purely a read: a builtin whose row does not exist yet resolves to
    /// an empty set rather than being created here, because consumers call this on hot paths where
    /// writing would race other consumers sharing the same scoped DbContext.
    /// <see cref="EnsureBuiltinTypes"/> at startup is what guarantees the rows exist.
    /// </summary>
    Task<TextSet> ResolveSet(WellKnownTextType wellKnown);

    /// <summary>Id of a builtin's row. Throws when it is missing (see <see cref="EnsureBuiltinTypes"/>).</summary>
    Task<int> GetTypeId(WellKnownTextType wellKnown);

    /// <summary>
    /// Creates any missing builtin type rows, without touching entries. Builtin types are defined
    /// in code, so their rows are an invariant: this runs once at startup, before anything reads
    /// them, and keeps the management page showing every builtin regardless of seeding history.
    /// </summary>
    Task EnsureBuiltinTypes();

    /// <summary>
    /// Tops up the prefab entries of builtin types — the "add prefabs" action. Deliberately
    /// separate from <see cref="EnsureBuiltinTypes"/> and never run automatically: entries are the
    /// user's data, and re-adding them on every launch would resurrect ones they deleted.
    /// </summary>
    Task AddPrefabEntries();
}
