using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bootstrap.Components.Orm;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Kinds;

/// <summary>
/// The <c>extensionGroup</c> adapter (v3.1 §3.4, §8.3; §3.8 here): I/O only, next to the service that owns the table.
/// It reads through the service's cache and writes only through <see cref="IExtensionGroupService"/>; the codec (the
/// pure half: validation, publishing, comparison, merging) is the kind's own and is handed in.
/// </summary>
/// <remarks>
/// <para>
/// Content is canonical (v3.1 §3.4): <c>{"extensions":[".avi",".mkv"],"name":"Video"}</c>, each extension trimmed,
/// given one leading dot and lowercased, deduplicated and sorted; extensions are values, so they are the children's
/// ids and there is no child map. Extension groups have no fingerprint (F11), no order and no subtype.
/// </para>
/// <para>
/// An update writes the stored raw extensions as they are, minus the removals, plus the adds (N10, §8.5.3), so a stored
/// <c>.MKV</c> keeps its case and the re-read canonical content equals the merged content.
/// </para>
/// </remarks>
public sealed class ExtensionGroupDataSyncKind(
    IDataSyncKindCodec codec,
    IExtensionGroupService service,
    FullMemoryCacheResourceService<BakabaseDbContext, ExtensionGroupDbModel, int> rows) : IDataSyncKind
{
    private const string NameMember = "name";
    private const string ExtensionsMember = "extensions";

    public IDataSyncKindCodec Codec { get; } = codec.Descriptor.Kind == DataSyncKindIds.ExtensionGroup
        ? codec
        : throw new ArgumentException($"The extension group adapter needs the {DataSyncKindIds.ExtensionGroup} codec.",
            nameof(codec));

    public async Task<IReadOnlyList<LocalEntity>> ReadAsync(IReadOnlyCollection<string>? localKeys,
        CancellationToken ct)
    {
        var wanted = localKeys?.ToHashSet(StringComparer.Ordinal);
        var all = await ReadAllAsync();
        var result = new List<LocalEntity>();
        for (var position = 0; position < all.Count; position++)
        {
            var row = all[position];
            var localKey = KeyOf(row.Id);
            if (wanted is not null && !wanted.Contains(localKey)) continue;
            result.Add(new LocalEntity(localKey, null, position, ContentOf(row)));
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<string, JsonObject>> CapturePreImageAsync(IReadOnlyCollection<string> localKeys,
        CancellationToken ct)
    {
        var wanted = localKeys.ToHashSet(StringComparer.Ordinal);
        return (await ReadAllAsync()).Where(r => wanted.Contains(KeyOf(r.Id)))
            .ToDictionary(r => KeyOf(r.Id), r => new JsonObject
            {
                [ExtensionsMember] = r.Extensions,
                [NameMember] = r.Name,
            }, StringComparer.Ordinal);
    }

    /// <summary>
    /// Creates, updates, binds and deletes in batch order (v3.1 §8.3). An update or delete whose entity no longer hashes
    /// to <c>ExpectedLocalHash</c> is skipped as ChangedDuringApply; nothing throws for it.
    /// </summary>
    public async Task<ApplyBatchOutcome> ApplyAsync(ApplyBatch batch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.Kind != DataSyncKindIds.ExtensionGroup)
            throw new ArgumentException($"A {batch.Kind} batch reached the extension group adapter.", nameof(batch));
        var created = new Dictionary<string, string>(StringComparer.Ordinal);
        var changed = new HashSet<string>(StringComparer.Ordinal);
        var operations = batch.Operations;
        for (var i = 0; i < operations.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            switch (operations[i])
            {
                case CreateEntityOperation:
                {
                    // Consecutive creates are one AddRange (v3.1 §8.3), results in input order.
                    var creates = new List<CreateEntityOperation>();
                    while (i < operations.Count && operations[i] is CreateEntityOperation create)
                    {
                        creates.Add(create);
                        i++;
                    }

                    i--;
                    var groups = await service.AddRange(creates.Select(c =>
                    {
                        var (name, extensions) = Parse(c.Content);
                        return new ExtensionGroupAddInputModel(name, extensions.ToHashSet(StringComparer.Ordinal));
                    }).ToArray());
                    for (var j = 0; j < creates.Count; j++) created[creates[j].ItemId] = KeyOf(groups[j].Id);
                    break;
                }
                case UpdateEntityOperation update:
                {
                    if (!await HashHoldsAsync(update.LocalKey, update.ExpectedLocalHash, ct))
                    {
                        changed.Add(update.ItemId);
                        break;
                    }

                    var id = IdOf(update.LocalKey);
                    var stored = (await service.Get(id)).Extensions ?? [];
                    var removed = update.RemovedChildIds.Select(Canonicalize).ToHashSet(StringComparer.Ordinal);
                    var extensions = stored.Where(e => !string.IsNullOrWhiteSpace(e) && !removed.Contains(Canonicalize(e)))
                        .ToList();
                    var present = extensions.Select(Canonicalize).ToHashSet(StringComparer.Ordinal);
                    foreach (var added in update.AddedChildIds.Select(Canonicalize))
                    {
                        if (present.Add(added)) extensions.Add(added);
                    }

                    await service.Put(id, new ExtensionGroupPutInputModel(Parse(update.MergedContent).Name,
                        extensions.ToHashSet(StringComparer.Ordinal)));
                    break;
                }
                case BindOnlyOperation:
                    break;
                case DeleteEntityOperation delete:
                {
                    if (!await HashHoldsAsync(delete.LocalKey, delete.ExpectedLocalHash, ct))
                    {
                        changed.Add(delete.ItemId);
                        break;
                    }

                    await service.Delete(IdOf(delete.LocalKey));
                    break;
                }
                case ChangeSubtypeOperation:
                    throw new InvalidOperationException("Extension groups have no subtype.");
                default:
                    throw new ArgumentOutOfRangeException(nameof(batch),
                        $"Unknown operation {operations[i].GetType().Name}.");
            }
        }

        return new ApplyBatchOutcome(created, changed);
    }

    /// <summary>Extensions are values nobody's data references: every entity and extension is unused (§8.5.3).</summary>
    public Task<IReadOnlyDictionary<string, EntityUsage>> GetUsageAsync(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> childIdsByLocalKey, CancellationToken ct)
    {
        IReadOnlyDictionary<string, EntityUsage> usage = childIdsByLocalKey.ToDictionary(e => e.Key,
            e => new EntityUsage(0, e.Value.Distinct(StringComparer.Ordinal).ToDictionary(c => c, _ => 0,
                StringComparer.Ordinal)), StringComparer.Ordinal);
        return Task.FromResult(usage);
    }

    /// <summary>Writes a captured pre-image back through the service; the group must still exist.</summary>
    public async Task RestoreAsync(string localKey, JsonObject preImage, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(preImage);
        var id = IdOf(localKey);
        if (await rows.GetByKey(id) is null)
            throw new KeyNotFoundException($"Extension group {localKey} no longer exists.");
        var name = preImage[NameMember]?.GetValue<string>() ??
                   throw new ArgumentException("An extension group pre-image has a name.", nameof(preImage));
        var raw = preImage[ExtensionsMember]?.GetValue<string>();
        var extensions = string.IsNullOrEmpty(raw)
            ? new HashSet<string>(StringComparer.Ordinal)
            : raw.Split(InternalOptions.TextSeparator, StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        await service.Put(id, new ExtensionGroupPutInputModel(name, extensions));
    }

    public Task DeleteAsync(string localKey, CancellationToken ct) => service.Delete(IdOf(localKey));

    public void ResetCaches() => rows.ClearCache();

    /// <summary>Extension groups have no order: Id order.</summary>
    public async Task<IReadOnlyList<string>> ReadOrderAsync(CancellationToken ct) =>
        (await ReadAllAsync()).Select(r => KeyOf(r.Id)).ToList();

    /// <summary>No-op: extension groups have no order (§3.7).</summary>
    public Task ApplyOrderAsync(IReadOnlyList<string> syncedLocalKeysInSharedOrder, CancellationToken ct) =>
        Task.CompletedTask;

    public Task ChangeSubtypeAsync(string localKey, string subtype, CancellationToken ct) =>
        throw new NotSupportedException("Extension groups have no subtype.");

    public Task<DataSyncTypeChangePreview> PreviewSubtypeChangeAsync(string localKey, string subtype,
        CancellationToken ct) =>
        throw new NotSupportedException("Extension groups have no subtype.");

    /// <summary>The fast path's hash of each stored row: its name and its raw extensions string (§6.1).</summary>
    public async Task<IReadOnlyDictionary<string, string>> ReadRawHashesAsync(CancellationToken ct) =>
        (await ReadAllAsync()).ToDictionary(r => KeyOf(r.Id),
            r => ContentHash.Of(new JsonArray(JsonValue.Create(r.Name), JsonValue.Create(r.Extensions))),
            StringComparer.Ordinal);

    /// <summary>
    /// Canonical content of a stored group (v3.1 §3.4), through the codec, so the adapter hands out exactly the
    /// codec's canonical form (<c>Write(ReadLocal(x)) == x</c>).
    /// </summary>
    private JsonObject ContentOf(ExtensionGroupDbModel row)
    {
        var extensions = (string.IsNullOrEmpty(row.Extensions)
                ? []
                : row.Extensions.Split(InternalOptions.TextSeparator, StringSplitOptions.RemoveEmptyEntries))
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(Canonicalize)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(e => e, StringComparer.Ordinal)
            .Select(e => (JsonNode?) JsonValue.Create(e))
            .ToArray();
        var content = new JsonObject {[ExtensionsMember] = new JsonArray(extensions), [NameMember] = row.Name};
        return Codec.Write(Codec.ReadLocal(content));
    }

    /// <summary>One extension's canonical form (v3.1 §3.4): trimmed, one leading dot, lowercased (invariant).</summary>
    internal static string Canonicalize(string extension) =>
        "." + extension.Trim().TrimStart('.').ToLower(CultureInfo.InvariantCulture);

    private async Task<bool> HashHoldsAsync(string localKey, string expectedLocalHash, CancellationToken ct)
    {
        var current = (await ReadAsync([localKey], ct)).SingleOrDefault();
        return current is not null &&
               string.Equals(ContentHash.Of(current.Content), expectedLocalHash, StringComparison.Ordinal);
    }

    private static (string Name, IReadOnlyList<string> Extensions) Parse(JsonObject content)
    {
        var name = content[NameMember] is JsonValue n && n.GetValueKind() == JsonValueKind.String
            ? n.GetValue<string>()
            : throw new ArgumentException("Extension group content has a name.", nameof(content));
        var extensions = content[ExtensionsMember] is JsonArray array
            ? array.Select(e => e is JsonValue v && v.GetValueKind() == JsonValueKind.String
                ? Canonicalize(v.GetValue<string>())
                : throw new ArgumentException("An extension is a string.", nameof(content))).ToList()
            : [];
        return (name, extensions);
    }

    private async Task<List<ExtensionGroupDbModel>> ReadAllAsync() =>
        (await rows.GetAll()).OrderBy(r => r.Id).ToList();

    private static string KeyOf(int id) => id.ToString(CultureInfo.InvariantCulture);

    private static int IdOf(string localKey) =>
        int.TryParse(localKey, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : throw new ArgumentException($"'{localKey}' is not an extension group id.", nameof(localKey));
}

public static class ExtensionGroupDataSyncKindRegistration
{
    /// <summary>
    /// Registers the <c>extensionGroup</c> kind with <paramref name="codec"/>, the kind's codec (the pure engine's
    /// extension group codec in production, registered by <c>AddDataSync()</c>). A kind is registered once per
    /// container, so a later call replaces the registration an earlier one made: a test hands in its own codec over the
    /// production one.
    /// </summary>
    public static IServiceCollection AddExtensionGroupDataSyncKind(this IServiceCollection services,
        IDataSyncKindCodec codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        if (codec.Descriptor.Kind != DataSyncKindIds.ExtensionGroup)
            throw new ArgumentException($"The {DataSyncKindIds.ExtensionGroup} kind needs its own codec.", nameof(codec));
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(IDataSyncKind) && services[i].ImplementationFactory?.Target is Factory)
                services.RemoveAt(i);
        }

        services.AddScoped<IDataSyncKind>(new Factory(codec).Create);
        return services;
    }

    /// <summary>What the registration is recognised by.</summary>
    private sealed class Factory(IDataSyncKindCodec codec)
    {
        public IDataSyncKind Create(IServiceProvider sp) => new ExtensionGroupDataSyncKind(codec,
            sp.GetRequiredService<IExtensionGroupService>(),
            sp.GetRequiredService<FullMemoryCacheResourceService<BakabaseDbContext, ExtensionGroupDbModel, int>>());
    }
}
