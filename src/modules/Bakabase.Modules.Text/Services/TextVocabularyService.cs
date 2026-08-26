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
        var type = await GetOrCreateBuiltin(wellKnown);
        var entries = await entryOrm.GetAll(e => e.TypeId == type.Id);
        return BuildSet(type, entries);
    }

    public async Task<int> GetTypeId(WellKnownTextType wellKnown) => (await GetOrCreateBuiltin(wellKnown)).Id;

    public async Task EnsureSeeds()
    {
        var existingTypes = await typeOrm.GetAll();
        var typeByWellKnown = existingTypes.Where(t => t.WellKnown.HasValue)
            .ToDictionary(t => t.WellKnown!.Value, t => t);

        foreach (var builtin in TextSeedData.BuiltinTypes)
        {
            if (!typeByWellKnown.TryGetValue(builtin.WellKnown, out var type))
            {
                type = (await typeOrm.Add(new Abstractions.Models.Db.TextType
                {
                    Name = builtin.Name,
                    WellKnown = builtin.WellKnown,
                    Shape = builtin.Shape,
                    CreatedAt = DateTime.Now
                })).Data;
                typeByWellKnown[builtin.WellKnown] = type;
            }

            if (builtin.Entries.Count == 0)
            {
                continue;
            }

            // Top up prefabs without resurrecting entries the user deleted in an earlier run: only
            // rows absent right now are added, and the comparison keys on both values so pair-shaped
            // types do not collapse into their first value.
            var existing = (await entryOrm.GetAll(e => e.TypeId == type.Id))
                .Select(e => (e.Value1, e.Value2))
                .ToHashSet();
            var missing = builtin.Entries.Where(e => !existing.Contains((e.Value1, e.Value2))).ToList();
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

    /// <summary>
    /// Builtins are defined in code, so a missing row is provisioned on demand rather than thrown
    /// over: consumers resolve wrapper or date-format sets during ordinary work and must not depend
    /// on seeding having run first (a fresh database, or a test that never seeded).
    /// </summary>
    private async Task<Abstractions.Models.Db.TextType> GetOrCreateBuiltin(WellKnownTextType wellKnown)
    {
        var type = await typeOrm.GetFirstOrDefault(t => t.WellKnown == wellKnown);
        if (type != null)
        {
            return type;
        }

        var definition = TextSeedData.BuiltinTypes.FirstOrDefault(t => t.WellKnown == wellKnown);
        return (await typeOrm.Add(new Abstractions.Models.Db.TextType
        {
            Name = definition?.Name ?? wellKnown.ToString(),
            WellKnown = wellKnown,
            Shape = definition?.Shape ?? TextTypeShape.Values,
            CreatedAt = DateTime.Now
        })).Data;
    }

    private static void EnsureNotBuiltin(Abstractions.Models.Db.TextType type, string action)
    {
        if (type.WellKnown.HasValue)
        {
            throw new InvalidOperationException($"Builtin text type [{type.Name}] cannot be {action}.");
        }
    }
}
