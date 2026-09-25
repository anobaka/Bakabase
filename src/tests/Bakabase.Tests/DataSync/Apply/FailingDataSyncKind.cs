using System.Text.Json.Nodes;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.DataSync.Kinds;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Kinds.ExtensionGroups;
using Bootstrap.Components.Orm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests.DataSync.Apply;

/// <summary>
/// Failure injection for the apply transaction (v3.1 H6, §13.5): a failure at a chosen operation of the adapter, a
/// write between the merge and the adapter (a hash that no longer holds), or a failure when the scope's context saves
/// a chosen kind of row (a base, a pending record, an inbox item, the history, the link).
/// </summary>
internal sealed class DataSyncFailureInjector
{
    /// <summary>Fails a save of the scope's context while it holds such a change.</summary>
    public Func<ChangeTracker, bool>? FailSave { get; set; }

    /// <summary>Fails the operation for which it returns true (counted from 0 across batches).</summary>
    public Func<ApplyOperation, int, bool>? FailOperation { get; set; }

    /// <summary>
    /// Runs before the adapter executes a batch, with the scope's own service: a local writer landing between the merge
    /// and the write, inside the apply's transaction.
    /// </summary>
    public Func<ApplyBatch, IExtensionGroupService, Task>? BeforeBatch { get; set; }

    public int Operations { get; set; }

    public void Attach(DbContext db) => db.SavingChanges += (_, _) =>
    {
        if (FailSave?.Invoke(db.ChangeTracker) == true) throw new InvalidOperationException("injected save failure");
    };

    public static bool Saves<T>(ChangeTracker tracker, Func<T, bool>? which = null) where T : class =>
        tracker.Entries<T>().Any(e => e.State is EntityState.Added or EntityState.Modified &&
                                      (which is null || which(e.Entity)));
}

/// <summary>The real extension group adapter with <see cref="DataSyncFailureInjector"/> in front of it.</summary>
internal sealed class FailingDataSyncKind(IDataSyncKind inner, DataSyncFailureInjector injector,
    IExtensionGroupService service) : IDataSyncKind
{
    public IDataSyncKindCodec Codec => inner.Codec;

    /// <summary>Registers the extension group kind behind the injector, attached to every scope's context.</summary>
    public static void AddExtensionGroups(IServiceCollection services, DataSyncFailureInjector injector) =>
        services.AddScoped<IDataSyncKind>(sp =>
        {
            injector.Attach(sp.GetRequiredService<BakabaseDbContext>());
            return new FailingDataSyncKind(new ExtensionGroupDataSyncKind(ExtensionGroupCodec.Instance,
                sp.GetRequiredService<IExtensionGroupService>(),
                sp.GetRequiredService<FullMemoryCacheResourceService<BakabaseDbContext, ExtensionGroupDbModel, int>>()),
                injector, sp.GetRequiredService<IExtensionGroupService>());
        });

    public async Task<ApplyBatchOutcome> ApplyAsync(ApplyBatch batch, CancellationToken ct)
    {
        if (injector.BeforeBatch is { } before) await before(batch, service);
        foreach (var op in batch.Operations)
        {
            if (injector.FailOperation?.Invoke(op, injector.Operations) == true)
                throw new InvalidOperationException("injected operation failure");
            injector.Operations++;
        }

        return await inner.ApplyAsync(batch, ct);
    }

    public Task<IReadOnlyList<LocalEntity>> ReadAsync(IReadOnlyCollection<string>? localKeys, CancellationToken ct) =>
        inner.ReadAsync(localKeys, ct);

    public Task<IReadOnlyDictionary<string, JsonObject>> CapturePreImageAsync(IReadOnlyCollection<string> localKeys,
        CancellationToken ct) => inner.CapturePreImageAsync(localKeys, ct);

    public Task<IReadOnlyDictionary<string, EntityUsage>> GetUsageAsync(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> childIdsByLocalKey, CancellationToken ct) =>
        inner.GetUsageAsync(childIdsByLocalKey, ct);

    public Task RestoreAsync(string localKey, JsonObject preImage, CancellationToken ct) =>
        inner.RestoreAsync(localKey, preImage, ct);

    public Task DeleteAsync(string localKey, CancellationToken ct) => inner.DeleteAsync(localKey, ct);

    public int CacheResets { get; private set; }

    public void ResetCaches()
    {
        CacheResets++;
        inner.ResetCaches();
    }

    public Task<IReadOnlyList<string>> ReadOrderAsync(CancellationToken ct) => inner.ReadOrderAsync(ct);

    public Task ApplyOrderAsync(IReadOnlyList<string> syncedLocalKeysInSharedOrder, CancellationToken ct) =>
        inner.ApplyOrderAsync(syncedLocalKeysInSharedOrder, ct);

    public Task ChangeSubtypeAsync(string localKey, string subtype, CancellationToken ct) =>
        inner.ChangeSubtypeAsync(localKey, subtype, ct);

    public Task<DataSyncTypeChangePreview> PreviewSubtypeChangeAsync(string localKey, string subtype,
        CancellationToken ct) => inner.PreviewSubtypeChangeAsync(localKey, subtype, ct);

    public Task<IReadOnlyDictionary<string, string>> ReadRawHashesAsync(CancellationToken ct) =>
        inner.ReadRawHashesAsync(ct);
}
