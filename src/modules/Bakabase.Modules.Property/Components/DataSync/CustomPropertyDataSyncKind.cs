using System.Globalization;
using System.Text.Json.Nodes;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Kinds.CustomProperties;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue;
using Bakabase.Modules.StandardValue.Extensions;
using Bootstrap.Components.Orm;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.Property.Components.DataSync;

/// <summary>
/// The <c>customProperty</c> adapter (v3.1 §2.2, §8.3; §2.3 here): I/O only, next to the service that owns the table.
/// It reads the stored rows through <see cref="ICustomPropertyService"/>'s cache and writes only through the services —
/// <c>AddRange</c>, <c>AddRangeVerbatim</c>, <c>PutVerbatim</c>, <c>SetOrders</c>, <c>ChangeType</c> and
/// <c>RemoveByKey</c>, and <see cref="ICustomPropertyValueService"/> for the values a restore points back at their
/// options — on the scope's context, so every write joins the caller's transaction. The pure half (validation,
/// publishing, comparison, merging) is <see cref="CustomPropertyCodec"/>.
/// </summary>
/// <remarks>
/// <para>
/// Content is <see cref="CustomPropertyContentMapper"/>'s, written by the codec, so it is exactly the codec's canonical
/// form (<c>Write(ReadLocal(x)) == x</c>) and holds every stored option, unvalidated (v3.1 B3). A row whose options do
/// not read is <see cref="LocalEntity.Unreadable"/>: it is recorded, and this adapter never writes it (§3.3).
/// </para>
/// <para>
/// A row of a type this build does not know (a newer build's, after a downgrade) cannot be content at all: it is
/// never read, ordered, used or written. It keeps a raw hash, though, so Refresh does not take it for a deletion and
/// publish a tombstone for a definition that still exists; it stays as it is until a build that knows its type reads
/// it again.
/// </para>
/// <para>
/// Local only, never content: the id (the local key), <c>CreatedAt</c> (the ID-reuse fingerprint, v3.1 §5.4), the
/// integer <c>Order</c> (the local order, §3.7) and the values. "Sync the definition only" lives on data sync's side
/// row, so content read here never carries it and a written content's <c>childrenLocal</c> is ignored.
/// </para>
/// <para>
/// Derived state (§8.10.6): a type change converts values, a deletion removes them and a restore may point them at
/// other option ids, so each invalidates the search index for the resources whose values it touched. Renames, adds,
/// removals, recolours and moves need no index work (index keys are ids). <see cref="ResetCaches"/> drops both
/// services' memory caches after a rollback and invalidates those resources again, so the index re-reads what the
/// rollback restored.
/// </para>
/// </remarks>
/// <typeparam name="TDbContext">The context the property services run on (<c>AddProperty&lt;TDbContext&gt;</c>).</typeparam>
public sealed class CustomPropertyDataSyncKind<TDbContext> : IDataSyncKind where TDbContext : DbContext
{
    /// <summary>Most samples a type change preview returns (§2.3).</summary>
    public const int MaxTypeChangeSamples = 20;

    private const string PreImageName = "name";
    private const string PreImageType = "type";
    private const string PreImageOptions = "options";
    private const string PreImageOrder = "order";
    private const string PreImageCreatedAt = "createdAt";
    private const string PreImageContent = "content";

    private static readonly CustomPropertyCodec SharedCodec = CustomPropertyCodec.Instance;

    private readonly ICustomPropertyService _properties;
    private readonly ICustomPropertyValueService _values;
    private readonly IPropertyTypeConverter _converter;
    private readonly GlobalCacheVault _caches;
    private readonly IServiceProvider _services;

    /// <summary>Resources whose index entries this scope invalidated, invalidated again by <see cref="ResetCaches"/>.</summary>
    private readonly HashSet<int> _invalidated = [];

    public CustomPropertyDataSyncKind(ICustomPropertyService properties, ICustomPropertyValueService values,
        IPropertyTypeConverter converter, GlobalCacheVault caches, IServiceProvider services)
    {
        _properties = properties;
        _values = values;
        _converter = converter;
        _caches = caches;
        _services = services;
    }

    public IDataSyncKindCodec Codec => SharedCodec;

    // ---- reading -----------------------------------------------------------------------------

    /// <summary>
    /// Entities in local order, <c>(Order, Id)</c>; <see cref="LocalEntity.Position"/> is the place in the whole kind
    /// even when <paramref name="localKeys"/> selects some. Keys that name no property are skipped.
    /// </summary>
    public async Task<IReadOnlyList<LocalEntity>> ReadAsync(IReadOnlyCollection<string>? localKeys,
        CancellationToken ct)
    {
        var wanted = localKeys?.ToHashSet(StringComparer.Ordinal);
        var rows = await ReadRowsAsync();
        var result = new List<LocalEntity>(wanted?.Count ?? rows.Count);
        for (var position = 0; position < rows.Count; position++)
        {
            var row = rows[position];
            if (wanted is not null && !wanted.Contains(KeyOf(row.Id))) continue;
            result.Add(ToLocalEntity(row, position));
        }

        return result;
    }

    /// <summary>
    /// The fast path's hash of each stored row (§6.1): its name, type and options string as stored, and its
    /// fingerprint, so a reused id is re-read even when the new row's other columns equal the old one's. A row of a
    /// type this build does not know has a hash too, although it is never read: see the class remarks.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> ReadRawHashesAsync(CancellationToken ct) =>
        (await _properties.GetAllDbModels()).ToDictionary(r => KeyOf(r.Id), r => ContentHash.Of(new JsonArray(
            JsonValue.Create(r.Name),
            JsonValue.Create(CustomPropertyTypes.NameOf(r.Type) ?? ((int) r.Type).ToString(CultureInfo.InvariantCulture)),
            JsonValue.Create(r.Options), JsonValue.Create(FingerprintOf(r.CreatedAt)))), StringComparer.Ordinal);

    /// <summary>The kind's local order: by <c>(Order, Id)</c>.</summary>
    public async Task<IReadOnlyList<string>> ReadOrderAsync(CancellationToken ct) =>
        (await ReadRowsAsync()).Select(r => KeyOf(r.Id)).ToList();

    /// <summary>
    /// The raw rows for undo: <c>{content, createdAt, name, options, order, type}</c>. <c>options</c> is the stored
    /// string (absent when null), <c>type</c> its number and <c>createdAt</c> in the round-trip format; what
    /// <see cref="RestoreAsync"/> writes back. <c>content</c> is the canonical content as <see cref="ReadAsync"/> hands
    /// it out (absent for an unreadable row), so re-creating a deleted property needs no kind-specific mapping: it is a
    /// <see cref="CreateEntityOperation"/>'s content, with the same option ids. Keys that name no property are skipped.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, JsonObject>> CapturePreImageAsync(
        IReadOnlyCollection<string> localKeys, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(localKeys);
        var wanted = localKeys.ToHashSet(StringComparer.Ordinal);
        var rows = await ReadRowsAsync();
        var result = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        for (var position = 0; position < rows.Count; position++)
        {
            var row = rows[position];
            if (!wanted.Contains(KeyOf(row.Id))) continue;
            var preImage = new JsonObject
            {
                [PreImageCreatedAt] = row.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
                [PreImageName] = row.Name,
                [PreImageOrder] = row.Order,
                [PreImageType] = (int) row.Type,
            };
            if (row.Options is not null) preImage[PreImageOptions] = row.Options;
            var local = ToLocalEntity(row, position);
            if (!local.Unreadable) preImage[PreImageContent] = local.Content;
            result[KeyOf(row.Id)] = preImage;
        }

        return result;
    }

    /// <summary>
    /// Usage per entity: its stored values and, for each requested option id, the distinct resources whose value
    /// references that id itself (a multilevel value names nodes; the merge sums a subtree, §8.5.4). A property that no
    /// longer exists, and a type without options, count nothing.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, EntityUsage>> GetUsageAsync(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> childIdsByLocalKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(childIdsByLocalKey);
        var types = (await ReadRowsAsync()).ToDictionary(r => r.Id, r => r.Type);
        var ids = childIdsByLocalKey.Keys.Select(TryIdOf).OfType<int>().Where(types.ContainsKey).ToHashSet();
        var valuesByProperty = ids.Count == 0
            ? new Dictionary<int, List<CustomPropertyValueDbModel>>()
            : (await _values.GetAllDbModels(v => ids.Contains(v.PropertyId), false))
            .GroupBy(v => v.PropertyId).ToDictionary(g => g.Key, g => g.ToList());

        var result = new Dictionary<string, EntityUsage>(StringComparer.Ordinal);
        foreach (var (localKey, childIds) in childIdsByLocalKey)
        {
            ct.ThrowIfCancellationRequested();
            var requested = childIds.Distinct(StringComparer.Ordinal).ToList();
            var counts = requested.ToDictionary(c => c, _ => 0, StringComparer.Ordinal);
            if (TryIdOf(localKey) is not { } id || !types.TryGetValue(id, out var type))
            {
                result[localKey] = new EntityUsage(0, counts);
                continue;
            }

            var values = valuesByProperty.GetValueOrDefault(id) ?? [];
            if (requested.Count > 0 && PropertySystem.Property.IsReferenceValueType(type))
            {
                var wanted = requested.ToHashSet(StringComparer.Ordinal);
                var dbValueType = PropertySystem.Property.GetDbValueType(type);
                var resources = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
                foreach (var value in values)
                {
                    foreach (var childId in ReferencedIds(value.Value, dbValueType))
                    {
                        if (!wanted.Contains(childId)) continue;
                        if (!resources.TryGetValue(childId, out var set)) resources[childId] = set = [];
                        set.Add(value.ResourceId);
                    }
                }

                foreach (var (childId, set) in resources) counts[childId] = set.Count;
            }

            result[localKey] = new EntityUsage(values.Count, counts);
        }

        return result;
    }

    // ---- writing -----------------------------------------------------------------------------

    /// <summary>
    /// Creates, updates, binds, deletes and changes subtypes in batch order (v3.1 §8.3, §8.5.6). Consecutive creates of
    /// one origin are one call, results in input order. A peer's create goes through <c>AddRange</c>, which folds what
    /// the service folds in any new property: nothing, for <c>PrepareCreate</c>'s content (v3.1 H4), which it stores as
    /// given. A re-created property (undo of a deletion, §8.11, <see cref="CreateEntityOperation.FromPreImage"/>) goes
    /// through <c>AddRangeVerbatim</c>: it keeps every captured option, case-variant duplicates under IgnoreCase
    /// included (F72). An update is a <c>PutVerbatim</c>: the merge already folded what the service folds in an edit
    /// (<see cref="OptionFolding"/>, cross-checked against its normalizer), and a second normalization could only
    /// diverge — the service would take a local choice without an id for one the edit introduced and fold it away,
    /// which merges keep (§3.3). An update, delete or subtype change whose entity is gone, is unreadable or no longer
    /// hashes to <c>ExpectedLocalHash</c> is skipped as ChangedDuringApply, and so is an automatic deletion
    /// (<see cref="DeleteEntityOperation.RequireNoValues"/>) of a property that has values; nothing throws for them.
    /// An entity the batch wrote is read again before a later operation of the batch is checked against it. Placement of creates is
    /// <see cref="ApplyOrderAsync"/>'s, after every batch (§3.7).
    /// </summary>
    public async Task<ApplyBatchOutcome> ApplyAsync(ApplyBatch batch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.Kind != DataSyncKindIds.CustomProperty)
            throw new ArgumentException($"A {batch.Kind} batch reached the custom property adapter.", nameof(batch));
        var created = new Dictionary<string, string>(StringComparer.Ordinal);
        var changed = new HashSet<string>(StringComparer.Ordinal);
        var rows = new RowSnapshot(this);
        var operations = batch.Operations;
        for (var i = 0; i < operations.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            switch (operations[i])
            {
                case CreateEntityOperation first:
                {
                    var creates = new List<CreateEntityOperation>();
                    while (i < operations.Count && operations[i] is CreateEntityOperation create &&
                           create.FromPreImage == first.FromPreImage)
                    {
                        creates.Add(create);
                        i++;
                    }

                    i--;
                    var models = creates.Select(c =>
                    {
                        var content = SharedCodec.ReadLocal(c.Content);
                        return new CustomPropertyAddOrPutDto
                        {
                            Name = content.Name, Type = content.Type,
                            Options = CustomPropertyContentMapper.ToOptionsJson(content),
                        };
                    }).ToArray();
                    var properties = first.FromPreImage
                        ? await _properties.AddRangeVerbatim(models)
                        : await _properties.AddRange(models);
                    for (var j = 0; j < creates.Count; j++) created[creates[j].ItemId] = KeyOf(properties[j].Id);
                    break;
                }
                case UpdateEntityOperation update:
                {
                    if (await rows.CurrentAsync(update.LocalKey, update.ExpectedLocalHash) is not { } row)
                    {
                        changed.Add(update.ItemId);
                        break;
                    }

                    var merged = SharedCodec.ReadLocal(update.MergedContent);
                    if (merged.Type != row.Type)
                        throw new InvalidOperationException(
                            $"Custom property {update.LocalKey}: an update never changes the type; a ChangeSubtypeOperation does.");
                    await _properties.PutVerbatim(row.Id, new CustomPropertyAddOrPutDto
                    {
                        Name = merged.Name, Type = row.Type, Options = CustomPropertyContentMapper.ToOptionsJson(merged),
                    });
                    rows.MarkWritten(row.Id);
                    break;
                }
                case BindOnlyOperation:
                    break;
                case DeleteEntityOperation delete:
                {
                    // The hash covers the row, not its values: a deletion decided on "no values" checks them again.
                    if (await rows.CurrentAsync(delete.LocalKey, delete.ExpectedLocalHash) is not { } row ||
                        (delete.RequireNoValues && await HasValuesAsync(row.Id)))
                    {
                        changed.Add(delete.ItemId);
                        break;
                    }

                    await RemoveAsync(row.Id);
                    rows.MarkWritten(row.Id);
                    break;
                }
                case ChangeSubtypeOperation change:
                {
                    if (await rows.CurrentAsync(change.LocalKey, change.ExpectedLocalHash) is not { } row)
                    {
                        changed.Add(change.ItemId);
                        break;
                    }

                    await ChangeTypeAsync(row, ParseSubtype(change.Subtype));
                    rows.MarkWritten(row.Id);
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(batch),
                        $"Unknown operation {operations[i].GetType().Name}.");
            }
        }

        return new ApplyBatchOutcome(created, changed);
    }

    /// <summary>
    /// Writes a captured pre-image back through <c>PutVerbatim</c>: the name and the stored options string, exactly as
    /// captured — case-variant duplicates under IgnoreCase that are no longer stored included, which <c>Put</c> would
    /// fold (F72). <c>CreatedAt</c> and <c>Order</c> are kept. An unreadable property is never written, and unreadable
    /// captured options are never restored.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No value is left naming an option the restored options lack (§3.2, the unified miss behaviour would drop it
    /// silently). A value whose option the pre-image does not have is pointed at the captured option of its label
    /// class (§3.4: the label under the captured IgnoreCase, a tag's group and name, a node's key path); with none,
    /// the restore is refused (<see cref="InvalidOperationException"/>) before anything is written — undo does not
    /// take away an option in use (v3.1 <c>AddedOptionsInUse</c>). A value that names no option already is left as
    /// it is.
    /// </para>
    /// <para>
    /// A type change is undone here too (§8.11): when the pre-image has another type, the property is converted back
    /// with <c>ChangeType</c> first (converting its values and invalidating their index entries), which rebuilds its
    /// options from its values with fresh ids (F73). Every value is then pointed at the captured option of its class;
    /// one the conversions left without one (its label changed on the way) keeps the option it has, added to the
    /// restored options — values lost by the first conversion stay lost, and no value dangles. Undo converts back
    /// through this method, not <see cref="ChangeSubtypeAsync"/>: after that, a restore of the same type would refuse
    /// such a value instead of keeping it.
    /// </para>
    /// </remarks>
    public async Task RestoreAsync(string localKey, JsonObject preImage, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(preImage);
        var row = await RowAsync(IdOf(localKey)) ??
                  throw new KeyNotFoundException($"Custom property {localKey} no longer exists.");
        if (IsUnreadable(row))
            throw new InvalidOperationException($"Custom property {localKey}: its options do not read; it is never written.");
        var name = preImage[PreImageName]?.GetValue<string>() ??
                   throw new ArgumentException("A custom property pre-image has a name.", nameof(preImage));
        var type = (PropertyType) (preImage[PreImageType]?.GetValue<int>() ??
                                   throw new ArgumentException("A custom property pre-image has a type.", nameof(preImage)));
        if (!CustomPropertyContentMapper.Supports(type))
            throw new ArgumentException($"A custom property pre-image of type {(int) type} this build does not know.",
                nameof(preImage));
        var options = preImage[PreImageOptions]?.GetValue<string>();
        var captured = CustomPropertyContentMapper.ReadRow(name, type, options);
        if (captured.Unreadable)
            throw new InvalidOperationException($"Custom property {localKey}: the captured options do not read.");

        var convertBack = type != row.Type;
        if (convertBack)
        {
            await ChangeTypeAsync(row, type);
            row = await RowAsync(row.Id) ??
                  throw new InvalidOperationException($"Custom property {localKey} is gone after its conversion.");
        }

        var (values, restored) = await PointValuesAtAsync(row, captured.Content, keepUnmatched: convertBack, ct);
        if (values.Count > 0)
        {
            EnsureOk(await _values.UpdateDbModelRange(values), $"point the values of custom property {row.Id} back");
            Invalidate(values.Select(v => v.ResourceId).Distinct().ToArray());
        }

        await _properties.PutVerbatim(row.Id, new CustomPropertyAddOrPutDto
        {
            Name = name, Type = type,
            Options = restored is null ? options : CustomPropertyContentMapper.ToOptionsJson(restored),
        });
    }

    /// <summary>Deletes the property and its values (<c>RemoveByKey</c>), then invalidates their resources' index entries.</summary>
    public async Task DeleteAsync(string localKey, CancellationToken ct)
    {
        var id = IdOf(localKey);
        if (await RowAsync(id) is null) throw new KeyNotFoundException($"Custom property {localKey} no longer exists.");
        await RemoveAsync(id);
    }

    /// <summary>
    /// Drops the property and value caches, which are shared across scopes and not transactional (F9), and invalidates
    /// again every resource this scope invalidated, so the index re-reads the restored rows. Every call does, not only
    /// the first: after a rollback to a savepoint the apply session calls it again once its transaction committed, and
    /// the index may have re-read, from the committed rows and before that commit, a resource whose values the
    /// transaction converted before the savepoint.
    /// </summary>
    public void ResetCaches()
    {
        _caches.TryRemove(typeof(CustomPropertyDbModel).FullName!, out _);
        _caches.TryRemove(typeof(CustomPropertyValueDbModel).FullName!, out _);
        if (_invalidated.Count == 0) return;
        _services.GetService<IResourceSearchIndexService>()?.InvalidateResources(_invalidated.ToArray());
    }

    /// <summary>
    /// Places synced properties in the given (shared) order in the slots synced properties hold now; every other
    /// property keeps its slot (§3.7). Keys that name no property are ignored, and so is a repeat. When the resulting
    /// order differs from the current one, every property's <c>Order</c> becomes its position, and only the rows whose
    /// <c>Order</c> changes are written, through <c>SetOrders</c>.
    /// </summary>
    public async Task ApplyOrderAsync(IReadOnlyList<string> syncedLocalKeysInSharedOrder, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(syncedLocalKeysInSharedOrder);
        if (!Codec.Descriptor.HasOrder) return;
        // Every row, of a type this build knows or not, so no slot moves under a row the kind does not show.
        var current = (await _properties.GetAllDbModels()).OrderBy(r => r.Order).ThenBy(r => r.Id).ToList();
        var byKey = current.ToDictionary(r => KeyOf(r.Id), StringComparer.Ordinal);
        var synced = syncedLocalKeysInSharedOrder.Distinct(StringComparer.Ordinal)
            .Select(k => byKey.GetValueOrDefault(k)).OfType<CustomPropertyDbModel>().ToList();
        var syncedIds = synced.Select(r => r.Id).ToHashSet();

        var placed = current.ToArray();
        var next = 0;
        for (var slot = 0; slot < placed.Length; slot++)
        {
            if (syncedIds.Contains(placed[slot].Id)) placed[slot] = synced[next++];
        }

        if (placed.Select(r => r.Id).SequenceEqual(current.Select(r => r.Id))) return;
        var orders = new Dictionary<int, int>();
        for (var position = 0; position < placed.Length; position++)
        {
            if (placed[position].Order != position) orders[placed[position].Id] = position;
        }

        await _properties.SetOrders(orders);
    }

    /// <summary>
    /// Converts the property to <paramref name="subtype"/> (a <see cref="PropertyType"/> name) with <c>ChangeType</c>,
    /// which converts its values and keeps <c>CreatedAt</c> and <c>Order</c> (B0), then invalidates the index entries
    /// of the resources with values (§8.10.6). A no-op when the type is already that one.
    /// </summary>
    public async Task ChangeSubtypeAsync(string localKey, string subtype, CancellationToken ct)
    {
        var type = ParseSubtype(subtype);
        var row = await RowAsync(IdOf(localKey)) ??
                  throw new KeyNotFoundException($"Custom property {localKey} no longer exists.");
        if (IsUnreadable(row))
            throw new InvalidOperationException($"Custom property {localKey}: its options do not read; it cannot be converted.");
        await ChangeTypeAsync(row, type);
    }

    /// <summary>
    /// What converting to <paramref name="subtype"/> does to this device's values (§8.5.6), through the same converter
    /// <c>ChangeType</c> uses. <c>ChangedCount</c> and the samples are the values whose display changes (the converter's
    /// own preview); <c>LossyCount</c> counts every value whose converted value no longer converts back to the original,
    /// so converting back cannot restore it. A lossy value may display alike, so it can exceed <c>ChangedCount</c>; it
    /// decides the backup before a conversion (§8.10.4), which is why it errs towards counting.
    /// </summary>
    public async Task<DataSyncTypeChangePreview> PreviewSubtypeChangeAsync(string localKey, string subtype,
        CancellationToken ct)
    {
        var type = ParseSubtype(subtype);
        var id = IdOf(localKey);
        var row = await RowAsync(id) ?? throw new KeyNotFoundException($"Custom property {localKey} no longer exists.");
        if (IsUnreadable(row))
            throw new InvalidOperationException($"Custom property {localKey}: its options do not read; it cannot be converted.");
        var property = (await _properties.GetByKey(id)).ToProperty();
        var values = (await _values.GetAll(v => v.PropertyId == id, CustomPropertyValueAdditionalItem.None, false))
            .Select(v => v.Value).ToList();
        var preview = await _converter.PreviewConversionAsync(property, type, values);
        var lossy = values.Count(v => IsLossy(property, v, preview.FromBizType, preview.ToBizType));
        return new DataSyncTypeChangePreview(CustomPropertyTypes.NameOf(row.Type)!, CustomPropertyTypes.NameOf(type)!,
            preview.TotalCount, preview.Changes.Count, lossy,
            preview.Changes.Take(MaxTypeChangeSamples)
                .Select(c => new DataSyncTypeChangeSample(c.FromDisplay, c.ToDisplay)).ToList());
    }

    // ---- helpers -----------------------------------------------------------------------------

    private async Task ChangeTypeAsync(CustomPropertyDbModel row, PropertyType type)
    {
        if (row.Type == type) return;
        EnsureOk(await _properties.ChangeType(row.Id, type), $"convert custom property {row.Id} to {type}");
        Invalidate(await ResourcesWithValuesAsync(row.Id));
    }

    /// <summary><c>RemoveByKey</c> deletes the values too, so the resources that had them are read first.</summary>
    private async Task RemoveAsync(int id)
    {
        var resources = await ResourcesWithValuesAsync(id);
        EnsureOk(await _properties.RemoveByKey(id), $"delete custom property {id}");
        Invalidate(resources);
    }

    private async Task<int[]> ResourcesWithValuesAsync(int id) =>
        (await _values.GetAllDbModels(v => v.PropertyId == id, false)).Select(v => v.ResourceId).Distinct().ToArray();

    private async Task<bool> HasValuesAsync(int id) =>
        (await _values.GetAllDbModels(v => v.PropertyId == id, false)).Count > 0;

    private void Invalidate(IReadOnlyCollection<int> resourceIds)
    {
        if (resourceIds.Count == 0) return;
        if (_services.GetService<IResourceSearchIndexService>() is not { } index) return;
        index.InvalidateResources(resourceIds);
        _invalidated.UnionWith(resourceIds);
    }

    private static void EnsureOk(BaseResponse response, string what)
    {
        if (response.Code != (int) ResponseCode.Success)
            throw new InvalidOperationException($"Could not {what}: {response.Code} {response.Message}");
    }

    /// <summary>Rows of the kind (types this build knows), in local order <c>(Order, Id)</c>.</summary>
    private async Task<List<CustomPropertyDbModel>> ReadRowsAsync() =>
        (await _properties.GetAllDbModels()).Where(r => CustomPropertyContentMapper.Supports(r.Type))
        .OrderBy(r => r.Order).ThenBy(r => r.Id).ToList();

    private async Task<CustomPropertyDbModel?> RowAsync(int id) =>
        (await _properties.GetAllDbModels(r => r.Id == id)).SingleOrDefault(r => CustomPropertyContentMapper.Supports(r.Type));

    private static LocalEntity ToLocalEntity(CustomPropertyDbModel row, int position)
    {
        var stored = CustomPropertyContentMapper.ReadRow(row.Name, row.Type, row.Options);
        return new LocalEntity(KeyOf(row.Id), FingerprintOf(row.CreatedAt), position, SharedCodec.Write(stored.Content),
            stored.Unreadable);
    }

    private static bool IsUnreadable(CustomPropertyDbModel row) =>
        CustomPropertyContentMapper.ReadRow(row.Name, row.Type, row.Options).Unreadable;

    /// <summary>v3.1 §5.4: <c>CreatedAt.Ticks</c>; null ("unknown") for a creation time the old ChangeType bug reset.</summary>
    private static string? FingerprintOf(DateTime createdAt) =>
        createdAt == default ? null : createdAt.Ticks.ToString(CultureInfo.InvariantCulture);

    private static IEnumerable<string> ReferencedIds(string? serialized, StandardValueType dbValueType)
    {
        if (string.IsNullOrEmpty(serialized)) return [];
        return serialized.DeserializeAsStandardValue(dbValueType) switch
        {
            string id when id.Length > 0 => [id],
            List<string> ids => ids.Where(i => !string.IsNullOrEmpty(i)),
            _ => [],
        };
    }

    // ---- restoring -----------------------------------------------------------------------------

    /// <summary>
    /// The values of <paramref name="row"/> that <see cref="RestoreAsync"/> points at <paramref name="captured"/>'s
    /// options (copies; unchanged values left out), and the options to restore when some had to be added to the
    /// captured ones (<paramref name="keepUnmatched"/>; null: the captured options as they are).
    /// </summary>
    private async Task<(List<CustomPropertyValueDbModel> Values, CustomPropertyContentV1? Restored)> PointValuesAtAsync(
        CustomPropertyDbModel row, CustomPropertyContentV1 captured, bool keepUnmatched, CancellationToken ct)
    {
        if (!PropertySystem.Property.IsReferenceValueType(row.Type)) return ([], null);
        var ignoreCase = captured.IgnoreCase == true;
        var current = CustomPropertyContentMapper.ReadRow(row.Name, row.Type, row.Options).Content;
        var currentKeys = OptionKeys.Of(current, ignoreCase);
        var restored = captured;
        var targets = OptionKeys.Of(restored, ignoreCase);
        var extended = false;
        var dbValueType = PropertySystem.Property.GetDbValueType(row.Type);
        var values = await _values.GetAllDbModels(v => v.PropertyId == row.Id, false);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var id in ReferencedIds(value.Value, dbValueType))
            {
                if (targets.Ids.Contains(id) || map.ContainsKey(id)) continue;
                // A value that names no option already: left as it is.
                if (!currentKeys.KeyById.TryGetValue(id, out var key)) continue;
                if (targets.IdByKey.TryGetValue(key, out var target))
                {
                    map[id] = target;
                    continue;
                }

                if (!keepUnmatched)
                    throw new InvalidOperationException(
                        $"Custom property {row.Id}: its values use option {id}, which its pre-image does not have; " +
                        "restoring it would leave them naming nothing.");
                restored = WithOptionOf(restored, current, id, ignoreCase);
                targets = OptionKeys.Of(restored, ignoreCase);
                extended = true;
            }
        }

        var rewritten = new List<CustomPropertyValueDbModel>();
        if (map.Count > 0)
        {
            foreach (var value in values)
            {
                if (Rewrite(value.Value, map, dbValueType) is { } serialized) rewritten.Add(value with { Value = serialized });
            }
        }

        return (rewritten, extended ? restored : null);
    }

    /// <summary>A serialized value with its option ids mapped (a list deduplicated), or null when nothing changes.</summary>
    private static string? Rewrite(string? serialized, IReadOnlyDictionary<string, string> map,
        StandardValueType dbValueType)
    {
        if (string.IsNullOrEmpty(serialized)) return null;
        switch (serialized.DeserializeAsStandardValue(dbValueType))
        {
            case string id when map.TryGetValue(id, out var target):
                return target.SerializeAsStandardValue(dbValueType);
            case List<string> ids when ids.Any(map.ContainsKey):
                return ids.Select(i => map.GetValueOrDefault(i, i)).Distinct(StringComparer.Ordinal).ToList()
                    .SerializeAsStandardValue(dbValueType);
            default:
                return null;
        }
    }

    /// <summary>
    /// <paramref name="content"/> with <paramref name="current"/>'s option <paramref name="id"/> added at the end of its
    /// list; a node under the counterpart of its parent's key path, with whatever part of that path is missing.
    /// </summary>
    private static CustomPropertyContentV1 WithOptionOf(CustomPropertyContentV1 content, CustomPropertyContentV1 current,
        string id, bool ignoreCase)
    {
        if (current.Choices.FirstOrDefault(c => c.Uuid == id) is { } choice)
            return content with { Choices = [..content.Choices, choice] };
        if (current.Tags.FirstOrDefault(t => t.Uuid == id) is { } tag) return content with { Tags = [..content.Tags, tag] };
        var path = PathTo(current.Nodes, id) ??
                   throw new InvalidOperationException($"Option {id} is not an option of the property.");
        return content with { Nodes = Graft(content.Nodes, path, 0) };

        IReadOnlyList<CustomPropertyNodeV1> Graft(IReadOnlyList<CustomPropertyNodeV1> level,
            IReadOnlyList<CustomPropertyNodeV1> nodes, int depth)
        {
            var key = ChildClasses.KeyOf(nodes[depth], ignoreCase);
            var index = depth == nodes.Count - 1
                ? -1
                : level.ToList().FindIndex(n => ChildClasses.KeyOf(n, ignoreCase) == key);
            if (index < 0) return [..level, Chain(nodes, depth)];
            var grafted = level.ToArray();
            grafted[index] = grafted[index] with { Children = Graft(grafted[index].Children, nodes, depth + 1) };
            return grafted;
        }

        static CustomPropertyNodeV1 Chain(IReadOnlyList<CustomPropertyNodeV1> nodes, int depth) => nodes[depth] with
        {
            Children = depth == nodes.Count - 1 ? [] : [Chain(nodes, depth + 1)],
        };
    }

    /// <summary>The nodes from a root down to the first node (pre-order) with <paramref name="id"/>, or null.</summary>
    private static IReadOnlyList<CustomPropertyNodeV1>? PathTo(IReadOnlyList<CustomPropertyNodeV1> nodes, string id)
    {
        foreach (var node in nodes)
        {
            if (node.Uuid == id) return [node];
            if (PathTo(node.Children, id) is { } below) return [node, ..below];
        }

        return null;
    }

    /// <summary>
    /// A property's options by label class (§3.4): every id, each id's class key (a node's is its key path), and for each
    /// class key the id of its first member that has one.
    /// </summary>
    private sealed record OptionKeys(HashSet<string> Ids, Dictionary<string, string> KeyById,
        Dictionary<string, string> IdByKey)
    {
        public static OptionKeys Of(CustomPropertyContentV1 content, bool ignoreCase)
        {
            var keys = new OptionKeys(new HashSet<string>(StringComparer.Ordinal),
                new Dictionary<string, string>(StringComparer.Ordinal), new Dictionary<string, string>(StringComparer.Ordinal));
            foreach (var choice in content.Choices) keys.Add(choice.Uuid, ChildClasses.KeyOf(choice, ignoreCase));
            foreach (var tag in content.Tags)
            {
                var key = ChildClasses.KeyOf(tag, ignoreCase);
                keys.Add(tag.Uuid, key.Group + "\0" + key.Name);
            }

            Walk(content.Nodes, "");
            return keys;

            void Walk(IReadOnlyList<CustomPropertyNodeV1> nodes, string parentPath)
            {
                foreach (var node in nodes)
                {
                    var path = parentPath + "\u0001" + ChildClasses.KeyOf(node, ignoreCase);
                    keys.Add(node.Uuid, path);
                    Walk(node.Children, path);
                }
            }
        }

        private void Add(string? id, string key)
        {
            if (string.IsNullOrEmpty(id)) return;
            Ids.Add(id);
            KeyById.TryAdd(id, key);
            IdByKey.TryAdd(key, id);
        }
    }

    /// <summary>
    /// Whether converting <paramref name="dbValue"/> loses information: its business value, converted to the new type
    /// and back, is no longer what it was (compared serialized, so a value that displays alike but loses a part, such
    /// as a label that holds the list separator, counts too). Anything that fails to convert counts as lost.
    /// </summary>
    private static bool IsLossy(Bakabase.Abstractions.Models.Domain.Property property, object? dbValue,
        StandardValueType fromBizType, StandardValueType toBizType)
    {
        try
        {
            var biz = PropertySystem.Property.ToBizValue(property, dbValue);
            if (biz is null) return false;
            var back = StandardValueSystem.Convert(StandardValueSystem.Convert(biz, fromBizType, toBizType), toBizType,
                fromBizType);
            return !string.Equals(biz.SerializeAsStandardValue(fromBizType), back?.SerializeAsStandardValue(fromBizType),
                StringComparison.Ordinal);
        }
        catch (Exception e) when (e is ArgumentException or InvalidCastException or FormatException
                                      or OverflowException or InvalidOperationException or NotSupportedException
                                      or NullReferenceException or KeyNotFoundException)
        {
            return true;
        }
    }

    private static PropertyType ParseSubtype(string subtype) =>
        CustomPropertyTypes.TryParse(subtype ?? "", out var type) && CustomPropertyContentMapper.Supports(type)
            ? type
            : throw new ArgumentException($"'{subtype}' is not a property type.", nameof(subtype));

    private static string KeyOf(int id) => id.ToString(CultureInfo.InvariantCulture);

    private static int? TryIdOf(string localKey) =>
        int.TryParse(localKey, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;

    private static int IdOf(string localKey) =>
        TryIdOf(localKey) ?? throw new ArgumentException($"'{localKey}' is not a custom property id.", nameof(localKey));

    /// <summary>
    /// The rows one batch checks its hashes against, read once; a row this batch wrote is read again, so an entity the
    /// batch touched twice is never judged by its old content.
    /// </summary>
    private sealed class RowSnapshot(CustomPropertyDataSyncKind<TDbContext> kind)
    {
        private Dictionary<int, CustomPropertyDbModel>? _rows;
        private readonly HashSet<int> _written = [];

        public void MarkWritten(int id) => _written.Add(id);

        /// <summary>The row while it exists, reads and hashes to <paramref name="expectedLocalHash"/>; else null.</summary>
        public async Task<CustomPropertyDbModel?> CurrentAsync(string localKey, string expectedLocalHash)
        {
            if (TryIdOf(localKey) is not { } id) return null;
            CustomPropertyDbModel? row;
            if (_written.Contains(id))
            {
                row = await kind.RowAsync(id);
            }
            else
            {
                _rows ??= (await kind._properties.GetAllDbModels())
                    .Where(r => CustomPropertyContentMapper.Supports(r.Type)).ToDictionary(r => r.Id);
                row = _rows.GetValueOrDefault(id);
            }

            if (row is null) return null;
            var local = ToLocalEntity(row, 0);
            return !local.Unreadable &&
                   string.Equals(ContentHash.Of(local.Content), expectedLocalHash, StringComparison.Ordinal)
                ? row
                : null;
        }
    }
}
