using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Current_directory_reads_only_ancestor_manifests_for_other_artifacts(bool useWorkspaces)
    {
        var (task, run, source) = await CurrentDirectoryInput();
        var appData = Path.Combine(_root, "appdata");
        var store = new ArtifactOperationStore(Db, () => appData);
        var file = Path.Combine(_root, "sibling.txt");
        await File.WriteAllTextAsync(file, "another branch");
        var parent = await store.AdoptAsync(new("ancestor", "ancestor", "files.compose", "parent"),
            new(_root, [file], "compositeFiles", false), "test");

        // These represent archives from other tasks, including AppData-relative registrations.
        // A scan of 16 full rows would materialize 8 MiB of unrelated JSON on every check.
        const int count = 16;
        var payload = new string('x', 256 * 1024);
        var unrelated = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var root = Directory.CreateDirectory(Path.Combine(i % 2 == 0 ? appData : _root, "other-" + i)).FullName;
            var path = Path.Combine(root, "payload.bin");
            await File.WriteAllTextAsync(path, "data");
            var id = "unrelated-" + i;
            var row = await store.AdoptAsync(new(id, id, "files.compose", id), new(root, [path], "files", false), "test");
            unrelated.Add(row.Id);
        }
        await Db.Artifacts.Where(a => unrelated.Contains(a.Id)).ExecuteUpdateAsync(update =>
            update.SetProperty(a => a.MetadataJson, payload).SetProperty(a => a.ManifestJson, payload));
        var commands = new List<string>();
        await using var traced = new BakabaseDbContext(new DbContextOptionsBuilder<BakabaseDbContext>()
            .UseSqlite(Db.Database.GetDbConnection()).EnableSensitiveDataLogging()
            .LogTo(commands.Add, [RelationalEventId.CommandExecuted]).Options);
        var tracedStore = new ArtifactOperationStore(traced, () => appData);
        var resolver = useWorkspaces ? new FileProcessingWorkspaceResolver(appData) : null;
        Assert.IsTrue(await AcquisitionCurrentDirectoryFiles.CanReadAsync(traced, tracedStore, new CompositeStorage(),
            resolver, task.Id, run.Id, "main", source, default));

        var queries = commands.Where(q => q.Contains("FROM \"Artifacts\"")).ToArray();
        var roots = queries.Single(q => q.Contains("\"Id\" <>"));
        Assert.IsFalse(roots.Contains("ManifestJson"), roots);
        Assert.IsFalse(roots.Contains("MetadataJson"), roots);
        var related = queries.Where(q => q.Contains("ManifestJson") && !q.Contains(source.Id)).ToArray();
        Assert.AreEqual(1, related.Length, string.Join("\n", related));
        StringAssert.Contains(related[0], parent.Id);
        Assert.IsFalse(related[0].Contains("MetadataJson"), related[0]);
    }
}
