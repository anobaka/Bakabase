using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Text.Components;
using Bakabase.Modules.Text.Extensions;
using Bootstrap.Components.Orm;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Modules.Text.Services;

public class TextVocabularyService<TDbContext>(
    FullMemoryCacheResourceService<TDbContext, Abstractions.Models.Db.TextType, int> typeOrm,
    FullMemoryCacheResourceService<TDbContext, Abstractions.Models.Db.TextEntry, int> entryOrm)
    : ITextVocabularyService
    where TDbContext : DbContext
{
    public async Task<List<TextTypeDescriptor>> GetTypes()
    {
        var types = await typeOrm.GetAll();
        var entries = await entryOrm.GetAll();
        var countByType = entries.GroupBy(e => e.TypeId).ToDictionary(g => g.Key, g => g.Count());
        return types
            .OrderByDescending(t => t.WellKnown.HasValue)
            .ThenBy(t => t.Id)
            .Select(t => t.ToDescriptor(countByType.GetValueOrDefault(t.Id)))
            .ToList();
    }

    public async Task<TextTypeDescriptor> AddType(string name, TextTypeShape shape, string? description = null)
    {
        name = name.Trim();
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Text type name cannot be empty.", nameof(name));
        }

        var duplicate = await typeOrm.GetFirstOrDefault(t => t.Name == name);
        if (duplicate != null)
        {
            throw new InvalidOperationException($"Text type [{name}] already exists.");
        }

        var added = (await typeOrm.Add(new Abstractions.Models.Db.TextType
        {
            Name = name,
            Shape = shape,
            Description = description,
            CreatedAt = DateTime.Now
        })).Data;

        return added.ToDescriptor(0);
    }

    public async Task RenameType(int id, string name)
    {
        var type = await GetTypeOrThrow(id);
        EnsureNotBuiltin(type, "renamed");

        name = name.Trim();
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Text type name cannot be empty.", nameof(name));
        }

        var duplicate = await typeOrm.GetFirstOrDefault(t => t.Name == name && t.Id != id);
        if (duplicate != null)
        {
            throw new InvalidOperationException($"Text type [{name}] already exists.");
        }

        await typeOrm.UpdateByKey(id, t => t.Name = name);
    }

    public async Task DeleteType(int id)
    {
        var type = await GetTypeOrThrow(id);
        EnsureNotBuiltin(type, "deleted");

        await entryOrm.RemoveAll(e => e.TypeId == id);
        await typeOrm.RemoveByKey(id);
    }

    public async Task<List<TextEntryValue>> GetEntries(int typeId)
    {
        var entries = await entryOrm.GetAll(e => e.TypeId == typeId);
        return entries.Select(e => e.ToDomainModel()).ToList();
    }

    public async Task<TextEntryValue> AddEntry(int typeId, string value1, string? value2 = null)
    {
        await GetTypeOrThrow(typeId);
        var added = (await entryOrm.Add(new Abstractions.Models.Db.TextEntry
        {
            TypeId = typeId,
            Value1 = value1,
            Value2 = value2
        })).Data;
        return added.ToDomainModel();
    }

    public async Task PatchEntry(int id, string? value1, string? value2)
    {
        await entryOrm.UpdateByKey(id, e =>
        {
            if (!string.IsNullOrEmpty(value1))
            {
                e.Value1 = value1;
            }

            if (value2 != null)
            {
                e.Value2 = value2.Length == 0 ? null : value2;
            }
        });
    }

    public async Task DeleteEntry(int id) => await entryOrm.RemoveByKey(id);

    public async Task AddEntries(int typeId, IEnumerable<(string Value1, string? Value2)> entries)
    {
        await GetTypeOrThrow(typeId);
        var models = entries.Select(e => new Abstractions.Models.Db.TextEntry
        {
            TypeId = typeId,
            Value1 = e.Value1,
            Value2 = e.Value2
        }).ToList();
        if (models.Count > 0)
        {
            await entryOrm.AddRange(models);
        }
    }

    public async Task<TextSet> ResolveSet(int typeId)
    {
        var type = await GetTypeOrThrow(typeId);
        var entries = await entryOrm.GetAll(e => e.TypeId == typeId);
        return BuildSet(type, entries);
    }

    public async Task<TextSet> ResolveSet(WellKnownTextType wellKnown)
    {
        var type = await FindBuiltin(wellKnown);
        if (type == null)
        {
            // Before startup seeding has run (or if a row was removed out of band) a builtin simply
            // has nothing to say — same as the empty list the old type-filtered query returned.
            // Creating it here instead would write on a read path, and consumers share a scoped
            // DbContext, so two of them resolving at once would collide mid-SaveChanges.
            return new TextSet {Shape = TextTypeShape.Values};
        }

        var entries = await entryOrm.GetAll(e => e.TypeId == type.Id);
        return BuildSet(type, entries);
    }

    public async Task<int> GetTypeId(WellKnownTextType wellKnown)
    {
        var type = await FindBuiltin(wellKnown);
        if (type == null)
        {
            throw new KeyNotFoundException(
                $"Builtin text type [{wellKnown}] is missing; startup seeding should have created it.");
        }

        return type.Id;
    }

    public async Task EnsureBuiltinTypes()
    {
        await MergeDuplicateBuiltins();

        var existing = (await typeOrm.GetAll())
            .Where(t => t.WellKnown.HasValue)
            .Select(t => t.WellKnown!.Value)
            .ToHashSet();

        var missing = TextSeedData.BuiltinTypes
            .Where(b => !existing.Contains(b.WellKnown))
            .Select(b => new Abstractions.Models.Db.TextType
            {
                Name = b.Name,
                WellKnown = b.WellKnown,
                Shape = b.Shape,
                CreatedAt = DateTime.Now
            })
            .ToList();

        if (missing.Count > 0)
        {
            await typeOrm.AddRange(missing);
        }
    }

    /// <summary>
    /// Collapses duplicate rows for a builtin onto the oldest one, moving any entries across. An
    /// earlier build provisioned builtins lazily on read, which could race into two rows for the
    /// same handle; repairing here rather than with a unique index keeps such a database able to
    /// start at all, since schema migrations run before this could clean up after them.
    /// </summary>
    private async Task MergeDuplicateBuiltins()
    {
        var duplicateGroups = (await typeOrm.GetAll())
            .Where(t => t.WellKnown.HasValue)
            .GroupBy(t => t.WellKnown!.Value)
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var group in duplicateGroups)
        {
            var ordered = group.OrderBy(t => t.Id).ToList();
            var survivor = ordered[0];
            foreach (var duplicate in ordered.Skip(1))
            {
                var entries = await entryOrm.GetAll(e => e.TypeId == duplicate.Id);
                foreach (var entry in entries)
                {
                    await entryOrm.UpdateByKey(entry.Id, e => e.TypeId = survivor.Id);
                }

                await typeOrm.RemoveByKey(duplicate.Id);
            }
        }
    }

    public async Task AddPrefabEntries()
    {
        await EnsureBuiltinTypes();

        var typeByWellKnown = (await typeOrm.GetAll())
            .Where(t => t.WellKnown.HasValue)
            // Grouped rather than keyed directly: a duplicate builtin row must degrade into
            // picking one, not into throwing halfway through the user's action.
            .GroupBy(t => t.WellKnown!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var builtin in TextSeedData.BuiltinTypes)
        {
            if (builtin.Entries.Count == 0 || !typeByWellKnown.TryGetValue(builtin.WellKnown, out var type))
            {
                continue;
            }

            // Only rows absent right now are added, so entries the user deleted stay deleted for
            // this run; the comparison keys on both values so pair shapes do not collapse into one.
            var existingEntries = (await entryOrm.GetAll(e => e.TypeId == type.Id))
                .Select(e => (e.Value1, e.Value2))
                .ToHashSet();
            var missing = builtin.Entries.Where(e => !existingEntries.Contains((e.Value1, e.Value2))).ToList();
            if (missing.Count > 0)
            {
                await AddEntries(type.Id, missing);
            }
        }
    }

    private static TextSet BuildSet(Abstractions.Models.Db.TextType type,
        List<Abstractions.Models.Db.TextEntry> entries)
    {
        var pairs = type.Shape == TextTypeShape.Values
            ? []
            : entries.Where(e => !string.IsNullOrEmpty(e.Value2))
                .Select(e => new TextPair(e.Value1, e.Value2!))
                .ToList();

        return new TextSet
        {
            TypeId = type.Id,
            Shape = type.Shape,
            Values = entries.Select(e => e.Value1).ToList(),
            Pairs = pairs
        };
    }

    private async Task<Abstractions.Models.Db.TextType> GetTypeOrThrow(int id)
    {
        var type = await typeOrm.GetByKey(id);
        if (type == null)
        {
            throw new KeyNotFoundException($"Text type [{id}] does not exist.");
        }

        return type;
    }

    private async Task<Abstractions.Models.Db.TextType?> FindBuiltin(WellKnownTextType wellKnown) =>
        await typeOrm.GetFirstOrDefault(t => t.WellKnown == wellKnown);

    private static void EnsureNotBuiltin(Abstractions.Models.Db.TextType type, string action)
    {
        if (type.WellKnown.HasValue)
        {
            throw new InvalidOperationException($"Builtin text type [{type.Name}] cannot be {action}.");
        }
    }
}
