using System.Data.Common;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Workflow.Artifacts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Bakabase.Tests.Artifacts;

public sealed partial class ArtifactOperationStoreTests
{
    [TestMethod]
    public async Task File_reference_guards_project_path_evidence_without_loading_processing_metadata()
    {
        var registration = await Files("reference-projection");
        var target = await Adopt("reference-target", registration);
        var alias = await Adopt("reference-alias", registration);
        await Adopt("reference-unrelated", await Files("reference-unrelated"));
        await _db.Set<ArtifactDbModel>().ExecuteUpdateAsync(setters => setters
            .SetProperty(a => a.MetadataJson, "{\"unrelatedProcessingDetails\":\"" + new string('x', 32768) + "\"}"));

        var queries = new ReferenceQueryObserver();
        await using var context = new BakabaseDbContext(new DbContextOptionsBuilder<BakabaseDbContext>()
            .UseSqlite("Data Source=" + Path.Combine(_root, "artifacts.db")).AddInterceptors(queries).Options);
        var store = Store(context);
        Assert.IsTrue(await store.HasRegisteredFilesAsync(target.Files));
        Assert.IsTrue(await store.HasOtherRegisteredFilesAsync(target.Files, target.Id));
        Assert.IsFalse(await store.HasRegisteredFilesAsync([Path.Combine(_root, "unregistered.txt")]));
        Assert.IsTrue(await store.AttachConsumerAsync(alias.OperationId, "other-owner", LeaseTime));
        Assert.IsFalse(await store.TryQuarantineAsync(target.Id), "A shared file's live consumer must still protect the target.");
        await store.ReleaseConsumerAsync(alias.OperationId, "other-owner");
        Assert.IsTrue(await store.TryQuarantineAsync(target.Id));
        Assert.IsFalse(await store.AttachConsumerAsync(alias.OperationId, "late-owner", LeaseTime),
            "A new consumer of a shared path must respect the quarantine fence.");

        Assert.IsTrue(queries.ArtifactSelects.Count >= 7);
        foreach (var query in queries.ArtifactSelects)
        {
            Assert.IsFalse(query.Contains("MetadataJson", StringComparison.Ordinal), query);
            Assert.IsFalse(query.Contains("CreatedAt", StringComparison.Ordinal), query);
            Assert.IsFalse(query.Contains("UpdatedAt", StringComparison.Ordinal), query);
            Assert.IsTrue(query.Contains("ManifestJson", StringComparison.Ordinal), query);
            Assert.IsTrue(query.Contains("QuarantineSourceRootPath", StringComparison.Ordinal), query);
        }
        Assert.IsTrue(File.Exists(target.Files[0]), "These guards must not move or delete any payload.");
    }

    [TestMethod]
    public async Task Narrow_reference_queries_observe_later_registrations_and_consumers_from_another_context()
    {
        var registration = await Files("fresh-reference");
        var target = await Adopt("fresh-target", registration);
        await using var context = OpenDb();
        var store = Store(context);
        Assert.IsFalse(await store.HasOtherRegisteredFilesAsync(target.Files, target.Id));

        var alias = await Adopt("new-reference", registration);
        Assert.IsTrue(await store.HasOtherRegisteredFilesAsync(target.Files, target.Id),
            "A previous probe must not cache a reference-free result across safety phases.");
        Assert.IsTrue(await _store.AttachConsumerAsync(alias.OperationId, "new-consumer", LeaseTime));
        Assert.IsFalse(await store.TryQuarantineAsync(target.Id),
            "A consumer added by another context after preview must still block retirement.");
        await _store.ReleaseConsumerAsync(alias.OperationId, "new-consumer");
        Assert.IsTrue(await store.TryQuarantineAsync(target.Id));
    }

    [TestMethod]
    public async Task Narrow_reference_queries_keep_quarantine_original_and_current_paths_protected()
    {
        var registration = await Files("quarantine-reference");
        var target = await Adopt("quarantine-reference", registration);
        var alias = await Adopt("original-reference", registration);
        Assert.IsTrue(await _store.TryQuarantineAsync(target.Id));
        var quarantine = Path.Combine(_appData, "quarantine", target.Id);
        var destination = Path.Combine(quarantine, target.Manifest.Entries.Single().RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(target.Files.Single(), destination);
        await _store.SetLocationAsync(target.Id, quarantine, ArtifactLocationState.Quarantined);

        await using var context = OpenDb();
        var store = Store(context);
        Assert.IsTrue(await store.HasOtherRegisteredFilesAsync(target.Files, alias.Id),
            "The original path remains protected while the registered file is in quarantine.");
        Assert.IsTrue(await store.HasRegisteredFilesAsync([destination]));
        Assert.IsFalse(await store.HasOtherRegisteredFilesAsync([destination], target.Id));
        Assert.IsFalse(await store.AttachConsumerAsync(alias.OperationId, "after-move", LeaseTime));
        Assert.IsTrue(File.Exists(destination));
    }

    private sealed class ReferenceQueryObserver : DbCommandInterceptor
    {
        internal List<string> ArtifactSelects { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Artifacts\"", StringComparison.Ordinal))
                ArtifactSelects.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
